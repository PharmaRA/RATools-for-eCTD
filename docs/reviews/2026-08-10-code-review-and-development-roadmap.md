# 2026-08-10 全项目代码审阅与后续开发路线图

> 状态：Active
> 审阅基准：`master` / `89f8a4c978cf114f5884380376a020a026d24056`
> 审阅范围：后端、前端、数据库持久化、文件系统操作、发布链路、测试、CI、配置、文档和 Git 历史
> 目的：作为 2026-07-26 已完成路线图之后的新执行基线
> 维护规则：文件引用以路径和方法名为主；行号仅对应上述审阅基准，后续可能漂移

## 1. 结论摘要

RATools-for-eCTD 已经不是原型项目。它具备清晰的 .NET 8 分层、React/Vite 前端、PostgreSQL 持久化、真实 PostgreSQL CI、端到端 smoke、依赖扫描和较高的自动化测试投入。既有 FDA 发布、校验、审计、PDF 检查和多区域骨架可以继续演进，不建议推倒重来。

当前阻碍生产化的主要问题不是功能数量，而是信任边界和状态一致性：

1. 客户端可将工作区外的服务器文件登记为文档，随后读取、发布或删除该文件。
2. 客户端可覆盖发布输出根路径，Application Number 又未限制为安全路径段。
3. 公开仓库仍跟踪运行时上传文件，其中包含一份 PDF。
4. 发布超时/取消可能阻止终态持久化，文档元数据更新也缺少统一事务。
5. 当前 API Key、内存队列和启动迁移模型只适合受控的单实例环境。

因此，下一阶段必须按以下顺序推进：仓库数据处置 -> 文件系统边界 -> 状态与事务一致性 -> 身份和持久作业 -> 查询与前端治理 -> 部署工程 -> EU 完整能力。EU 规则扩张、页面重构和性能优化不得挤占前三阶段。

## 2. 审阅与验证基线

### 2.1 已验证

- 前端 `npm run lint` 通过。
- 前端 `npm run build` 通过，但 Ant Design 主 chunk 约 1.19 MB，超过当前 1.10 MB 告警线。
- 前端 `npm run test` 通过：59 个测试文件、421 项测试。
- 安装 SDK 后，本地 `dotnet restore` 和 Release `dotnet build` 通过；构建为 0 error、39 warnings。
- Python 3.14.7 下通过 `py scripts/tests/test_publish_jobs_controller_contract.py`。
- CI 包含 .NET 8 Release 构建、真实 PostgreSQL 测试、Python API 契约测试、前端 lint/build/test、NuGet/npm 漏洞扫描。
- Smoke workflow 覆盖真实 PostgreSQL 下的应用、序列、上传、验证、后台发布、报告、制品、历史和审计链路。
- `git diff --check` 通过；审阅期间未修改生产代码。
- 代码中未发现成片的 `TODO`/`FIXME` 占位实现，也未发现生产路径上的同步 `.Result`/`.Wait()` 调用。

### 2.2 尚未能本地验证

SDK 与 Python 安装后已经补跑 restore、build 和 Python 契约脚本。后端测试宿主仍无法启动：项目目标为 `net8.0`，本机有 `Microsoft.NETCore.App 8.0.29`，但缺少 `Microsoft.AspNetCore.App 8.x`，当前只有 `Microsoft.AspNetCore.App 10.0.10`。需要并行安装 .NET 8 SDK 或 ASP.NET Core Runtime 8.x，并确认 `dotnet --list-runtimes` 出现 `Microsoft.AspNetCore.App 8.0.x` 后，再执行第 8 节后端测试。

### 2.3 风险级别

| 级别 | 定义 | 执行要求 |
|---|---|---|
| P0 | 可导致数据泄露、越权文件操作或公开仓库数据事件 | 停止功能扩张，立即处置 |
| P1 | 可导致任务永久卡住、数据与文件不一致或主要流程不可用 | P0 后紧接修复 |
| P2 | 契约、扩展性或部署边界问题 | 进入下一生产化里程碑 |
| P3 | 可维护性、测试信噪比和性能预算问题 | 不阻塞 P0/P1 |

