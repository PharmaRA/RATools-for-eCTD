# 构建质量门禁（Build Quality Gates）

## 概述

当前后端构建非常宽松：`Directory.Build.props` 仅设置 `TargetFramework=net8.0`、`Nullable=enable`、`ImplicitUsings=enable`、`LangVersion=latest` 以及复制 DTD 内容（见 `Directory.Build.props:1-14`），没有开启 `TreatWarningsAsErrors`、`EnableNETAnalyzers`、`AnalysisMode` 或 `EnforceCodeStyleInBuild`；仓库根目录无 `.editorconfig`，也无 `Directory.Packages.props`，各 `csproj` 重复声明版本号（例如 EF Core `8.0.8` 在 `RATools.Infrastructure.csproj` 与 `RATools.Tests.csproj` 多处出现）。前端 `tsconfig.app.json` 开启了 `noUnusedLocals`/`noUnusedParameters`/`noFallthroughCasesInSwitch`，但**没有** `"strict": true`（`noImplicitAny` 亦未启用），代码中存在大量 `any`（如 `PublishHistoryTab.tsx` 的 `useState<any>`、`appShared.ts` 的 `sequences: any[]`）。ESLint 使用 `tseslint.configs.recommended`（非 type-checked 规则集，见 `frontend/eslint.config.js:14`）。

本设计分阶段收紧前后端构建质量门禁，核心原则是**分阶段、低噪声**，避免一次性把海量已有告警变成编译错误而阻塞所有人。

## 目标

1. 后端分阶段启用 `EnableNETAnalyzers`、`AnalysisMode=Recommended`、`EnforceCodeStyleInBuild`，最终启用 `TreatWarningsAsErrors`，将代码质量问题在构建期暴露。
2. 新增仓库根 `.editorconfig`，统一编码风格与分析器严重级别，作为后端 code style 强制执行与前端编辑器一致性的基础。
3. （可选）引入 `Directory.Packages.props` 集中管理 NuGet 包版本，消除各 `csproj` 重复版本号导致的漂移风险。
4. 前端启用 `"strict": true`，并补齐当前用 `any` 表达的领域类型模型（Application/Sequence、发布历史响应、发布报告）。
5. 全程分阶段推进，每阶段把新增告警收敛到零后再进入下一阶段，确保 CI 始终可绿。

## 非目标

- 不在本设计中搭建 CI 流水线本身（由 ci-pipeline 设计负责）；本设计聚焦构建期质量配置。
- 不引入第三方静态分析平台（SonarQube、CodeQL 等）。
- 不重写业务逻辑，仅在类型与告警层面收紧；类型补全以「描述既有数据形状」为限，不改变运行时行为。
- 不一次性把所有分析器告警提升为错误。

## 设计

### 阶段一：后端启用分析器（不阻断）

在 `Directory.Build.props` 的 `<PropertyGroup>` 中追加：

```xml
<EnableNETAnalyzers>true</EnableNETAnalyzers>
<AnalysisMode>Recommended</AnalysisMode>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
```

此阶段先让分析器与 code style 检查运行起来但**不阻断**构建，统计基线告警量。`EnforceCodeStyleInBuild` 依赖 `.editorconfig` 提供风格规则（见下节）。所有源项目（`RATools.Api`、`RATools.Application`、`RATools.Domain`、`RATools.Infrastructure`）与测试项目通过 `Directory.Build.props` 自动继承，无需逐个修改 `csproj`。

### 阶段二：新增根 `.editorconfig`

在仓库根新增 `.editorconfig`，统一缩进、换行、`using` 排序等，并显式设定关键分析器/code style 规则的 `severity`。建议从保守级别起步：

```ini
root = true

[*.cs]
indent_style = space
indent_size = 4
dotnet_sort_system_directives_first = true
csharp_style_namespace_declarations = file_scoped:warning
dotnet_style_qualification_for_field = false:suggestion
# 噪声较大的规则先降级，待逐步清理后再提升
dotnet_diagnostic.CA1062.severity = suggestion

[*.{ts,tsx,js,json,yml}]
indent_style = space
indent_size = 2
```

`file_scoped` 与现有代码风格一致（如 `GlobalExceptionMiddleware.cs:3`、`Program.cs:77` 的 `public partial class Program;`）。

### 阶段三：逐项目消除告警并启用 TreatWarningsAsErrors

按项目体量从小到大（`Domain` → `Application` → `Infrastructure` → `Api`）清理告警，每个项目收敛到零后，针对该项目在其 `csproj` 开启 `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`；全部项目清零后再在 `Directory.Build.props` 统一开启，避免一次性把全仓告警变错误。对暂时无法处理的规则，使用 `.editorconfig` 精确降级而非全局关闭，保留意图记录。

### 阶段四（可选）：集中包版本管理

新增仓库根 `Directory.Packages.props`，启用 `ManagePackageVersionsCentrally`，将各 `csproj` 中重复的版本号上提：

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="8.0.8" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.8" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.8" />
    <!-- 其余包同理 -->
  </ItemGroup>
