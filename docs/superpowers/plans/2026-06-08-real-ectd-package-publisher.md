# Real eCTD Package Publisher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace prototype publish output with a package publisher that writes real ICH `index.xml`, US `m1/us/us-regional.xml`, documents, standards DTD assets, `index-md5.txt`, and a delivery zip.

**Architecture:** Keep `IBackboneService.GenerateAsync` as the publish-job orchestration boundary, but make `BackboneService` delegate to `IEctdPackageModelBuilder`, `IIchIndexXmlWriter`, and `IUsRegionalXmlWriter`. Extend the infrastructure file writer to persist multiple generated files plus `EctdPublishedFile` document entries into the existing `_jobs`, `_artifacts`, and `_packages` layout.

**Tech Stack:** .NET 8, xUnit, LINQ to XML, `System.IO.Compression`, existing publish job service, package model, ICH writer, US regional writer, and bundled DTD assets.

---

## File Structure

- Create `src/RATools.Application/Abstractions/Publishing/BackboneGeneratedFile.cs`
  - Immutable record for generated package files such as `index.xml` and `m1/us/us-regional.xml`.
- Modify `src/RATools.Application/Abstractions/Publishing/IBackboneFileWriter.cs`
  - Replace the single XML content and `SubmissionDocument` inputs with generated files and `EctdPublishedFile` entries.
- Modify `src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs`
  - Write generated files, copy published files, copy DTD assets, build `index-md5.txt`, and create the delivery zip.
- Create `tests/RATools.Tests/Publishing/LocalBackboneFileWriterTests.cs`
  - Cover complete package layout and missing source file failure.
- Modify `src/RATools.Application/Publishing/BackboneService.cs`
  - Use package builder and both XML writers instead of prototype XML generation.
- Create `tests/RATools.Tests/Publishing/BackboneServiceTests.cs`
  - Cover orchestration and generated-file handoff.
- Modify `src/RATools.Application/Publishing/PublishOutputVerifier.cs`
  - Read xlink hrefs from both `http://www.w3.org/1999/xlink` and `http://www.w3c.org/1999/xlink`.
- Modify `tests/RATools.Tests/Publishing/PublishOutputVerifierTests.cs`
  - Add DTD-compatible xlink namespace coverage.
- Modify `src/RATools.Application/Publishing/PublishJobService.cs`
  - Write the final report without recreating package zip from the report directory.
- Modify `tests/RATools.Tests/Publishing/PublishJobServiceEvidenceTests.cs`
  - Assert package zip remains the delivery zip after final report write.

## Task 1: File Writer Contract And Local Package Layout

**Files:**
- Create: `src/RATools.Application/Abstractions/Publishing/BackboneGeneratedFile.cs`
- Modify: `src/RATools.Application/Abstractions/Publishing/IBackboneFileWriter.cs`
- Modify: `src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs`
- Create: `tests/RATools.Tests/Publishing/LocalBackboneFileWriterTests.cs`

- [ ] **Step 1: Write failing complete-layout test**

Create `tests/RATools.Tests/Publishing/LocalBackboneFileWriterTests.cs` with a test named `SaveAsync_WritesGeneratedFilesDocumentsDtdsMd5AndPackageZip`.

The test should:

- create a temp root;
- create a source file at `{temp}/source/0001/m1/us/12-cover-letters/cover.pdf`;
- create a `LocalBackboneFileWriter` with `Options.Create(new BackboneOutputOptions { RootPath = tempRoot })`;
- pass generated files `index.xml` and `m1/us/us-regional.xml`;
- pass one `EctdPublishedFile` whose href is `m1/us/12-cover-letters/cover.pdf`;
- assert returned `FilePath` is the written `index.xml`;
- assert `index.xml`, `m1/us/us-regional.xml`, the copied PDF, `util/dtd/ich-ectd-3-2.dtd`, `util/dtd/us-regional-v3-3.dtd`, and `index-md5.txt` exist;
- assert the package zip contains those same delivery entries.

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.LocalBackboneFileWriterTests.SaveAsync_WritesGeneratedFilesDocumentsDtdsMd5AndPackageZip
```

Expected: FAIL at compile time because `BackboneGeneratedFile` does not exist and `IBackboneFileWriter.SaveAsync` has the old signature.

- [ ] **Step 3: Add generated file contract and update writer signature**

Create `BackboneGeneratedFile`:

```csharp
namespace RATools.Application.Abstractions.Publishing;

