# 健康检查与可观测性（Health Check & Observability）

## 概述

当前 `src/RATools.Api/Program.cs` 通过 `app.MapGet("/health", () => Results.Ok(new { status = "ok" }))`（`Program.cs:66`）映射了一个静态健康端点，以及 `GET /version`（`Program.cs:67-71`）。该 `/health` 不依赖任何运行时探测，**没有**调用 `AddHealthChecks()`，也不探测 PostgreSQL，因此即便数据库不可用 `/health` 仍返回 `ok`，对编排器/负载均衡器是误导性的就绪信号。应用启动时同步执行 `dbContext.Database.Migrate()`（`Program.cs:43-46`），数据库不可达会直接导致启动失败。

日志方面，全代码库仅 2 个文件注入了 `ILogger`：`GlobalExceptionMiddleware.cs` 与 `ApiKeyAuthenticationHandler.cs`。Application/Infrastructure 服务层（发布 `PublishJobService`、校验 `SequenceValidationService`、持久化各 `EfCore*Repository`、审计 `AuditLogService`）基本没有结构化日志。仓库未引入 Serilog、OpenTelemetry、Application Insights 或 Sentry。正面项是 `GlobalExceptionMiddleware` 返回符合 RFC7807 的 `application/problem+json` 并带 `traceId`（`GlobalExceptionMiddleware.cs:35-43`）。

对一个 eCTD 法规发布工具而言，发布、校验、删除、审计等关键路径的可追溯日志链路属于**功能性需求**而非锦上添花。本设计补齐可用的健康检查与结构化日志/可观测性能力。

## 目标

1. 引入 `AddHealthChecks()` 并加入 PostgreSQL/`DbContext` 探针，使健康检查真实反映数据库可用性。
2. 区分存活探针 `/health/live`（进程是否运行）与就绪探针 `/health/ready`（依赖是否就绪），并保持现有匿名访问。
3. 为关键路径（发布、校验、删除、审计）补齐结构化日志，形成可追溯的操作链路。
4. 评估并引入 OpenTelemetry 追踪与指标，复用现有 `traceId` 形成端到端关联。
5. 在不破坏现有 `/health` 契约与 `/version` 的前提下增量演进。

## 非目标

- 不引入特定的日志/监控后端供应商（如 Application Insights、Sentry）的强绑定；以标准抽象（`ILogger`、OpenTelemetry OTLP）为先。
- 不改变现有 RFC7807 错误响应格式（`GlobalExceptionMiddleware` 已合规）。
- 不改动 API Key 认证机制或授权策略。
- 不在本设计中改变启动期 `Database.Migrate()` 的行为（仅在健康检查层面区分迁移就绪状态）。
- 不引入分布式追踪所需的额外基础设施部署（仅提供可配置的导出能力）。

## 设计

### 健康检查：AddHealthChecks 与 DbContext 探针

在 `Program.cs` 服务注册段（紧随 `AddInfrastructure` 之后）加入：

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<RAToolsDbContext>(
        name: "database",
        tags: new[] { "ready" });
```

`AddDbContextCheck<RAToolsDbContext>` 由 `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 提供，复用已注册的 `RAToolsDbContext`（`Program.cs:44` 已能从 DI 解析），通过 `CanConnectAsync` 实际探测数据库。亦可改用 `AspNetCore.HealthChecks.Npgsql` 直连连接串。当 `Persistence:Provider` 为 `InMemory`（`Program.cs:40`、`48`）时，数据库探针应跳过或恒为健康，避免误报。

### 存活与就绪端点划分