</Project>
```

随后将各 `csproj` 的 `<PackageReference Include="X" Version="Y" />` 改为去掉 `Version`。涉及文件包括 `src/RATools.Infrastructure/RATools.Infrastructure.csproj:13-24`、`src/RATools.Api/RATools.Api.csproj`、`src/RATools.Application/RATools.Application.csproj` 与 `tests/RATools.Tests/RATools.Tests.csproj:15-23`。此举消除 EF Core `8.0.8` 等版本号在多处重复、易漂移的问题。

### 阶段五：前端启用 strict 与类型补全

分两步推进，避免一次性产生海量类型错误：

1. **先开 `noImplicitAny`**：在 `frontend/tsconfig.app.json` 的 `compilerOptions` 增量加入 `"noImplicitAny": true`，定位所有隐式 `any`；现有显式 `any`（如 `useState<any>`）不受影响，便于先解决隐式问题。
2. **再开 `"strict": true`**：补齐领域类型后启用完整 strict（含 `strictNullChecks` 等）。

类型补全聚焦下列已用 `any` 表达的模型：

- `Application` / `Sequence`：`frontend/src/pages/appShared.ts:11` 当前为 `sequences: any[]`，应定义 `Sequence` 接口并替换。
- 发布报告：`ReportPanel.tsx:53`、`PackageReviewPanel.tsx:117` 的 `useState<any>(null)`、`appShared.ts:47` 的 `getLifecycleIssueCount(summary?: any)`、`appShared.ts:56` 的 `getReportAvailabilityLabel(entry: any)`，应抽象为 `PublishReport` 及其子结构（lifecycle matches、integrity evidence 等）。
- 发布历史响应：`PublishHistoryTab.tsx:30` 的 `useState<any>(null)` 与各列 `render: (_: any, r: any)`，应定义 `PublishHistoryResponse` / `PublishHistoryRow`（含 `publishReadiness`）。
- 制品列表：`ArtifactsPanel.tsx:10` 的 `useState<any[]>([])` 应替换为 `Artifact[]`。

ESLint 同步从 `tseslint.configs.recommended` 升级到 `tseslint.configs.recommendedTypeChecked`（需在 `eslint.config.js` 配置 `parserOptions.project`），开启 `no-explicit-any` 等 type-aware 规则，从 lint 侧防止 `any` 回流。

## 测试策略

- 每阶段执行 `dotnet build tests/RATools.Tests/RATools.Tests.csproj` 与 `dotnet test`，确认告警数量按预期收敛且测试全绿。
- 启用 `TreatWarningsAsErrors` 后，确认构建在零告警下成功；故意引入一个告警验证其确实变为错误。
- 前端每步执行 `npm run build`（`tsc -b && vite build`）与 `npm test`（`vitest run`），确认类型错误清零、测试通过。
- `npm run lint` 在升级到 type-checked 规则集后通过。
- 引入 `Directory.Packages.props` 后执行 `dotnet restore` 确认版本解析一致、无 NU 警告。

## 风险

**风险：** 一次性开启 `TreatWarningsAsErrors` 或前端 `strict` 会瞬间产生大量错误，阻塞所有开发。

**缓解：** 严格按阶段推进，先以非阻断方式暴露基线，逐项目/逐选项清零后再提升为错误；前端先 `noImplicitAny` 再 `strict`。

**风险：** 集中包版本管理改造遗漏某个 `csproj` 的 `Version`，导致还原失败或版本不一致。

**缓解：** 改造后立即 `dotnet restore` 校验，并依赖 NU1008 等告警定位遗漏；分项目提交便于回滚。

**风险：** 用 `any` 补类型时凭猜测建模，与后端 DTO 实际形状不符，引入误导性类型。

**缓解：** 类型以后端实际响应（控制器/契约）与现有 UI 取值路径为依据校对，仅描述既有形状、不改运行时行为，并通过 `vitest` 与手工冒烟验证。

## 验收标准

1. `Directory.Build.props` 启用 `EnableNETAnalyzers`、`AnalysisMode=Recommended`、`EnforceCodeStyleInBuild`，并最终启用 `TreatWarningsAsErrors`。
2. 仓库根存在 `.editorconfig`，`dotnet build` 据其执行 code style 检查。
3. 后端在零告警下构建成功，引入告警会导致构建失败。
4. （若实施）存在 `Directory.Packages.props`，各 `csproj` 不再重复声明版本号且 `dotnet restore` 通过。
5. `frontend/tsconfig.app.json` 启用 `"strict": true`，`appShared.ts` / `PublishHistoryTab.tsx` / `ReportPanel.tsx` / `ArtifactsPanel.tsx` 等不再使用 `any`，`npm run build` 与 `npm test` 通过。
6. 前端 ESLint 升级到 type-checked 规则集且 `npm run lint` 通过。
