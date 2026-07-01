# Publish Job Service Decomposition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move artifact/report file IO out of `PublishJobService` into focused collaborators and remove the empty backbone report placeholder.

**Architecture:** Add an application-layer artifact store abstraction and an infrastructure implementation that enforces `IWorkspacePathPolicy` before disk access. Extract `PublishReportStore` for report JSON read/write and `PublishArtifactResolver` for artifact DTOs, downloads, content types, and artifact summary statistics. Keep publish orchestration in `PublishJobService` and preserve public DTO/API contracts.

**Tech Stack:** .NET 8, xUnit, Microsoft.Extensions.DependencyInjection, System.Text.Json.

---

## Scope Check

This plan implements roadmap Task 7 and the code-facing acceptance criteria from `docs/superpowers/specs/2026-06-18-publish-job-service-decomposition-design.md`.

Do not change publish API DTOs, publish job status transitions, validation/readiness behavior, or generated package naming. Do not delete `.worktrees` in this task; destructive worktree cleanup from the design spec requires separate explicit confirmation. This task may audit `.worktrees` and confirm `.gitignore` coverage only.

## File Structure Map

- Create: `src/RATools.Application/Abstractions/Publishing/IPublishArtifactStore.cs`
  - Defines async file/directory existence, size, text read/write, and directory stats operations for publish artifacts.
- Create: `src/RATools.Infrastructure/Publishing/LocalPublishArtifactStore.cs`
  - Implements `IPublishArtifactStore` with `IWorkspacePathPolicy.EnsureAllowed` before each IO entry.
- Create: `src/RATools.Application/Publishing/PublishArtifactResolver.cs`
  - Builds artifact lists/download records, resolves supported artifact names, maps content types, and builds artifact summary asynchronously.
- Create: `src/RATools.Application/Publishing/PublishReportStore.cs`
  - Reads and writes `PublishExecutionReportDto` JSON and preserves existing report exception semantics.
- Modify: `src/RATools.Application/Publishing/PublishJobService.cs`
  - Delegates report/artifact IO to extracted collaborators and contains no `File`, `FileInfo`, `Directory`, or `DirectoryInfo` references.
- Modify: `src/RATools.Application/Publishing/BackboneService.cs`
  - Stops passing `"{}"` placeholder report content.
- Modify: `src/RATools.Application/Abstractions/Publishing/IBackboneFileWriter.cs`
  - Removes `reportContent` from `SaveAsync`.
- Modify: `src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs`
  - Returns report path without writing placeholder report JSON.
- Modify: `src/RATools.Application/DependencyInjection.cs`
  - Registers `PublishArtifactResolver` and `PublishReportStore`.
- Modify: `src/RATools.Infrastructure/DependencyInjection.cs`
  - Registers `IPublishArtifactStore` as `LocalPublishArtifactStore`.
- Create: `tests/RATools.Tests/Publishing/PublishArtifactResolverTests.cs`
  - Covers artifact existence, size, content types, unsupported artifact names, summary stats, and cancellation.
- Create: `tests/RATools.Tests/Publishing/PublishReportStoreTests.cs`
  - Covers report read/write, missing report, corrupted JSON, non-completed job, and cancellation.
- Create: `tests/RATools.Tests/Infrastructure/LocalPublishArtifactStoreTests.cs`
  - Covers path-policy enforcement for each IO entry.
- Modify: `tests/RATools.Tests/Publishing/LocalBackboneFileWriterTests.cs`
  - Asserts no placeholder report JSON is written by backbone writer.
- Modify: `tests/RATools.Tests/Publishing/PublishJobServiceEvidenceTests.cs`
- Modify: `tests/RATools.Tests/Publishing/PublishJobServiceRealEctdIntegrationTests.cs`
  - Update direct `PublishJobService` constructors with extracted collaborators and assert final report remains authoritative.

## Task 1: Add Publish Artifact Store Abstraction

**Files:**
- Create: `src/RATools.Application/Abstractions/Publishing/IPublishArtifactStore.cs`
- Create: `tests/RATools.Tests/Infrastructure/LocalPublishArtifactStoreTests.cs`

- [ ] **Step 1: Write failing infrastructure tests**

Create `tests/RATools.Tests/Infrastructure/LocalPublishArtifactStoreTests.cs` with tests that instantiate `LocalPublishArtifactStore` using a recording `IWorkspacePathPolicy`. Cover `ExistsAsync`, `GetSizeAsync`, `ReadAllTextAsync`, `WriteAllTextAsync`, and `GetDirectoryStatsAsync`.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~LocalPublishArtifactStoreTests"
```

Expected: fail because `LocalPublishArtifactStore` and `IPublishArtifactStore` do not exist.

- [ ] **Step 2: Implement abstraction and local store**

Define:

```csharp
namespace RATools.Application.Abstractions.Publishing;