public sealed record BackboneGeneratedFile(string RelativePath, string Content);
```

Update `IBackboneFileWriter.SaveAsync` to accept:

```csharp
IReadOnlyCollection<BackboneGeneratedFile> generatedFiles,
string reportFileName,
string packageFileName,
string reportContent,
IReadOnlyCollection<EctdPublishedFile> publishedFiles,
CancellationToken cancellationToken = default
```

- [ ] **Step 4: Implement local package writer**

Update `LocalBackboneFileWriter` so it:

- validates `generatedFiles` and `publishedFiles`;
- resolves all generated and published relative paths below the delivery root;
- writes all generated files;
- copies all published source files and throws `FileNotFoundException` when a source is missing;
- copies `AppContext.BaseDirectory/reference/dtd/*.dtd` into `util/dtd`;
- writes `index-md5.txt` after all files are present;
- creates package zip from the delivery root;
- returns the absolute path to `index.xml`.

- [ ] **Step 5: Run layout test to verify it passes**

Run the same focused test command from Step 2.

Expected: PASS.

- [ ] **Step 6: Add missing source file test**

Add `SaveAsync_ThrowsWhenPublishedSourceFileIsMissing` to `LocalBackboneFileWriterTests`. The test should call `SaveAsync` with an `EctdPublishedFile` whose `SourcePath` does not exist and assert `FileNotFoundException`.

- [ ] **Step 7: Run local writer tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.LocalBackboneFileWriterTests
```

Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add src\RATools.Application\Abstractions\Publishing\BackboneGeneratedFile.cs src\RATools.Application\Abstractions\Publishing\IBackboneFileWriter.cs src\RATools.Infrastructure\Publishing\LocalBackboneFileWriter.cs
git add -f tests\RATools.Tests\Publishing\LocalBackboneFileWriterTests.cs
git commit -m "feat: write real eCTD package files"
```

## Task 2: BackboneService Real Package Orchestration

**Files:**
- Modify: `src/RATools.Application/Publishing/BackboneService.cs`
- Create: `tests/RATools.Tests/Publishing/BackboneServiceTests.cs`

- [ ] **Step 1: Write failing orchestration test**

Create `tests/RATools.Tests/Publishing/BackboneServiceTests.cs` with `GenerateAsync_BuildsPackageGeneratesBothXmlFilesAndWritesPackage`.

The test should use fakes for `IEctdPackageModelBuilder`, `IIchIndexXmlWriter`, `IUsRegionalXmlWriter`, and `IBackboneFileWriter`. It should assert:

- the builder receives the requested application id and sequence number;
- the file writer receives generated files `index.xml` and `m1/us/us-regional.xml`;
- the file writer receives the package published files;
- the returned DTO has `FileName == "index.xml"`, `XmlContent` equal to the ICH writer XML, and paths from the file writer.

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.BackboneServiceTests.GenerateAsync_BuildsPackageGeneratesBothXmlFilesAndWritesPackage
```

Expected: FAIL because the current `BackboneService` uses repositories and prototype XML instead of the package model and real writers.

- [ ] **Step 3: Replace BackboneService internals**

Change `BackboneService` constructor dependencies to:

```csharp
IEctdPackageModelBuilder packageModelBuilder,
IIchIndexXmlWriter ichIndexXmlWriter,
IUsRegionalXmlWriter usRegionalXmlWriter,
IBackboneFileWriter backboneFileWriter
```

In `GenerateAsync`, build the package, generate both XML files, call the file writer with generated files and `package.PublishedFiles`, and return `GeneratedBackboneDto` based on the ICH `index.xml`.

- [ ] **Step 4: Run orchestration test**

Run the focused test from Step 2.

Expected: PASS.

- [ ] **Step 5: Run existing writer and package model tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RATools.Tests.Publishing.PackageModel|FullyQualifiedName~RATools.Tests.Publishing.Ich|FullyQualifiedName~RATools.Tests.Publishing.UsRegional|FullyQualifiedName~RATools.Tests.Publishing.BackboneServiceTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\RATools.Application\Publishing\BackboneService.cs
git add -f tests\RATools.Tests\Publishing\BackboneServiceTests.cs
git commit -m "feat: orchestrate real ectd package generation"
```

## Task 3: Publish Evidence And Final Zip Preservation

**Files:**
- Modify: `src/RATools.Application/Publishing/PublishOutputVerifier.cs`
- Modify: `src/RATools.Application/Publishing/PublishJobService.cs`
- Modify: `tests/RATools.Tests/Publishing/PublishOutputVerifierTests.cs`
- Modify: `tests/RATools.Tests/Publishing/PublishJobServiceEvidenceTests.cs`

- [ ] **Step 1: Write failing verifier namespace test**

Add `VerifyAsync_ReadsReferencesFromDtdCompatibleXlinkNamespace` to `PublishOutputVerifierTests`. The XML should declare `xmlns:xlink="http://www.w3c.org/1999/xlink"` and reference an existing output file. Assert the verifier reports no missing referenced file.

- [ ] **Step 2: Run verifier test to verify it fails**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PublishOutputVerifierTests.VerifyAsync_ReadsReferencesFromDtdCompatibleXlinkNamespace
```

Expected: FAIL because the verifier currently only reads `http://www.w3.org/1999/xlink`.

- [ ] **Step 3: Update verifier xlink handling**

Update `ReadDocumentReferences` so it reads href attributes from both supported xlink namespace URIs and returns a distinct set.

- [ ] **Step 4: Add failing package preservation assertion**

Update `PublishJobServiceEvidenceTests.ExecuteAsync_StoresIntegrityEvidenceInExecutionReport` to open the package zip after `ExecuteAsync` returns and assert it contains `index.xml` and `leaf.txt`. This should fail before the service fix because `WriteFinalReportAsync` recreates the package from the report directory.

- [ ] **Step 5: Stop final report from recreating package zip**

Update `PublishJobService.WriteFinalReportAsync` so it writes the final report JSON and does not delete or recreate `packagePath`.

- [ ] **Step 6: Run publish evidence tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RATools.Tests.Publishing.PublishOutputVerifierTests|FullyQualifiedName~RATools.Tests.Publishing.PublishJobServiceEvidenceTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src\RATools.Application\Publishing\PublishOutputVerifier.cs src\RATools.Application\Publishing\PublishJobService.cs
git add -f tests\RATools.Tests\Publishing\PublishOutputVerifierTests.cs tests\RATools.Tests\Publishing\PublishJobServiceEvidenceTests.cs
git commit -m "fix: preserve delivery zip after publish report"
```

## Task 4: Full Backend Verification

**Files:**
- No production files unless a verification failure exposes a real defect.

- [ ] **Step 1: Run full backend tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj
```

Expected: PASS. `NU1900` warnings caused by restricted NuGet vulnerability-feed access are acceptable if the test exit code is 0.

- [ ] **Step 2: Run diff checks**

Run:

```powershell
git diff --check
```

Expected: exit code 0, except do not normalize bundled DTD whitespace if the command reports pre-existing DTD whitespace outside this branch's edited files.

- [ ] **Step 3: Inspect status and recent commits**

Run:

```powershell
git status --short
git log --oneline -8
```

Expected: clean status after the final commit and recent commits showing the package publisher work.