保留现有 `GET /health` 以兼容已有调用方（如 `Program.cs:65` 的根重定向、`README.md:50` 描述的 Vite 代理、冒烟脚本轮询），并新增分层端点：

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // 不跑任何依赖检查，仅表示进程存活
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") // 含数据库等依赖
}).AllowAnonymous();
```

- `/health/live`：进程在跑即 200，供编排器存活探针使用，不因数据库抖动而重启容器。
- `/health/ready`：聚合 `ready` 标签下的检查（数据库等），数据库不可用时返回 503，供负载均衡器/就绪探针判断是否接流量。
- `/health`：保持返回 `{ status: "ok" }` 静态契约不变，避免破坏既有依赖；新栈推荐迁移到分层端点。

三者均 `AllowAnonymous()`，与现有端点策略一致。

### 关键路径结构化日志

为以下 Application/Infrastructure 服务注入 `ILogger<T>` 并补齐结构化日志（使用命名占位符，便于结构化检索与与 `traceId` 关联）：

- 发布：`src/RATools.Application/Publishing/PublishJobService.cs` —— 记录发布作业创建、状态流转（Pending/Running/完成/失败）、报告/索引/校验和/包 zip 生成与产物落地，携带 `ApplicationId`、`SequenceNumber`、`JobId`。
- 校验：`src/RATools.Application/Validation/SequenceValidationService.cs` 与 `PublishReadinessService.cs` —— 记录校验触发、各类结果汇总（section 匹配、lifecycle、文件存在性、发布就绪），携带计数与关键失败项。
- 删除：`IWorkspaceDeletionService` 实现与应用/序列删除路径 —— 删除属高影响操作，应记录删除目标、级联范围与结果（成功/被约束阻止）。
- 审计：`src/RATools.Application/Auditing/AuditLogService.cs` 与 `EfCoreAuditLogRepository.cs` —— 记录审计条目写入；审计落库失败必须显式记日志，不可静默吞掉。

日志级别约定：正常生命周期事件用 `Information`，可恢复异常/业务冲突用 `Warning`，未预期失败用 `Error`。结构化字段统一字段名（`ApplicationId`、`SequenceNumber`、`JobId`、`AuditEventType`），便于跨服务串联。

### 与现有 traceId 的关联

`GlobalExceptionMiddleware` 已将 `context.TraceIdentifier` 写入 `traceId`（`GlobalExceptionMiddleware.cs:24`、`41`）。引入 OpenTelemetry 后，应让服务层日志携带同一关联标识，使一次请求从入口、服务层日志到错误响应可端到端串联。建议启用 `ActivityTrackingOptions` 把 `TraceId`/`SpanId` 纳入日志 scope。

### OpenTelemetry 追踪与指标（可选增强）

引入 `OpenTelemetry.Extensions.Hosting` 与相关 instrumentation：

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
```

- 追踪：覆盖 ASP.NET Core 请求与 EF Core 查询，定位发布/校验链路中的慢操作。
- 指标：暴露请求量、错误率、EF 查询耗时等，便于容量与健康趋势观察。
- 导出：默认 OTLP，导出端点通过配置开关控制，未配置时可禁用导出，避免本地/CI 噪声。

是否启用通过配置项控制（如 `Observability:Tracing:Enabled`），默认关闭以保持本地与现有部署行为不变。

## 测试策略

- 在 `tests/RATools.Tests` 新增端点测试，基于 `WebApplicationFactory<Program>`：`/health/live` 在进程存活时恒为 200；`/health/ready` 在数据库可用时 200。
- 模拟数据库不可用（或用使 `CanConnect` 失败的配置），断言 `/health/ready` 返回 503 而 `/health/live` 仍 200。
- 断言 `GET /health` 仍返回 `{ status: "ok" }`，`GET /version` 行为不变，保证向后兼容。
- 针对 `InMemory` provider 路径确认数据库探针被跳过、不误报不健康。
- 对发布/校验/删除/审计服务，使用可捕获日志的 `ILogger` 测试替身（如 `FakeLogger`）断言关键事件以预期级别与结构化字段被记录。

## 风险

**风险：** 新增 `/health/ready` 数据库探针在数据库短暂抖动时返回 503，若被误配为存活探针会触发容器重启。

**缓解：** 明确划分 `/health/live`（不含依赖）与 `/health/ready`（含依赖），并在文档说明编排器应分别绑定存活与就绪探针。

**风险：** 改动 `/health` 语义会破坏现有调用方（根重定向、Vite 代理、冒烟脚本轮询）。

**缓解：** `/health` 静态契约保持不变，仅新增分层端点；通过端点测试锁定原契约。

**风险：** 关键路径大量补日志可能记录敏感信息（路径、申请号、文件名）或拖慢热点路径。

**缓解：** 仅记录标识符与计数等结构化字段，避免输出机密内容；热点路径以 `Information` 为限并支持按级别配置，必要处用日志级别守卫。

## 验收标准

1. `Program.cs` 调用 `AddHealthChecks()` 并注册 `RAToolsDbContext` 数据库探针。
2. 暴露 `/health/live` 与 `/health/ready`，均匿名可访问；数据库不可用时 `/health/ready` 返回 503、`/health/live` 仍 200。
3. 原 `GET /health` 与 `GET /version` 行为与契约保持不变，并有测试覆盖。
4. 发布、校验、删除、审计关键路径具备结构化日志，包含统一的关联字段（`ApplicationId`、`SequenceNumber`、`JobId` 等）。
5. 提供可配置开关的 OpenTelemetry 追踪与指标接入，默认关闭导出，启用后可见 ASP.NET Core 与 EF Core 的追踪/指标。
6. 新增健康检查端点测试在 `tests/RATools.Tests` 中随现有套件一并通过。
