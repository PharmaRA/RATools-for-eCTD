# 发布作业异步执行（Publish Job Async Execution）

## 概述

修复发布作业的并发竞态，并把发布执行移出 HTTP 请求线程。当前 `PublishJobService` 用"先查后建"的方式防止重复活动作业，存在 check-then-act 竞态；同时 `/execute` 端点在请求线程上同步执行整段发布（递归拷贝 + 打包 zip），阻塞请求且无超时。

本设计以数据库约束为竞态的事实来源，把服务层预检降级为 best-effort，并在 InMemory 仓储补齐等价守卫；同时把发布执行改为后台作业，`/execute` 立即返回作业 id 供轮询。

## 目标

1. 以数据库的活动作业部分唯一索引作为防重的事实来源，服务层预检改为 best-effort 友好提示。
2. 为 InMemory 仓储补齐等价的活动作业守卫，使 InMemory 模式下竞态不再产生重复活动作业。
3. 把发布执行移出 HTTP 请求线程，改为后台作业执行。
4. `/execute` 立即返回作业 id，客户端通过既有轮询端点查询状态。
5. 明确并保持 `Pending → Running → Completed/Failed` 的状态机语义。

## 非目标

- 不更换状态枚举的取值（保持 `Pending`、`Running`、`Completed`、`Failed`）。
- 不引入外部消息队列中间件；后台执行基于进程内宿主服务。
- 不改变发布产物的目录布局或 zip 打包逻辑。
- 不改动既有的轮询/报告/产物下载端点契约（仅新增异步语义）。

## 设计

### 当前竞态

服务层预检与作业创建分离，构成 check-then-act 竞态：

- 预检：`src/RATools.Application/Publishing/PublishJobService.cs:431-452` 的 `EnsureNoActivePublishAsync` 分别查询 `Pending` 与 `Running` 作业，任一存在即抛 `PublishJobAlreadyInProgressException`。
- 创建：`PublishJobService.cs:317-323` 的 `ExecuteInternalAsync` 先调用 `EnsureNoActivePublishAsync`，再 `new PublishJob(...)` 并 `repository.AddAsync(job, ...)`。

预检与创建之间存在时间窗口，两个并发请求都可能通过预检并各自创建活动作业。

### 数据库约束作为事实来源

关系型 provider 已有兜底约束（见 `2026-05-29-database-constraints-design.md`）：

- `src/RATools.Infrastructure/Persistence/EfCore/RAToolsDbContext.cs:114-116` 定义了 `(ApplicationId, SequenceNumber)` 上的部分唯一索引，过滤条件为 `Status IN ('Pending', 'Running')`。
- `src/RATools.Infrastructure/Persistence/EfCore/EfCorePublishJobRepository.cs:18-21` 的 `AddAsync` 捕获 `DbUpdateException`，在命中该索引冲突时转换为 `PublishJobAlreadyInProgressException`。

因此在 PostgreSQL 模式下，即使预检通过，数据库插入也会因唯一索引拒绝第二个活动作业。设计上把 `EnsureNoActivePublishAsync` 明确定位为 best-effort 友好提示（正常路径快速返回冲突），把真正的防重职责交给数据库约束与 `AddAsync` 的异常转换。

### InMemory 仓储补齐守卫

InMemory 仓储没有等价约束，竞态真实存在：`src/RATools.Infrastructure/Persistence/InMemory/InMemoryPublishJobRepository.cs:11-15` 的 `AddAsync` 直接 `_items[job.Id] = job`，不检查是否已存在同一应用/序列的活动作业。

补齐方式：在 `AddAsync` 内对 `_items` 做原子检查——在加入前扫描是否已存在相同 `(ApplicationId, SequenceNumber)` 且状态为 `Pending` 或 `Running` 的作业，若存在则抛 `PublishJobAlreadyInProgressException`。由于底层为 `ConcurrentDictionary`，检查与写入需用锁或 `GetOrAdd` 配合保证原子性，等价于数据库的部分唯一索引语义。

### 后台执行

`/execute` 端点当前在请求线程上同步执行整段发布：`src/RATools.Api/Controllers/PublishJobsController.cs:101-117` 的 `Execute` 直接 `await publishJobService.ExecuteAsync(...)` 并返回完整 `PublishExecutionReportDto`。`ExecuteAsync` 内部经由 `ExecuteInternalAsync` 完成校验、递归拷贝已发布文件、复制标准资产、生成 `index-md5.txt` 与 zip 打包，整段在请求线程上运行，阻塞请求且无超时。

改造为后台作业：