public sealed record PublishArtifactDirectoryStats(int FileCount, long TotalSizeBytes);

public interface IPublishArtifactStore
{
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
    Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<PublishArtifactDirectoryStats> GetDirectoryStatsAsync(string directoryPath, CancellationToken cancellationToken = default);
}
```

Implement `LocalPublishArtifactStore` so every public method first resolves `var allowedPath = pathPolicy.EnsureAllowed(path);`, then performs the disk operation. `ExistsAsync` returns true for either a file or directory. `WriteAllTextAsync` creates the parent directory when present.

- [ ] **Step 3: Verify store tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~LocalPublishArtifactStoreTests"
```

Expected: all store tests pass.

## Task 2: Extract PublishReportStore

**Files:**
- Create: `src/RATools.Application/Publishing/PublishReportStore.cs`
- Create: `tests/RATools.Tests/Publishing/PublishReportStoreTests.cs`

- [ ] **Step 1: Write failing report store tests**

Cover:

- completed job with missing output directory throws `PublishJobReportUnavailableException`;
- completed job with missing report throws `PublishJobReportUnavailableException`;
- corrupted JSON throws `PublishJobReportCorruptedException`;
- write then read round-trips `PublishExecutionReportDto`;
- already-cancelled token throws `OperationCanceledException` before store IO.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PublishReportStoreTests"
```

Expected: fail because `PublishReportStore` does not exist.

- [ ] **Step 2: Implement PublishReportStore**

Move report JSON logic from `PublishJobService.GetExecutionReportAsync` and `WriteFinalReportAsync` into:

```csharp
public sealed class PublishReportStore(IPublishArtifactStore artifactStore)
{
    public Task<PublishExecutionReportDto> ReadAsync(PublishJob job, CancellationToken cancellationToken = default);
    public Task WriteAsync(PublishExecutionReportDto report, CancellationToken cancellationToken = default);
}
```

Use `PublishOutputNaming.BuildPublishReportPath(job.OutputPath, job.SequenceNumber, job.Id)`, preserve existing exception messages closely, and deserialize with `PropertyNameCaseInsensitive = true`.

- [ ] **Step 3: Verify report store tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PublishReportStoreTests"
```

Expected: all report store tests pass.

## Task 3: Extract PublishArtifactResolver

**Files:**
- Create: `src/RATools.Application/Publishing/PublishArtifactResolver.cs`
- Create: `tests/RATools.Tests/Publishing/PublishArtifactResolverTests.cs`

- [ ] **Step 1: Write failing resolver tests**

Cover:

- `BuildArtifactsAsync` returns `BackboneXml`, `PublishReport`, and `PackageZip` with correct existence, size, and content type;
- `ResolveAsync` is case-insensitive and returns null for an unsupported name;
- `BuildArtifactSummaryAsync` returns directory file count/total size and package size;
- already-cancelled token throws `OperationCanceledException`.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PublishArtifactResolverTests"
```

Expected: fail because `PublishArtifactResolver` does not exist.

- [ ] **Step 2: Implement PublishArtifactResolver**

Move artifact DTO creation, artifact name resolution, content-type mapping, and artifact summary logic from `PublishJobService`.

Expose:

```csharp
public sealed class PublishArtifactResolver(IPublishArtifactStore artifactStore)
{
    public Task<PublishArtifactsDto> BuildArtifactsAsync(PublishJob job, CancellationToken cancellationToken = default);
    public Task<PublishArtifactDto?> ResolveAsync(PublishJob job, string artifactName, CancellationToken cancellationToken = default);
    public Task<PublishArtifactSummaryDto?> BuildArtifactSummaryAsync(PublishJobDto publishJob, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Verify resolver tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PublishArtifactResolverTests"
```

Expected: all resolver tests pass.

## Task 4: Wire Stores Into PublishJobService

**Files:**
- Modify: `src/RATools.Application/Publishing/PublishJobService.cs`
- Modify: `src/RATools.Application/DependencyInjection.cs`
- Modify: `src/RATools.Infrastructure/DependencyInjection.cs`
- Modify: service construction in existing publish tests.

- [ ] **Step 1: Update PublishJobService constructor and delegation**

Inject `PublishArtifactResolver artifactResolver` and `PublishReportStore reportStore`.

Replace:

- `GetExecutionReportAsync` report file logic with `return await reportStore.ReadAsync(job, cancellationToken);`
- `GetArtifactsAsync` artifact list logic with `return await artifactResolver.BuildArtifactsAsync(job, cancellationToken);`
- `GetArtifactDownloadAsync` resolver call with `await artifactResolver.ResolveAsync(job, artifactName, cancellationToken);`
- `BuildAndPersistReportAsync` artifact summary with `await artifactResolver.BuildArtifactSummaryAsync(jobDto, cancellationToken);`
- `WriteFinalReportAsync` with `await reportStore.WriteAsync(report, cancellationToken);`

- [ ] **Step 2: Register dependencies**

Add application registrations:

```csharp
services.AddSingleton<PublishArtifactResolver>();
services.AddSingleton<PublishReportStore>();
```

Add infrastructure registration:

```csharp
services.AddSingleton<IPublishArtifactStore, LocalPublishArtifactStore>();
```

- [ ] **Step 3: Verify no direct IO remains in PublishJobService**

Run:

```powershell
Select-String -Path src\RATools.Application\Publishing\PublishJobService.cs -Pattern 'File\.|Directory\.|FileInfo|DirectoryInfo'
```

Expected: no matches.

## Task 5: Remove Backbone Placeholder Report

**Files:**
- Modify: `src/RATools.Application/Publishing/BackboneService.cs`
- Modify: `src/RATools.Application/Abstractions/Publishing/IBackboneFileWriter.cs`
- Modify: `src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs`
- Modify: `tests/RATools.Tests/Publishing/LocalBackboneFileWriterTests.cs`

- [ ] **Step 1: Write/adjust failing placeholder test**

Change `LocalBackboneFileWriterTests.SaveAsync_WritesGeneratedFilesDocumentsDtdsMd5AndPackageZip` so it asserts `result.ReportPath` is the expected path but `File.Exists(result.ReportPath)` is false immediately after `SaveAsync`.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~LocalBackboneFileWriterTests"
```

Expected: fail until `LocalBackboneFileWriter` stops writing report content.

- [ ] **Step 2: Remove reportContent parameter**

Remove `reportContent` from `IBackboneFileWriter.SaveAsync`, `LocalBackboneFileWriter.SaveAsync`, `BackboneService.GenerateAsync`, and all test call sites. Keep `reportFileName` so the writer can return the eventual report path.

- [ ] **Step 3: Verify writer tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~LocalBackboneFileWriterTests"
```

Expected: writer tests pass and no placeholder report is produced.

## Task 6: Full Backend Verification and Commit

**Files:**
- All Task 7 files.

- [ ] **Step 1: Run targeted publish tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PublishArtifactResolverTests|FullyQualifiedName~PublishReportStoreTests|FullyQualifiedName~LocalPublishArtifactStoreTests|FullyQualifiedName~PublishJobServiceEvidenceTests|FullyQualifiedName~PublishJobServiceRealEctdIntegrationTests|FullyQualifiedName~LocalBackboneFileWriterTests"
```

Expected: all targeted publish decomposition tests pass.

- [ ] **Step 2: Run direct IO boundary scan**

Run:

```powershell
Select-String -Path src\RATools.Application\Publishing\PublishJobService.cs -Pattern 'File\.|Directory\.|FileInfo|DirectoryInfo'
Select-String -Path src\RATools.Application\Publishing\BackboneService.cs -Pattern '"\{\}"'
```

Expected: no matches.

- [ ] **Step 3: Run full backend tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release
```

Expected: 0 failed tests.

- [ ] **Step 4: Commit publish job decomposition**

Run:

```powershell
git add src\RATools.Application src\RATools.Infrastructure tests\RATools.Tests docs\superpowers\plans\2026-07-01-publish-job-service-decomposition-execution.md
git commit -m "refactor: decompose publish job file IO"
```

If the docs path is ignored, use `git add -f` for only this plan file.

## Self-Review Notes

- Spec coverage: covers artifact resolver, report store, path-policy-backed IO store, cancellation-token propagation, removal of `"{}"` placeholder, and direct IO scan. Worktree deletion is intentionally excluded because it is destructive and needs separate explicit confirmation.
- Placeholder scan: no task uses TBD/TODO language; each task has concrete files and commands.
- Type consistency: `IPublishArtifactStore`, `PublishArtifactResolver`, and `PublishReportStore` method names are consistent across tasks and planned call sites.
