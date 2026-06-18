# 校验和 MD5 合规修复（Checksum MD5 Compliance）

## 概述

修复 eCTD backbone 的校验和合规缺陷。当前 ICH `index.xml` 与 US Regional `us-regional.xml` 的 leaf 都输出 `checksum-type="sha256"`，而 `index-md5.txt` 被写成整棵交付树的 MD5 清单。ICH eCTD v3.2.2 规范强制 leaf 校验和使用 MD5，按 FDA 惯例 `index-md5.txt` 应只包含 `index.xml` 自身的 MD5。当前实现会被真实的 FDA 验收流程直接判定为不合规，是阻断性缺陷。

本设计让包模型承载 MD5，两个 writer 输出 `checksum-type="md5"` 与正确的 MD5 值，并把 `index-md5.txt` 改为仅 `index.xml` 的 MD5。SHA256 不再用于 backbone 校验和，但保留为额外的完整性证据。

## 目标

1. ICH `index.xml` 与 US Regional `us-regional.xml` 的每个 leaf 都输出 `checksum-type="md5"`，且 `checksum` 值为对应文件的 MD5。
2. `index-md5.txt` 只包含 `index.xml` 自身的 MD5，遵循 FDA 惯例。
3. 包模型（`EctdLeaf`、`EctdPublishedFile`）携带 MD5 值，作为 backbone 校验和的事实来源。
4. 保留 SHA256 作为额外的完整性证据，供 `PublishOutputVerifier` 与 artifact integrity evidence 使用，不写入 backbone 校验和属性。
5. 同步更新现有断言 sha256 的测试，使其断言 MD5。

## 非目标

- 不重新设计 `SubmissionDocument` 的存储模型，仅在需要时新增 MD5 字段。
- 不为 `index-md5.txt` 引入除 MD5 外的其他清单格式。
- 不实现完整的 DTD 校验或合规校验器（属于后续任务）。
- 不改变文档上传、放置或发布流程的对外契约。

## 设计

### 当前不合规来源

三处实现共同导致缺陷：

- ICH writer：`src/RATools.Application/Publishing/Ich/IchIndexXmlWriter.cs:136-137` 的 `BuildLeafElement` 把 `leaf.Sha256` 作为 `checksum`，并硬编码 `checksum-type="sha256"`。
- US Regional writer：`src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs:148-149` 的 `BuildLeafElement` 同样输出 `leaf.Sha256` 与 `checksum-type="sha256"`。
- MD5 清单：`src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs:139-156` 的 `BuildMd5Manifest` 遍历整棵 `deliveryRoot`（排除自身）并为每个文件写入 MD5 行，使 `index-md5.txt` 成为"全树清单"而非"仅 index.xml 的 MD5"。

值得注意的是，导入侧已假设校验和为 MD5：`src/RATools.Application/Applications/ApplicationImportService.cs:175-177` 读取 leaf 的 `checksum` 属性并与 `ComputeMd5(resolvedPath)` 比对，这进一步说明发布侧输出 SHA256 与系统内部约定不一致。

### 包模型承载 MD5

领域实体当前只存 SHA256：`src/RATools.Domain/Documents/SubmissionDocument.cs:46` 的 `public string Sha256 { get; private set; }`，构造函数与 `Rehydrate` 都只接收 `sha256` 参数。包模型记录同样只携带 SHA256：`src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs:60` 的 `EctdLeaf.Sha256` 与 `EctdPackageRecords.cs:75` 的 `EctdPublishedFile.Sha256`。

`EctdPackageModelBuilder` 把 `document.Sha256` 直接放入 leaf：`src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs:126` 与 `EctdPackageModelBuilder.cs:238`。

设计上，MD5 需要进入包模型。两种可选方案：

- 方案 A（推荐）：在文档上传时计算并持久化 MD5。为 `SubmissionDocument` 新增 `Md5` 字段（构造函数、`Rehydrate`、EF 映射、迁移），由计算 SHA256 的同一上传路径同时计算 MD5。`EctdPackageModelBuilder` 把 `document.Md5` 填入 `EctdLeaf` 与 `EctdPublishedFile` 的新 `Md5` 字段。优点是 backbone 写入时无需读盘，且 MD5 与文档内容绑定持久化。
- 方案 B：在写 backbone 时按文件计算 MD5。保持领域模型不变，由 writer 或文件写入层对每个已落盘的 published file 计算 MD5。优点是改动面小，缺点是 backbone 生成需要额外的逐文件读盘，并要求文件先落盘再回填校验和，增加流程耦合。

推荐方案 A，使包模型成为校验和的单一事实来源，与现有 `Sha256` 字段并列。`EctdLeaf` 与 `EctdPublishedFile` 同时保留 `Sha256` 与新增 `Md5`：