## 3. 审阅发现

### F-01 [P0] 原始文档路径可绕过工作区边界

**证据链**

- [`POST /api/documents`](../../src/RATools.Api/Controllers/DocumentsController.cs#L32) 接受客户端提供的 `StoragePath`。
- [`DocumentService.CreateAsync`](../../src/RATools.Application/Documents/DocumentService.cs#L21) 直接持久化该路径，没有调用 `IWorkspacePathPolicy`。
- [`DocumentPlacementService.CreateAsync`](../../src/RATools.Application/Documents/DocumentPlacementService.cs#L19) 只检查 Document、Application 和 Sequence 是否存在，没有验证文档路径属于该应用/序列。
- [`SequenceValidationService`](../../src/RATools.Application/Validation/SequenceValidationService.cs#L294) 只检查 `File.Exists`。
- [`EctdPackageModelBuilder`](../../src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs#L130) 将 `document.StoragePath` 作为发布源，随后 [`LocalBackboneFileWriter`](../../src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs#L71) 打开并复制该文件。
- 未挂载的文档可进入 [`DocumentService.DeleteAsync`](../../src/RATools.Application/Documents/DocumentService.cs#L151)，其中在路径策略执行前直接调用 `File.Delete(document.StoragePath)`。

**影响**

持有当前共享 API Key 的调用方可以在进程权限范围内登记任意绝对路径，将文件装入发布包并下载；如果文档尚未挂载，还可以通过删除文档删除工作区外的单个文件。这违反 README 所描述的“所有工作区读写删除都受 AllowedWorkspaceRoots 保护”的边界。

**修复方向**

- 删除公共原始创建端点，或将其改为仅供受信任迁移/导入流程调用的内部能力。
- 所有正常文档必须由上传/导入服务产生服务端路径、大小和散列，客户端不能声明这些事实。
- 在文档挂载、验证、发布、下载和删除前统一执行路径白名单、应用归属、序列归属和 reparse point/symlink 检查。
- 删除流程必须先验证文件路径，再删除数据库记录和物理文件。

**验收条件**

- [x] API 无法登记白名单外绝对路径或相对逃逸路径。（提交：`security: retire raw document creation`、`security: verify cross-platform path boundaries`）
- [x] 白名单外文档无法挂载、验证、发布或删除。（提交：`security: enforce document workspace ownership`）
- [x] Windows junction/symlink 与 Linux symlink 用例均不能逃逸允许根。（提交：`security: verify cross-platform path boundaries`）
- [x] 集成测试证明目标文件在所有拒绝场景中保持不变。（提交：`security: verify cross-platform path boundaries`）

### F-02 [P0] 发布输出路径和 Application Number 可造成路径逃逸

**证据链**

- [`CreatePublishJobRequestBody.OutputDirectoryPath`](../../src/RATools.Api/Contracts/CreatePublishJobRequestBody.cs#L15) 只要求非空。
- [`BackboneService`](../../src/RATools.Application/Publishing/BackboneService.cs#L42) 将该路径原样传给 writer。
- [`LocalBackboneFileWriter`](../../src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs#L46) 优先采用请求路径，并在其下创建目录、写文件、覆盖 zip 和清理 `_jobs` 子树。
- [`SubmissionApplication`](../../src/RATools.Domain/Applications/SubmissionApplication.cs#L29) 对 Application Number 只做空白/长度类约束，而 writer 将其直接用作路径段。

**影响**

调用方可以选择进程有权限的任意输出根；包含分隔符或 `..` 的 Application Number 还可能改变预期目录结构。结果包括任意位置创建/覆盖发布文件，以及在构造出的 `_jobs` 目录中递归清理历史作业。

**修复方向**

- 推荐从公开发布请求中移除物理 `OutputDirectoryPath`，只允许使用服务端配置的输出根；确需多目标时使用服务端登记的 destination ID。
- Application Number 必须验证为安全单路径段，或使用独立、不可控的存储键映射目录。
- writer 对最终 root、application root、job root、artifact 和 package 路径逐层验证包含关系。
- 所有递归删除继续拒绝 reparse point，并加入跨平台逃逸测试。

**验收条件**

- [x] 请求不能选择配置白名单外的输出位置。（提交：`security: enforce configured publish destination`）
- [x] `..`、绝对路径、UNC、驱动器路径、混合分隔符和保留设备名全部被拒绝。（提交：`security: contain publish output paths`）
- [x] 发布、覆盖和保留清理都不能越过服务端批准的 application root。（提交：`security: contain publish output paths`、`security: verify cross-platform path boundaries`）

### F-03 [P0] 公开仓库跟踪运行时上传数据

**证据链**

- 远端 `https://github.com/PharmaRA/RATools-for-eCTD.git` 在审阅日为 public。
- `git ls-files 'src/RATools.Api/App_Data/uploads/**'` 返回 29 个文件，共 458,808 bytes，其中 1 个 PDF。
- [`.gitignore`](../../.gitignore#L16) 已忽略该目录，但 ignore 不会移除历史中已经跟踪的文件。

**影响**

无法仅凭文件名判断该 PDF 是否包含受版权、保密或个人信息约束的内容。公开历史意味着只删除当前分支文件不能撤回已经传播的对象。

**修复方向**

- 立即确认文件来源、所有者、授权和敏感性；在结论前按潜在数据事件处置。
- 从当前版本移除全部运行时上传文件，smoke fixture 改在临时目录中生成。
- 若文件敏感或无公开授权，协调维护者使用 `git filter-repo` 重写所有远端历史并通知已有 clone 使用者；该动作必须单独批准和协调，不能在普通修复提交中直接执行。
- CI 增加禁止跟踪 `App_Data/uploads`、发布输出、数据库文件和密钥文件的守卫。

**验收条件**

- [x] `git ls-files 'src/RATools.Api/App_Data/uploads/**'` 输出为空。
- [ ] 仓库数据/密钥扫描通过。
- [x] 敏感性结论与是否重写历史的决策被记录。
- [x] CI 对重新提交运行时数据直接失败。

### F-04 [P1] 发布取消/超时可能阻止终态持久化

**证据链**

- [`PublishJobBackgroundService`](../../src/RATools.Infrastructure/Publishing/PublishJobBackgroundService.cs#L42) 使用 15 分钟链接 token 执行作业。
- 成功路径先 `MarkCompleted`，再用执行 token 写审计，最后才 [`repository.UpdateAsync`](../../src/RATools.Application/Publishing/PublishJobService.cs#L314)。
- 异常路径先 `MarkFailed`，仍用同一个 token 写审计，最后才 [`repository.UpdateAsync`](../../src/RATools.Application/Publishing/PublishJobService.cs#L326)。
- [`TryWriteAuditAsync`](../../src/RATools.Application/Publishing/PublishJobService.cs#L374) 明确不吞掉 `OperationCanceledException`。

**影响**

token 已取消时，审计调用可在终态保存前中断控制流，使数据库继续保留 Pending/Running。成功路径发生取消时还可能对已 Completed 的域对象再次 `MarkFailed`，产生二次异常。

**修复方向**

- 显式区分 `OperationCanceledException` 与普通失败。
- 使用独立、短时且不继承执行取消状态的 cleanup token 持久化 Failed/Completed。
- 先提交任务终态，再 best-effort 写审计；审计失败必须留结构化日志，但不能回滚已经完成的任务。
- 对验证、readiness、writer、审计和 repository 分别增加取消/故障注入测试。

**验收条件**

- [x] 取消、超时和 host stopping 后作业最终均为 Completed 或 Failed，不存在永久活动状态。（提交：`reliability: persist publish terminal states`）
- [x] 审计写入失败不改变业务终态。（提交：`reliability: persist publish terminal states`）
- [x] 同一序列在失败终态落库后可再次发布。（提交：`reliability: persist publish terminal states`）

### F-05 [P1] 元数据重命名跨两个 SaveChanges，补偿不恢复数据库

**证据链**

- 原实现中的 [`DocumentPlacementService.UpdateMetadataAsync`](../../src/RATools.Application/Documents/DocumentPlacementService.cs) 先更新 Document，再更新 Placement。
- 两个 EF repository 都在各自的 `UpdateAsync` 中立即 `SaveChangesAsync`：[`EfCoreDocumentRepository`](../../src/RATools.Infrastructure/Persistence/EfCore/EfCoreDocumentRepository.cs#L15) 和 [`EfCoreDocumentPlacementRepository`](../../src/RATools.Infrastructure/Persistence/EfCore/EfCoreDocumentPlacementRepository.cs#L15)。
- 原实现第二次更新失败后只恢复内存对象并尝试移回文件，没有再次持久化原 Document 状态。

**影响**

数据库可能仍指向新文件名/新路径，而物理文件已经移回旧路径；后续验证、下载和发布将出现不一致。

**修复方向**

- 引入 Application 层 unit of work，让 Document 与 Placement 在单个数据库事务中提交。
- 文件移动位于事务外时采用明确的补偿流程，并使用独立 cleanup token。
- 增加第二次更新失败、数据库提交失败、文件移回失败和并发删除的故障注入测试。

**验收条件**

- [x] 任意阶段失败后，Document、Placement 和物理文件要么全部为旧状态，要么在文件无法回移时全部保留为新状态；并发删除会显式报告补偿不完整。（提交：`reliability: make metadata updates atomic`）
- [x] SQLite 覆盖跨多次 `SaveChanges` 的提交/回滚；真实 PostgreSQL 测试覆盖同一事务的回滚行为。（提交：`reliability: make metadata updates atomic`）

### F-06 [P1] 制品下载绕过 API Key 注入

**证据链**

- [`apiFetch`](../../frontend/src/apiClient.ts#L50) 仅对 JavaScript fetch 注入 `X-RA-Tools-Api-Key`。
- [`artifactDisplay`](../../frontend/src/components/publishing/artifactDisplay.tsx#L36)、[`PackageReviewPanel`](../../frontend/src/components/publishing/PackageReviewPanel.tsx#L207)、[`PublishProgressCard`](../../frontend/src/pages/workspace/PublishProgressCard.tsx#L75) 和 [`ReportPanel`](../../frontend/src/components/publishing/ReportPanel.tsx#L193) 使用普通 `href`。
- [`ApiKeyAuthenticationHandler`](../../src/RATools.Api/Security/ApiKeyAuthenticationHandler.cs#L27) 要求请求头携带 API Key。

**影响**

启用认证后，页面内查询可以成功，但浏览器直接下载报告或 zip 会收到 401。

**修复方向**

- 短期：提供统一 `downloadArtifact`，使用 `apiFetch` 获取 Blob，再通过 object URL 触发下载并及时 revoke。
- 长期：若进入共享部署，改用 HttpOnly secure cookie/session 或受限时效签名 URL，不在浏览器包中放共享密钥。

**验收条件**

- [x] API Key 必填环境中，四个下载入口均可下载并保持正确文件名。（提交：`security: authenticate artifact downloads`）
- [x] 401、410 和网络错误有一致 UI 反馈。（提交：`security: authenticate artifact downloads`）
- [x] 测试验证请求头或真实下载行为，不再只断言 href 字符串。（提交：`security: authenticate artifact downloads`）

### F-07 [P2] Publish Job 创建契约与实现语义漂移

**证据链**

- [`POST /api/publish-jobs`](../../src/RATools.Api/Controllers/PublishJobsController.cs#L83) 返回 201，并调用 `CreateAsync`。
- [`PublishJobService.CreateAsync`](../../src/RATools.Application/Publishing/PublishJobService.cs#L29) 实际调用 `ExecuteInternalAsync`，同步运行完整流程。
- [Python 契约脚本](../../scripts/tests/test_publish_jobs_controller_contract.py#L43) 只检查 attribute 和响应 DTO，不验证运行行为。
- 同一 controller 已存在明确的异步 [`POST /api/publish-jobs/execute`](../../src/RATools.Api/Controllers/PublishJobsController.cs#L101)。

以上是原问题记录；当前同步入口已停止执行，保留路由仅返回 `410 Gone` 迁移提示，唯一创建并入队的命令是 `/execute`。

**建议决策**

优先保留单一异步命令：逐步弃用同步 `POST /api/publish-jobs`，由 `/execute` 创建 Pending 资源并入队。若业务必须保留两阶段协议，则 `POST /api/publish-jobs` 只能创建资源，并新增 `POST /api/publish-jobs/{id}/execute`。不要继续保留两个表面不同、行为重叠的入口。

**验收条件**

- [x] Controller、OpenAPI、README、HTTP 示例和行为测试统一描述 `/execute` 的 `202 Accepted` 异步入队语义；旧入口返回 `410 Gone`。（提交：`api: unify publish job command contract`）
- [x] 契约脚本与 controller 行为测试验证旧入口不会调用发布服务，并能发现响应语义漂移。（提交：`api: unify publish job command contract`）

### F-08 [P2] 发布历史查询执行全量数据库和磁盘扫描

**证据链**

- [`ApplicationPublishHistoryService`](../../src/RATools.Application/Applications/ApplicationPublishHistoryService.cs#L30) 固定请求 page 1 / `int.MaxValue`。
- 它逐个任务读取并反序列化报告 JSON，再在内存中过滤 readiness 和分页。

**影响**

每次 UI 翻页都是 O(全部作业 + 全部报告磁盘 I/O)，数据量增长后会显著拖慢 API，并增加损坏报告导致的不稳定性。

**修复方向**

- 发布完成时把 validation/readiness/artifact/lifecycle 摘要物化到数据库。
- repository 直接按 status、readiness、时间和 sequence 在 SQL 中过滤、聚合和分页。
- 报告 JSON 保留为详情证据，不作为列表查询的数据源。

### F-09 [P2，部署相关] 浏览器共享 API Key 与客户端可写审计不构成多用户安全模型

**证据链**

- [`frontend/.env.development`](../../frontend/.env.development#L1) 通过 `VITE_API_KEY` 注入密钥；Vite 环境变量会进入浏览器构建产物，不能视为秘密。
- [`AuditLogsController.Create`](../../src/RATools.Api/Controllers/AuditLogsController.cs#L31) 接受客户端提供的 Actor 和 Details，可创建任意审计记录。

**边界判断**

若产品严格限定为单机、单用户、受控网络工具，这属于已知限制；一旦进入共享浏览器部署或多用户环境，就必须在上线前替换。

**修复方向**

- 明确部署 ADR：local-only 或 shared deployment。
- shared 模式采用 OIDC/session、角色授权、CSRF 防护和服务端身份派生。
- 移除公共审计创建端点，或只允许内部服务通过不可伪造的调用路径写入。

### F-10 [P2，部署相关] 发布队列和启动恢复仅支持单实例

**证据链**

- [`ChannelPublishJobQueue`](../../src/RATools.Infrastructure/Publishing/ChannelPublishJobQueue.cs) 已改为可配置容量、满载等待的进程内单消费者队列；它仍不是跨实例持久队列。
- [`StalePublishJobRecoveryService`](../../src/RATools.Infrastructure/Publishing/StalePublishJobRecoveryService.cs#L11) 在启动时把全部活动作业标记为 Failed，前提是假定没有其他实例正在执行。
- [`Program.cs`](../../src/RATools.Api/Program.cs#L78) 在每个应用实例启动时直接执行 `Database.Migrate()`。

**影响**

当前模型可接受单实例重启，但多实例时一个实例可能错误回收另一个实例的作业；并发启动迁移也不应作为正式发布策略。

**修复方向**

- 需要水平扩展前，采用 DB-backed outbox/queue、claim lease、worker instance ID、heartbeat 和幂等执行。
- 启动恢复只回收租约过期任务，不回收全部活动行。
- 数据库迁移改为部署阶段的单独 job。

### F-11 [P3] 前端数据生命周期和测试信噪比需要治理

**证据**

- 多个页面使用自定义 `useEffect` loader，没有统一取消、缓存、去重和 stale response 防护。
- 测试虽然全部通过，但输出包含 React `act`、弃用的 `ReactDOMTestUtils.act` 和 Ant Design 废弃属性警告。
- 大型页面和测试文件继续增长，Ant Design chunk 已超过构建告警线。

**修复方向**

- 在 P0/P1 完成后渐进引入 TanStack Query，统一 query key、取消、缓存和 mutation invalidation。
- 按业务 hook/面板拆分 `SequenceWorkspacePage`、`ApplicationsPage` 等大型页面。
- 清理现有警告，并让测试对未列入白名单的新 console warning 失败。
- 先分析 bundle，再决定按路由/组件拆包，不为追求数字进行脆弱的 manualChunks 配置。

### F-12 [P3] Release 构建仍有 39 个 warning

**证据**

- SDK 安装后的 Release build 成功，但输出 39 个编译器/分析器 warning。
- [`Directory.Build.props`](../../Directory.Build.props#L14) 设置了 `TreatWarningsAsErrors=false`。
- [`PdfPigPdfInspector`](../../src/RATools.Infrastructure/Publishing/Validation/Pdf/PdfPigPdfInspector.cs#L29) 有两个 `CS8602`，因为 `TryGetBookmarks` 的 out 值在 nullable 分析中仍可能为 null。
- 其余主要为 CA1512、CA1711、CA1716、CA18xx 等 API 命名、分配和性能建议。

**修复方向**

- 先处理可能影响正确性的编译器/nullability warning，再按规则类别批量处理分析器 warning。
- 对明确有意保留的公共命名使用带理由的局部 suppression，不做无意义的破坏性重命名。
- 清零后启用 `TreatWarningsAsErrors=true`，CI 使用与项目目标一致的 .NET 8 SDK 再验证一次。

**验收条件**

- [ ] Release build 为 0 error、0 warning。
- [ ] `TreatWarningsAsErrors=true` 后完整后端测试仍通过。

## 4. 决策点

| ID | 决策 | 最晚时点 | 默认建议 |
|---|---|---|---|
| D-01 | 公开 PDF 是否敏感/有公开授权，是否重写 Git 历史 | Phase 0 开始前 | 无法证明可公开时按敏感处理 |
| D-02 | 产品是严格 local-only，还是未来 shared deployment | Phase 3 前 | **已决策**：当前仅支持 local-only；shared 另立安全里程碑，见 [ADR-0001](../architecture/0001-local-only-deployment-boundary.md) |
| D-03 | Publish Job 采用单一异步命令，还是两阶段创建/执行 | Phase 2 前 | 单一异步命令，弃用同步入口 |
| D-04 | EU M1 完整规则、官方资产和版本维护来源 | Phase 6 前 | 没有权威来源时保持受控骨架，不宣称完整合规 |

## 5. 分阶段开发计划

### Phase 0：仓库数据处置和防复发（1-2 天）

**目标**：先处理潜在公开数据事件，并确保运行时数据不能再次进入版本库。

- [x] 完成 D-01，记录文件来源、授权、敏感性和负责角色；见[公开仓库数据事件处置记录](../security/2026-08-10-public-repository-data-incident.md)。
- [x] 从 HEAD 移除 `App_Data/uploads` 下全部已跟踪文件，保留目录结构只使用 `.gitkeep`（如确有需要）。
- [x] 根据 D-01 决定协调重写远端历史；实际 force push 等待仓库管理员明确批准，不在普通提交中执行。
- [x] 修正 `.gitignore` 的意图：正式文档使用可跟踪的 `docs/reviews`/`docs/section-dictionary`，`reference` 与 `scripts/tests` 不再被误忽略；本地 agent 草稿和生成工具目录继续忽略。
- [x] 新增 repository hygiene 测试或 CI 脚本，禁止 runtime data、数据库和密钥被跟踪。

**Stop gate**：F-03 全部验收条件通过，远端处置决策已记录。

### Phase 1：文件系统信任边界（3-5 天）

**目标**：证明所有文件读取、写入、复制、删除和递归清理都不能逃逸服务端批准的根。

- [x] 修复 F-01：退役/限制 raw document create；路径和散列由服务端生成。（提交：`security: retire raw document creation`）
- [x] 修复 F-01：挂载、验证、发布和删除统一执行 workspace + application + sequence ownership 检查。（提交：`security: enforce document workspace ownership`）
- [x] 修复 F-02：移除任意输出路径，或映射为受控 destination ID。（提交：`security: enforce configured publish destination`）
- [x] 修复 F-02：Application Number 使用安全存储段，所有最终路径做 containment 检查。（提交：`security: contain publish output paths`）
- [x] 修复 F-06：提供鉴权 Blob 下载 helper，并替换所有普通 href。（提交：`security: authenticate artifact downloads`）
- [x] 为 Windows/Linux 增加路径遍历、UNC、大小写、symlink/junction/reparse point 对抗性集成测试，并在 CI 的 `ubuntu-latest` 与 `windows-latest` 矩阵执行。（提交：`security: verify cross-platform path boundaries`）

**Stop gate**：恶意路径矩阵全部通过；测试证明白名单外文件内容和元数据均未变化。

### Phase 2：作业状态和数据库/文件事务（3-5 天）

**目标**：任何取消、异常和并发情况下都不留下永久活动任务或数据库/文件漂移。

- [x] 修复 F-04：终态先使用独立 cleanup token 持久化，审计改为后置 best-effort。（提交：`reliability: persist publish terminal states`）
- [x] 增加取消、15 分钟超时、host stopping、审计失败、writer 失败和 repository 失败测试。（提交：`reliability: persist publish terminal states`）
- [x] 修复 F-05：引入 unit of work/事务边界，并实现可验证的文件补偿。（提交：`reliability: make metadata updates atomic`）
- [x] 完成 D-03，修复 F-07 的 API 行为和契约测试。（提交：`api: unify publish job command contract`）
- [x] 将无界 Channel 改为可配置有界队列和满载等待 backpressure；入队取消会持久化 Failed，持久化队列留到 Phase 3。（提交：`reliability: bound publish job queue`）

**Stop gate**：故障注入矩阵通过；数据库中无遗留 Pending/Running；文件与数据库状态一致。

### Phase 3：持久作业、身份和审计边界（1-2 周，受 D-02 约束）

**目标**：为重启恢复和可选的共享部署建立明确架构，而不是继续扩大临时 API Key 模型。

- [x] 记录 D-02 ADR 和威胁模型。（提交：`docs: define local-only deployment boundary`）
- [ ] 实现 DB-backed outbox/queue、租约、heartbeat、实例归属、重试和幂等键。
- [ ] 启动恢复只回收租约过期任务。
- [ ] shared 模式引入 OIDC/session、角色与服务端 actor；local-only 模式在配置和文档中明确限制。
- [ ] 移除或内化客户端审计写入口，审计事件由业务服务生成。

**Stop gate**：双实例集成测试证明不会重复执行或误回收；审计 actor 不可由普通客户端伪造。

### Phase 4：查询和前端数据治理（约 1 周）

**目标**：发布历史随数据量增长仍保持稳定，并减少前端手工异步状态的重复和竞态。

- [ ] 修复 F-08：物化发布摘要，SQL 过滤/分页/聚合，报告 JSON 只用于详情。
- [ ] 加入大数据量查询基准和索引执行计划检查。
- [ ] 渐进引入 TanStack Query，从 workspace、audit 和 publish history 开始。
- [ ] 生成或共享 OpenAPI TypeScript 契约，减少手写 DTO 漂移。
- [ ] 拆分大型页面、清理测试警告、建立 bundle budget。
- [ ] 修复 F-12，清零后端 build warning 并启用 warnings-as-errors。

**Stop gate**：历史查询 I/O 与总作业数解耦；前端全门禁无未登记 warning。

### Phase 5：生产发布工程（约 1 个 Sprint）

**目标**：让应用具备可重复部署、升级、观测和恢复能力。

- [ ] API/前端生产镜像或明确的静态托管方案。
- [ ] reverse proxy、TLS、安全 header、外部 secrets 和持久存储配置。
- [ ] 独立数据库 migration job，取消多实例应用启动迁移。
- [ ] readiness/liveness、结构化指标、发布队列深度、作业耗时和失败率告警。
- [ ] 数据库与发布制品备份/恢复演练。
- [ ] SBOM、版本号、变更日志和回滚步骤。

**Stop gate**：全新环境部署和备份恢复均通过自动化演练。

### Phase 6：EU 完整能力（2+ Sprints，受 D-04 约束）

**目标**：只有在权威规则、资产、许可和维护来源确定后，才把 EU 从受控骨架提升为正式能力。

- [ ] 取得并版本化官方 EU M1 规则、DTD/schema 与生命周期要求。
- [ ] 建立规则来源、版本、生效日期和退役策略。
- [ ] 完整 Section Dictionary、regional writer、验证规则和认证 fixture。
- [ ] FDA 回归与 EU 端到端测试同时通过。
- [ ] README 能力矩阵只陈述测试证据能够支持的合规范围。

## 6. 优先级与依赖

| 顺序 | 覆盖发现 | 原因 |
|---|---|---|
| 1 | F-03 | 公开数据风险具有时间敏感性 |
| 2 | F-01、F-02、F-06 | 关闭可直接利用的文件系统和主要流程安全缺口 |
| 3 | F-04、F-05、F-07 | 恢复任务状态、事务和 API 契约可信度 |
| 4 | F-09、F-10 | 部署形态确定后再付出身份和持久队列成本 |
| 5 | F-08、F-11、F-12 | 在正确性稳定后处理扩展性和质量门禁 |
| 6 | EU/新增规则 | 不应早于生产安全与可靠性基线 |

## 7. 执行纪律

- 每个 Phase 使用独立分支或独立 PR；P0 可按事件处置拆成更小提交。
- 每项修复先添加能够复现问题的测试，再修改实现。
- 安全测试必须同时证明“操作被拒绝”和“目标文件/数据库未变化”。
- 涉及文件与数据库的流程必须有异常矩阵，不接受只覆盖 happy path。
- 不以 InMemory provider 证明 PostgreSQL 约束或事务语义。
- 不把公开接口的安全性建立在前端校验上。
- 不在未确认 D-01 前执行 Git 历史重写；不在未确认 D-02 前默认引入完整多用户系统。
- 每个 Stop gate 全绿后才进入下一 Phase；完成项在本文勾选并记录 PR/commit。

## 8. 完整质量门禁

仓库没有 `.sln`，后端命令必须直接指定项目文件。

```powershell
dotnet restore tests/RATools.Tests/RATools.Tests.csproj
dotnet build tests/RATools.Tests/RATools.Tests.csproj --configuration Release --no-restore
dotnet test tests/RATools.Tests/RATools.Tests.csproj --configuration Release --no-build
py scripts/tests/test_publish_jobs_controller_contract.py

Push-Location frontend
npm run lint
npm run build
npm run test
Pop-Location
```

Windows 使用 `py` 启动器；Linux CI 使用 `python3`。运行后端测试前，`dotnet --list-runtimes` 必须包含 `Microsoft.AspNetCore.App 8.0.x`。

真实 PostgreSQL 约束测试需要设置 `RATOOLS_TEST_POSTGRES`，其连接串应指向独立测试库。最终合并还必须通过：

- [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)
- [`.github/workflows/smoke.yml`](../../.github/workflows/smoke.yml)
- CodeQL 和依赖扫描
- `git diff --check`
- Phase 对应的安全/故障注入/性能验收测试

## 9. 明确延后事项

在 Phase 0-2 完成前，以下工作不进入主线优先级：

- EU 完整 M1 规则扩张和“完整合规”声明。
- 不与已发现故障相关的大规模页面重写。
- 纯粹为了减少行数的后端抽象或 repository 重构。
- 没有真实部署目标支撑的 Kubernetes/复杂云原生改造。
- 只调整 chunk 数字、但没有加载性能证据的前端拆包。

## 10. 完成定义

本路线图不是以“代码已写”作为完成标准。一个 Phase 只有同时满足以下条件才可标记完成：

1. 对应 Finding 的所有验收条件均有自动化证据。
2. 本地完整门禁、真实 PostgreSQL 测试、CI 和 smoke 全绿。
3. 文档、OpenAPI/HTTP 示例和运行时行为一致。
4. 无新增未处置的 P0/P1 风险。
5. 本文任务已勾选，并记录关联 PR/commit 和必要决策。
