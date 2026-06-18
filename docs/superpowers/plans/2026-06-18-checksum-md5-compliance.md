# 校验和 MD5 合规修复实施计划（Checksum MD5 Compliance Implementation Plan）

> 配套设计：`docs/superpowers/specs/2026-06-18-checksum-md5-compliance-design.md`

**目标：** 让 eCTD backbone 的 leaf 校验和输出 `checksum-type="md5"`，`index-md5.txt` 仅含 `index.xml` 的 MD5；包模型携带 MD5 作为事实来源；SHA256 保留为额外完整性证据。

**方案：** 采用设计中的方案 A——在文档上传时同时计算并持久化 MD5，使包模型成为 backbone 校验和的单一事实来源，writer 仅读取字段，避免发布期逐文件读盘。

**约定：** 每个 Task 完成后运行相关测试并提交一次 commit。

---

## Task 1：领域与存储承载 MD5

**文件：**
- `src/RATools.Domain/Documents/SubmissionDocument.cs`
- `src/RATools.Application/Abstractions/Storage/FileUploadResult.cs`
- `src/RATools.Infrastructure/Storage/LocalFileStorage.cs`
- `src/RATools.Application/Documents/DocumentService.cs`（上传路径传递 MD5）
- `src/RATools.Application/Documents/Requests/CreateDocumentRequest.cs`
- `src/RATools.Application/Documents/Dtos/DocumentDto.cs`
- `src/RATools.Application/Applications/ApplicationImportService.cs`（计算并传入 MD5/SHA256）

**步骤：**
- [ ] `SubmissionDocument` 新增 `Md5` 字段，公共构造函数要求非空；`Rehydrate` 对存量行容忍空值（不抛错），保持向后兼容。
- [ ] `FileUploadResult` 新增 `Md5`。
- [ ] `LocalFileStorage.SaveAsync` 在同一流式读取中同时计算 SHA256 与 MD5，回填 `FileUploadResult.Md5`。
- [ ] `DocumentService` 三条创建/上传路径把 `Md5` 透传给 `SubmissionDocument`。
- [ ] `CreateDocumentRequest`、`DocumentDto` 新增 `Md5`，`DocumentMapping.ToDto` 同步。
- [ ] `ApplicationImportService`：导入时分别计算 MD5 与 SHA256，构造 `SubmissionDocument` 时正确填入两者（修正当前把 MD5 误填进 SHA256 槽的隐患）。
- [ ] 构建通过。

## Task 2：EF 持久化与迁移

**文件：**
- `src/RATools.Infrastructure/Persistence/EfCore/DocumentRecord.cs`
- `src/RATools.Infrastructure/Persistence/EfCore/RAToolsDbContext.cs`
- `src/RATools.Infrastructure/Persistence/EfCore/EfCoreDocumentRepository.cs`
- 新增迁移（含 Designer 与快照更新）

**步骤：**
- [ ] `DocumentRecord` 新增 `Md5`（默认空串容忍存量）。
- [ ] `RAToolsDbContext` 配置 `documents.Md5`，`HasMaxLength(128)`，不强制 `IsRequired`（容忍历史行回填前为空）。
- [ ] `DocumentRecordMapping.ToRecord`/`ToDomain` 与 `UpdateAsync` 同步 `Md5`。
- [ ] `dotnet ef migrations add AddDocumentMd5` 生成迁移与 Designer，确认快照含新列。
- [ ] 构建通过。

## Task 3：包模型与 Builder 携带 MD5

**文件：**
- `src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs`
- `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`

**步骤：**
- [ ] `EctdLeaf` 与 `EctdPublishedFile` 新增 `Md5` 字段（与 `Sha256` 并列）。
- [ ] `BuildLeaf` 把 `document.Md5` 填入；若 `document.Md5` 为空但 `SourcePath` 文件存在则按文件计算补齐（兼容存量行）。
- [ ] `BuildPublishedFiles` 透传 `Md5`。
- [ ] 更新受影响的 Builder 测试与测试夹具。
- [ ] 相关测试通过。

## Task 4：Writer 输出 MD5

**文件：**
- `src/RATools.Application/Publishing/Ich/IchIndexXmlWriter.cs`
- `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs`
- `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`
- `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs`

**步骤：**
- [ ] 两个 `BuildLeafElement` 改为 `checksum=leaf.Md5`、`checksum-type="md5"`。
- [ ] 更新 writer 测试夹具（`CreateLeaf` 增加 md5），断言 `checksum-type="md5"` 与对应 MD5 值。
- [ ] 相关测试通过。

## Task 5：index-md5.txt 仅含 index.xml 的 MD5

**文件：**
- `src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs`
- 新增/更新 `LocalBackboneFileWriter` 测试

**步骤：**
- [ ] 用只针对 `index.xml` 的逻辑替换 `BuildMd5Manifest`；定位 `deliveryRoot/index.xml`，写单行 `"<md5>  index.xml"`；缺失则快速失败。
- [ ] 复用 `ComputeMd5`。
- [ ] 新增测试断言 `index-md5.txt` 仅含 index.xml 单行 MD5。
- [ ] 相关测试通过。

## Task 6：全量验证

**步骤：**
- [ ] `dotnet test tests/RATools.Tests/RATools.Tests.csproj` 全绿。
- [ ] `cd frontend && npm test` 全绿。
- [ ] 审查 diff。

## 自检
- 覆盖设计的全部验收标准：leaf MD5、index-md5.txt 仅 index.xml、包模型承载 MD5、SHA256 仅作完整性证据、测试由 sha256 改 md5。
- 范围控制：不重构 SubmissionDocument 存储模型（仅加字段），不引入新清单格式，不实现完整合规校验器。