```csharp
public sealed record EctdLeaf(
    // ...既有字段...
    long FileSize,
    string Sha256,
    string Md5,
    EctdLifecycleReference? Lifecycle);
```

### Writer 输出 MD5

两个 writer 的 `BuildLeafElement` 改为使用 MD5：

```csharp
new XAttribute("checksum", leaf.Md5),
new XAttribute("checksum-type", "md5"),
```

修改点：

- `src/RATools.Application/Publishing/Ich/IchIndexXmlWriter.cs:136-137`
- `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs:148-149`

`xlink:href`、`operation`、`modified-file` 等其余属性保持不变。

### index-md5.txt 仅含 index.xml 的 MD5

`LocalBackboneFileWriter` 的 `index-md5.txt` 改为只写 `index.xml` 自身的 MD5。当前在 `src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs:77-79` 调用 `BuildMd5Manifest(deliveryRoot, indexMd5Path)` 生成全树清单。

改为定位 `deliveryRoot` 下的 `index.xml`，对其计算 MD5，并写入单行内容。FDA 惯例的行格式为 MD5 值后接 `index.xml` 路径，例如：

```text
d41d8cd98f00b204e9800998ecf8427e  index.xml
```

`BuildMd5Manifest`（`LocalBackboneFileWriter.cs:139-156`）应被替换为只处理 `index.xml` 的逻辑；`ComputeMd5`（`LocalBackboneFileWriter.cs:158-163`）可复用。如果 `index.xml` 不存在则应快速失败，因为这表示 backbone 生成不完整。

### SHA256 作为额外完整性证据

SHA256 不再进入 backbone 校验和属性，但保留为额外的完整性证据：

- `EctdPublishedFile.Sha256` 仍由 `PublishOutputVerifier`（`src/RATools.Application/Publishing/PublishOutputVerifier.cs`）与 artifact integrity evidence 使用，作为 backbone 之外的独立校验维度。
- 领域 `SubmissionDocument.Sha256` 字段保留不动，仅新增 `Md5`。

这样 backbone 对外符合 MD5 规范，内部仍保留更强的 SHA256 证据用于发布产物自检。

## 测试策略

- 更新 `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs:103-104`：断言 `checksum-type` 为 `md5` 且 `checksum` 为 leaf 的 MD5 值。
- 更新 `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs:134-135`：断言 `checksum-type` 为 `md5` 且 `checksum` 为 leaf 的 MD5 值。
- 检查并按需更新 `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`，验证 builder 正确填充 `Md5`。
- `tests/RATools.Tests/Publishing/Validation/EctdXmlValidatorTests.cs:42` 使用的 `checksum-type="sha256"` 测试夹具若涉及合规判断需同步调整；该断言（`EctdXmlValidatorTests.cs:53`）目前只检查 `checksum` 缺失场景，应确认是否需要扩展为校验 `checksum-type="md5"`。
- 新增 `LocalBackboneFileWriter` 测试，断言 `index-md5.txt` 仅包含 `index.xml` 的单行 MD5。
- 运行完整后端测试套件。

## 风险

**风险：** 文档上传时计算并持久化 MD5 需要新增领域字段与数据库迁移，可能影响既有数据行。

**缓解：** 采用与现有 SHA256 一致的迁移模式；对存量行可在迁移中回填 MD5，或在首次发布时按文件计算补齐。迁移保持 fail-fast，不静默接受空值。

**风险：** 修改 `index-md5.txt` 语义后，依赖全树清单的下游消费方可能受影响。

**缓解：** 全树清单不符合 FDA 惯例，本身即缺陷来源；如需全树完整性证据，应由 `PublishOutputVerifier` 的 SHA256 证据承担，而非 `index-md5.txt`。

**风险：** 两个 writer 与文件写入层分散修改，可能出现遗漏导致部分 leaf 仍输出 sha256。

**缓解：** 由包模型作为单一事实来源统一提供 MD5，writer 仅读取字段；测试覆盖两个 writer 与 `index-md5.txt` 的输出。

## 验收标准

校验和 MD5 合规修复在满足以下条件时视为完成：

- ICH `index.xml` 与 US Regional `us-regional.xml` 的所有 leaf 输出 `checksum-type="md5"` 与对应文件的 MD5 值。
- `index-md5.txt` 仅包含 `index.xml` 自身的 MD5。
- 包模型携带 MD5，并作为 backbone 校验和的事实来源。
- SHA256 不再出现在 backbone 校验和属性中，但仍用于发布产物完整性证据。
- 原先断言 sha256 的 writer 测试改为断言 md5 并通过。
- 现有后端与前端测试套件仍然通过。
