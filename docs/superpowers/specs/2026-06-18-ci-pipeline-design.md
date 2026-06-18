# 持续集成流水线（CI Pipeline）

## 概述

本仓库当前没有任何持续集成配置（根目录不存在 `.github/workflows`、`azure-pipelines.yml` 等文件），所有构建、测试与冒烟验证都依赖开发者本地手工执行。本设计引入基于 GitHub Actions 的 CI 流水线，在 Pull Request 与推送到 `master` 时自动执行后端构建/测试、前端 lint/构建/测试，并以服务容器（service container）方式提供 PostgreSQL 16，使流水线与本地 `docker-compose.yml` 的数据库环境保持一致。可观测的端到端冒烟测试 `scripts/smoke-test.ps1` 作为可选的定时任务运行。

## 目标

1. 在 PR 与推送到 `master` 时自动验证后端（`dotnet restore` / `build` / `test`）与前端（`npm ci` / `lint` / `build` / `test`），阻止破坏构建或测试的变更被合并。
2. 复用现有测试基础设施：后端 `tests/RATools.Tests`（xUnit，约 31 个测试文件 / ~145 个测试，基于 `WebApplicationFactory<Program>` 的进程内集成测试，使用 EF InMemory 与 Sqlite），前端 `vitest run`。
3. 为需要真实数据库的场景提供与本地一致的 PostgreSQL 16 服务容器，保证后续 PostgreSQL 专有行为（如分区唯一索引）可在 CI 中验证。
4. 把 `scripts/smoke-test.ps1` 端到端冒烟以可选的定时/手动触发方式纳入，作为针对法规发布链路的回归保护。
5. 引入依赖与构建产物缓存，缩短流水线时长。

## 非目标

- 不引入部署/发布（CD）阶段，不向任何环境发布制品。
- 不替换或重写现有测试，不更改测试运行框架。
- 不强制要求后端单元/集成测试连接真实 PostgreSQL；现有 InMemory + Sqlite 测试保持进程内运行。
- 不在本设计中收紧编译告警门禁（分析器、`TreatWarningsAsErrors`、前端 `strict` 等由 build-quality-gates 设计单独处理）。
- 不引入除 GitHub Actions 之外的第二套 CI 平台。

## 设计

### 触发条件与整体结构

新增工作流文件 `.github/workflows/ci.yml`，触发条件：

```yaml
on:
  pull_request:
    branches: [master]
  push:
    branches: [master]
```

流水线拆分为两个互相独立、可并行的 job：`backend` 与 `frontend`。两者无相互依赖，可同时运行以缩短墙钟时间。冒烟测试单独放在 `.github/workflows/smoke.yml`，由 `schedule`（定时）与 `workflow_dispatch`（手动）触发，不阻塞 PR。

> 注意：仓库无 `.sln` 解决方案文件（已核对根目录与 `Glob *.sln` 均无结果）。CI 应以各 `.csproj` 为单位执行命令，路径与 `README.md` 的「Local Run / Useful Commands」一致。

### 后端 job（backend）

运行环境 `ubuntu-latest`，步骤如下：

1. `actions/checkout@v4`。
2. `actions/setup-dotnet@v4`，`dotnet-version: 8.0.x`（与 `Directory.Build.props` 中 `TargetFramework=net8.0` 一致）。
3. `dotnet restore`：对四个源项目与测试项目逐一还原，或直接 `dotnet restore tests/RATools.Tests/RATools.Tests.csproj`（测试项目通过 `ProjectReference` 引用全部源项目，见 `tests/RATools.Tests/RATools.Tests.csproj:7-12`，一次还原即可拉全依赖）。
4. `dotnet build tests/RATools.Tests/RATools.Tests.csproj --configuration Release --no-restore`。
5. `dotnet test tests/RATools.Tests/RATools.Tests.csproj --configuration Release --no-build --logger trx --results-directory ./TestResults`。

由于现有后端测试使用 EF InMemory（`Microsoft.EntityFrameworkCore.InMemory` 8.0.8）与 Sqlite（`Microsoft.EntityFrameworkCore.Sqlite` 8.0.8），见 `tests/RATools.Tests/RATools.Tests.csproj:16-17`，该 job 无需 PostgreSQL 服务容器即可全量通过。

### 后端 job 的 PostgreSQL 服务容器（可选增强）

为支持后续依赖真实 PostgreSQL 的测试（例如数据库约束/分区唯一索引验证），后端 job 可声明服务容器，镜像版本与 `docker-compose.yml:5` 的 `postgres:16` 对齐：

```yaml
services:
  postgres:
    image: postgres:16
    env:
      POSTGRES_DB: ratools
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports: ['5432:5432']
    options: >-
      --health-cmd "pg_isready -U postgres -d ratools"
      --health-interval 10s --health-timeout 5s --health-retries 10
```

环境变量与凭据同 `docker-compose.yml:8-11`，healthcheck 与 `docker-compose.yml:16-20` 保持一致。需要真实数据库的测试通过环境变量覆盖连接串与 `Persistence:Provider=PostgreSql`（参见 `Program.cs:40`）。在没有此类测试前，服务容器可保持注释/禁用，避免无谓启动开销。

### 前端 job（frontend）