- 引入后台执行机制（`IHostedService` + 进程内队列/`Channel`）。`/execute` 改为创建处于 `Pending` 的作业、把作业 id 入队，立即返回作业 id（如 `202 Accepted` 加 `PublishJobDto`），不再等待发布完成。
- 后台宿主服务从队列取出作业 id，执行原 `ExecuteInternalAsync` 的发布流程，并通过状态机推进作业状态。
- 客户端使用既有轮询端点查询进度：`PublishJobsController.cs:20-25` 的 `GetById`、`PublishJobsController.cs:27-47` 的 `GetExecutionReport`、`PublishJobsController.cs:49-54` 的 `GetArtifacts`。轮询端点已存在，无需新增。

后台执行应使用独立于 HTTP 请求的 DI scope（后台服务自建 scope 解析 scoped 的 `IPublishJobService` 与仓储），避免请求结束后 `DbContext` 被释放。

### 状态机与超时

状态枚举已定义为 `Pending=1, Running=2, Completed=3, Failed=4`（`src/RATools.Domain/Publishing/PublishJobStatus.cs:5-8`）。注意终态为 `Completed`（非 `Succeeded`）。领域实体已实现受控转换：

- `MarkRunning`（`src/RATools.Domain/Publishing/PublishJob.cs:52-61`）只允许从 `Pending` 进入 `Running`。
- `MarkCompleted`（`PublishJob.cs:63-78`）只允许从 `Running` 进入 `Completed`。
- `MarkFailed`（`PublishJob.cs:80-93`）允许从非终态进入 `Failed`。

后台执行流程应遵循 `Pending → Running → Completed/Failed`：取出作业后调用 `MarkRunning` 并持久化，发布成功调用 `MarkCompleted`，失败调用 `MarkFailed`。当前 `ExecuteInternalAsync` 在创建作业后并未显式 `MarkRunning`（`PublishJobService.cs:322-323` 之后直接进入校验），异步化时应补上 `Running` 转换，使轮询端能区分排队中与执行中。

超时方面，后台执行应对单个发布作业设置超时（基于 `CancellationToken`），超时则 `MarkFailed` 并记录原因，避免长时间拷贝/打包无限期占用后台线程。

## 测试策略

- 新增 InMemory 仓储并发测试：并发 `AddAsync` 相同应用/序列的活动作业，断言只有一个成功、其余抛 `PublishJobAlreadyInProgressException`。
- 新增/调整服务测试：验证 `EnsureNoActivePublishAsync` 作为 best-effort 提示，且最终防重由 `AddAsync` 保证。
- 新增后台执行测试：`/execute` 立即返回作业 id，作业状态依次经过 `Pending → Running → Completed`，失败路径进入 `Failed`。
- 新增超时测试：模拟长时间发布触发超时，断言作业进入 `Failed` 并记录原因。
- 运行完整后端测试套件。

## 风险

**风险：** 把执行移出请求线程后，客户端若不轮询将无法得知结果，可能误以为发布未发生。

**缓解：** `/execute` 返回作业 id 并配合既有轮询端点；前端改为轮询作业状态与报告。

**风险：** 后台服务复用请求 scope 的 `DbContext`，请求结束后 `DbContext` 被释放导致执行失败。

**缓解：** 后台宿主服务为每个作业创建独立 DI scope，解析 scoped 的服务与仓储。

**风险：** InMemory 守卫的原子性实现不当，仍可能在高并发下漏判。

**缓解：** 使用锁或 `ConcurrentDictionary` 的原子操作保证检查与写入原子；以并发测试验证。

**风险：** 异步化补充 `MarkRunning` 转换若与既有流程顺序不一致，可能触发状态机的 `InvalidOperationException`。

**缓解：** 严格遵循领域实体的受控转换，仅在取出作业、确认处于 `Pending` 后调用 `MarkRunning`；以状态流转测试覆盖。

## 验收标准

发布作业异步执行在满足以下条件时视为完成：

- 服务层预检 `EnsureNoActivePublishAsync` 为 best-effort，最终防重由数据库部分唯一索引与 `AddAsync` 异常转换保证。
- InMemory 仓储 `AddAsync` 具备等价的活动作业守卫，并发下不产生重复活动作业。
- `/execute` 不再在请求线程上同步执行发布，而是创建作业并立即返回作业 id。
- 发布在后台作业中执行，状态依 `Pending → Running → Completed/Failed` 推进，并支持超时。
- 客户端可通过既有轮询端点获取作业状态、报告与产物。
- 新增的并发、异步与超时测试通过。
- 现有后端与前端测试套件仍然通过。