运行环境 `ubuntu-latest`，工作目录 `frontend`，步骤如下：

1. `actions/checkout@v4`。
2. `actions/setup-node@v4`，建议 `node-version: 20`，并启用 npm 缓存（见缓存策略）。
3. `npm ci`：CI 使用 `npm ci` 而非 `npm install`，要求提交 `frontend/package-lock.json`。若仓库尚未提交 lockfile，作为本设计前置项补充。
4. `npm run lint`（对应 `frontend/package.json:9` 的 `eslint .`）。
5. `npm run build`（对应 `package.json:8` 的 `tsc -b && vite build`，可同时捕获 TypeScript 编译错误）。
6. `npm test`（对应 `package.json:10` 的 `vitest run`，已配置 `jsdom`，见 `devDependencies`）。

### 缓存策略

- 后端 NuGet：使用 `actions/setup-dotnet` 的内建缓存或 `actions/cache` 缓存 `~/.nuget/packages`，key 基于所有 `**/*.csproj` 的哈希。鉴于仓库各 `csproj` 重复声明版本号且无 `Directory.Packages.props`，以 csproj 内容作为 key 即可；后续若引入集中包管理，可改用 `packages.lock.json`。
- 前端 npm：用 `actions/setup-node` 的 `cache: 'npm'` 与 `cache-dependency-path: frontend/package-lock.json`。
- 构建产物：CI 内 `dotnet build` 与 `dotnet test` 间复用 `--no-build`，避免重复编译；前端 `tsc -b` 的增量信息（`tsBuildInfoFile`，见 `tsconfig.app.json:3`）位于 `node_modules/.tmp`，随 npm 缓存自然受益。

### 测试结果与制品

- 后端使用 `--logger trx` 产出测试结果，通过 `actions/upload-artifact` 上传 `TestResults`，便于失败排查。
- 可选接入测试报告展示动作，将 `.trx` 渲染为 PR 检查摘要。

### 定时冒烟测试（smoke.yml，可选）

`scripts/smoke-test.ps1`（参数见脚本 `scripts/smoke-test.ps1:1-9`，默认 `BaseUrl=http://localhost:5000`、`ApiKey=dev-api-key-do-not-use-in-production`）需要运行中的完整栈。该工作流：

1. 由 `schedule`（如每日）与 `workflow_dispatch` 触发，不绑定 PR。
2. 起 `postgres:16` 服务容器；以 `Persistence:Provider=PostgreSql` 后台运行 `dotnet run --project src/RATools.Api/RATools.Api.csproj`（API 启动时会自动执行迁移，见 `Program.cs:43-46`）。
3. 轮询 `GET /health`（`Program.cs:66`）确认 API 就绪后，用 `pwsh` 执行 `scripts/smoke-test.ps1 -BaseUrl <url> -ApiKey <key>`。
4. `ubuntu-latest` 默认带 PowerShell Core（`pwsh`），脚本以 `Invoke-RestMethod` 为主，跨平台可运行。

## 测试策略

- 在功能分支开 PR，确认 `backend` 与 `frontend` 两个 job 被触发并独立运行。
- 后端 job 全量通过现有 ~145 个 xUnit 测试，产出并上传 `.trx`。
- 前端 job 依次跑通 `lint`、`build`、`test` 三步，任一失败即标红。
- 故意引入一个失败的后端测试与一处前端 lint 错误，验证流水线确实会失败（红线门禁有效）。
- 手动 `workflow_dispatch` 触发 `smoke.yml`，确认服务容器、API 启动、`/health` 探活与冒烟脚本端到端跑通。

## 风险

**风险：** `npm ci` 要求存在 `frontend/package-lock.json`，若仓库未提交 lockfile，前端 job 会直接失败。

**缓解：** 将提交 `package-lock.json` 作为本设计的前置步骤；过渡期可临时回退到 `npm install` 并提示尽快补齐 lockfile。

**风险：** 现有后端测试基于 InMemory/Sqlite，CI 通过不代表 PostgreSQL 专有行为（唯一约束、分区索引）正确。

**缓解：** 提供与 `docker-compose.yml` 对齐的 PostgreSQL 服务容器供针对性测试使用，并保留定时冒烟测试覆盖真实数据库链路。

**风险：** 冒烟测试依赖运行中的完整栈，耗时长且易因环境抖动产生不稳定（flaky）结果。

**缓解：** 将冒烟测试从 PR 门禁中剥离，仅定时/手动触发；通过轮询 `/health` 而非固定 `sleep` 确保就绪后再执行。

## 验收标准

1. 仓库新增 `.github/workflows/ci.yml`，在 PR 与推送到 `master` 时触发。
2. `backend` job 完成 `restore` / `build` / `test`，全量现有测试通过并上传测试结果。
3. `frontend` job 在 `frontend` 目录完成 `npm ci` / `lint` / `build` / `test` 且全部通过。
4. NuGet 与 npm 依赖缓存生效，重复运行可观测到缓存命中。
5. 引入一处失败用例可让对应 job 变红，验证门禁有效。
6. 提供可选的 `.github/workflows/smoke.yml`，以服务容器 + `scripts/smoke-test.ps1` 跑通定时端到端冒烟，且不阻塞 PR。
