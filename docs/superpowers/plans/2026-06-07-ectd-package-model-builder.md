# eCTD Package Model Builder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a tested application-layer eCTD package model builder for FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3 package facts.

**Architecture:** Add a focused `RATools.Application.Publishing.PackageModel` module that reads existing repositories and standards metadata, then returns immutable package records. Keep XML generation and `BackboneService` unchanged so this batch creates a stable package contract for later `index.xml` and `us-regional.xml` writers.

**Tech Stack:** .NET, xUnit, Microsoft.Extensions.DependencyInjection, existing RATools domain/application abstractions.

---

## File Structure

- Create `src/RATools.Application/Publishing/PackageModel/BuildEctdPackageRequest.cs`
  - Request record for application id and sequence number.
- Create `src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs`
  - Immutable records: `EctdSequencePackage`, `EctdApplicationMetadata`, `EctdSequenceMetadata`, `EctdLeaf`, `EctdLifecycleReference`, `EctdPublishedFile`.
- Create `src/RATools.Application/Publishing/PackageModel/EctdPackageExceptions.cs`
  - Package-model-specific exception types with contextual properties.
- Create `src/RATools.Application/Publishing/PackageModel/IEctdPackageModelBuilder.cs`
  - Application-layer service contract.
- Create `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`
  - Main builder implementation.
- Modify `src/RATools.Application/DependencyInjection.cs`
  - Register `IEctdPackageModelBuilder` as scoped.
- Create `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`
  - Unit tests with in-memory stub repositories and a stub standards provider.

## Task 1: Add Package Model Contract

**Files:**
- Create: `src/RATools.Application/Publishing/PackageModel/BuildEctdPackageRequest.cs`
- Create: `src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs`
- Create: `src/RATools.Application/Publishing/PackageModel/EctdPackageExceptions.cs`
- Create: `src/RATools.Application/Publishing/PackageModel/IEctdPackageModelBuilder.cs`
- Test: `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`

- [ ] **Step 1: Write the failing contract test**

Add this initial test file:

```csharp
using RATools.Application.Publishing.PackageModel;

namespace RATools.Tests.Publishing.PackageModel;

public sealed class EctdPackageModelBuilderTests
{
    [Fact]
    public void PackageRecords_ExposeExpectedImmutableContract()
    {
        var applicationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var placementId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var documentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var package = new EctdSequencePackage(
            applicationId,
            "ANDA123456",
            "0001",
            "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
            "3.2.2",
            "3.3",
            new EctdApplicationMetadata("ANDA123456", "Acme Pharma", "US", "us-fda-ectd-322", "anda"),
            new EctdSequenceMetadata("0001", "original-application", null, "Initial sequence", "Acme Pharma", "356h"),
            [
                new EctdLeaf(
                    placementId,
                    documentId,
                    "leaf-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    "0001",
                    "m1.1",
                    "m1",
                    "new",
                    "Cover Letter",
                    "m1/us/cover.pdf",
                    "cover.pdf",
                    "application/pdf",
                    "C:/work/0001/m1/us/cover.pdf",
                    20,
                    "sha256",
                    null)
            ],
            [],
            [
                new EctdPublishedFile(
                    documentId,
                    "C:/work/0001/m1/us/cover.pdf",
                    "m1/us/cover.pdf",
                    "cover.pdf",
                    20,
                    "sha256")
            ]);

        Assert.Equal(applicationId, package.ApplicationId);
        Assert.Equal("3.2.2", package.IchEctdVersion);
        Assert.Single(package.Module1Leaves);
        Assert.Empty(package.IchBackboneLeaves);
        Assert.Single(package.PublishedFiles);
    }
}
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests.PackageRecords_ExposeExpectedImmutableContract
```

Expected: FAIL at compile time because `RATools.Application.Publishing.PackageModel` and package records do not exist.

- [ ] **Step 3: Add the package model records and interface**

Create `src/RATools.Application/Publishing/PackageModel/BuildEctdPackageRequest.cs`:

```csharp
namespace RATools.Application.Publishing.PackageModel;

public sealed record BuildEctdPackageRequest(Guid ApplicationId, string SequenceNumber);
```

Create `src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs`:

```csharp
namespace RATools.Application.Publishing.PackageModel;

public sealed record EctdSequencePackage(
    Guid ApplicationId,
    string ApplicationNumber,
    string SequenceNumber,
    string StandardsProfile,
    string IchEctdVersion,
    string UsRegionalModule1Version,
    EctdApplicationMetadata Application,
    EctdSequenceMetadata Sequence,
    IReadOnlyCollection<EctdLeaf> Module1Leaves,
    IReadOnlyCollection<EctdLeaf> IchBackboneLeaves,
    IReadOnlyCollection<EctdPublishedFile> PublishedFiles);

public sealed record EctdApplicationMetadata(
    string ApplicationNumber,
    string SponsorName,
    string Region,
    string TemplateKey,
    string? ApplicationType);

public sealed record EctdSequenceMetadata(
    string SequenceNumber,
    string SubmissionType,
    string? SubmissionSubtype,
    string Description,
    string ApplicantName,
    string? FormType);

public sealed record EctdLeaf(
    Guid PlacementId,
    Guid DocumentId,
    string LeafId,
    string SequenceNumber,
    string CtdSection,
    string Module,
    string Operation,
    string Title,
    string Href,
    string FileName,
    string MediaType,
    string SourcePath,
    long FileSize,
    string Sha256,
    EctdLifecycleReference? Lifecycle);

public sealed record EctdLifecycleReference(
    Guid TargetPlacementId,
    Guid TargetDocumentId,
    string TargetSequenceNumber,
    string ModifiedFileHref);

public sealed record EctdPublishedFile(
    Guid DocumentId,
    string SourcePath,
    string Href,
    string FileName,
    long FileSize,
    string Sha256);
```

Create `src/RATools.Application/Publishing/PackageModel/EctdPackageExceptions.cs`:

```csharp
namespace RATools.Application.Publishing.PackageModel;

public abstract class EctdPackageException(string message) : Exception(message);

public sealed class EctdPackageApplicationNotFoundException(Guid applicationId)
    : EctdPackageException($"Application {applicationId} was not found.")
{
    public Guid ApplicationId { get; } = applicationId;
}

public sealed class EctdPackageSequenceNotFoundException(Guid applicationId, string sequenceNumber)
    : EctdPackageException($"Sequence {sequenceNumber} does not exist on application {applicationId}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;
}

public sealed class EctdPackageDocumentNotFoundException(Guid applicationId, string sequenceNumber, Guid placementId, Guid documentId)
    : EctdPackageException($"Placement {placementId} in sequence {sequenceNumber} references missing document {documentId}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid PlacementId { get; } = placementId;

    public Guid DocumentId { get; } = documentId;
}

public sealed class EctdPackageUnsupportedOperationException(Guid applicationId, string sequenceNumber, Guid placementId, int operationValue)
    : EctdPackageException($"Placement {placementId} in sequence {sequenceNumber} has unsupported operation value {operationValue}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid PlacementId { get; } = placementId;

    public int OperationValue { get; } = operationValue;
}

public sealed class EctdPackageInvalidSectionException(Guid applicationId, string sequenceNumber, Guid placementId, string ctdSection)
    : EctdPackageException($"Placement {placementId} in sequence {sequenceNumber} has unsupported CTD section '{ctdSection}'.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid PlacementId { get; } = placementId;

    public string CtdSection { get; } = ctdSection;
}

public sealed class EctdPackageLifecycleTargetException(
    Guid applicationId,
    string sequenceNumber,
    Guid placementId,
    Guid? targetPlacementId,
    string reason)
    : EctdPackageException($"Placement {placementId} in sequence {sequenceNumber} requires a valid lifecycle target: {reason}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid PlacementId { get; } = placementId;

    public Guid? TargetPlacementId { get; } = targetPlacementId;

    public string Reason { get; } = reason;
}
```

Create `src/RATools.Application/Publishing/PackageModel/IEctdPackageModelBuilder.cs`:

```csharp
namespace RATools.Application.Publishing.PackageModel;

public interface IEctdPackageModelBuilder
{
    Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Run the focused test to verify it passes**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests.PackageRecords_ExposeExpectedImmutableContract
```

Expected: PASS.

- [ ] **Step 5: Commit the package contract**

Run:

```powershell
git add -f tests\RATools.Tests\Publishing\PackageModel\EctdPackageModelBuilderTests.cs
git add src\RATools.Application\Publishing\PackageModel
git commit -m "feat: add eCTD package model contract"
```

## Task 2: Build Package Metadata and Empty Leaf Collections

**Files:**
- Modify: `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`
- Create: `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`

- [ ] **Step 1: Add failing tests for metadata and not-found errors**

Append these tests and helpers to `EctdPackageModelBuilderTests`:

```csharp
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Standards;
using RATools.Domain.Applications;
using RATools.Domain.Documents;

[Fact]
public async Task BuildAsync_BuildsPackageMetadataWithSequenceFallbacks()
{
    var applicationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var application = SubmissionApplication.Rehydrate(
        applicationId,
        "ANDA123456",
        "US",
        "Acme Pharma",
        DateTime.UtcNow,
        [SubmissionSequence.Rehydrate("0001", "original-application", "Initial sequence", DateTime.UtcNow)],
        "C:/workspace/ANDA123456",
        "us-fda-ectd-322");
    var builder = CreateBuilder(application, [], []);

    var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001"));

    Assert.Equal(applicationId, package.ApplicationId);
    Assert.Equal("ANDA123456", package.ApplicationNumber);
    Assert.Equal("0001", package.SequenceNumber);
    Assert.Equal("FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3", package.StandardsProfile);
    Assert.Equal("3.2.2", package.IchEctdVersion);
    Assert.Equal("3.3", package.UsRegionalModule1Version);
    Assert.Equal("ANDA123456", package.Application.ApplicationNumber);
    Assert.Equal("Acme Pharma", package.Application.SponsorName);
    Assert.Equal("US", package.Application.Region);
    Assert.Equal("us-fda-ectd-322", package.Application.TemplateKey);
    Assert.Null(package.Application.ApplicationType);
    Assert.Equal("original-application", package.Sequence.SubmissionType);
    Assert.Null(package.Sequence.SubmissionSubtype);
    Assert.Equal("Initial sequence", package.Sequence.Description);
    Assert.Equal("Acme Pharma", package.Sequence.ApplicantName);
    Assert.Null(package.Sequence.FormType);
    Assert.Empty(package.Module1Leaves);
    Assert.Empty(package.IchBackboneLeaves);
    Assert.Empty(package.PublishedFiles);
}

[Fact]
public async Task BuildAsync_UsesSequencePublishingMetadataWhenPresent()
{
    var metadata = SequencePublishingMetadata.Create(
        "anda",
        "supplement",
        "efficacy",
        "Safety update",
        "Regulatory Applicant LLC",
        "356h");
    var applicationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    var application = SubmissionApplication.Rehydrate(
        applicationId,
        "ANDA123456",
        "US",
        "Acme Pharma",
        DateTime.UtcNow,
        [SubmissionSequence.Rehydrate("0002", "legacy-type", "Legacy description", DateTime.UtcNow, metadata)],
        "C:/workspace/ANDA123456",
        "us-fda-ectd-322");
    var builder = CreateBuilder(application, [], []);

    var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0002"));

    Assert.Equal("anda", package.Application.ApplicationType);
    Assert.Equal("supplement", package.Sequence.SubmissionType);
    Assert.Equal("efficacy", package.Sequence.SubmissionSubtype);
    Assert.Equal("Safety update", package.Sequence.Description);
    Assert.Equal("Regulatory Applicant LLC", package.Sequence.ApplicantName);
    Assert.Equal("356h", package.Sequence.FormType);
}

[Fact]
public async Task BuildAsync_ThrowsWhenApplicationIsMissing()
{
    var builder = CreateBuilder(null, [], []);
    var applicationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    var exception = await Assert.ThrowsAsync<EctdPackageApplicationNotFoundException>(
        () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

    Assert.Equal(applicationId, exception.ApplicationId);
}

[Fact]
public async Task BuildAsync_ThrowsWhenSequenceIsMissing()
{
    var applicationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    var application = SubmissionApplication.Rehydrate(
        applicationId,
        "ANDA123456",
        "US",
        "Acme Pharma",
        DateTime.UtcNow,
        [SubmissionSequence.Rehydrate("0001", "original-application", "Initial sequence", DateTime.UtcNow)],
        "C:/workspace/ANDA123456",
        "us-fda-ectd-322");
    var builder = CreateBuilder(application, [], []);

    var exception = await Assert.ThrowsAsync<EctdPackageSequenceNotFoundException>(
        () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0009")));

    Assert.Equal(applicationId, exception.ApplicationId);
    Assert.Equal("0009", exception.SequenceNumber);
}
```

Also add these helpers inside the test class:

```csharp
private static EctdPackageModelBuilder CreateBuilder(
    SubmissionApplication? application,
    IReadOnlyCollection<DocumentPlacement> placements,
    IReadOnlyCollection<SubmissionDocument> documents)
{
    return new EctdPackageModelBuilder(
        new StubApplicationRepository(application),
        new StubDocumentPlacementRepository(placements),
        new StubDocumentRepository(documents),
        new StubStandardsProfileProvider());
}

private sealed class StubStandardsProfileProvider : IStandardsProfileProvider
{
    public StandardsProfile GetProfile(string templateKey)
    {
        return new StandardsProfile(
            templateKey,
            "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
            "FDA CDER/CBER",
            "United States",
            "3.2.2",
            "3.3",
            "1.9",
            "4.5",
            [],
            []);
    }
}

private sealed class StubApplicationRepository(SubmissionApplication? application) : IApplicationRepository
{
    public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(application?.Id == id ? application : null);
    public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>(application is null ? [] : [application]);
}

private sealed class StubDocumentRepository(IReadOnlyCollection<SubmissionDocument> documents) : IDocumentRepository
{
    public Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(documents.SingleOrDefault(x => x.Id == id));
    public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(documents);
}

private sealed class StubDocumentPlacementRepository(IReadOnlyCollection<DocumentPlacement> placements) : IDocumentPlacementRepository
{
    public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(placements.SingleOrDefault(x => x.Id == id));
    public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(placements);
    public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placements.Where(x => x.ApplicationId == applicationId).ToArray());
    public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<DocumentPlacement>>(placements.Where(x => x.ApplicationId == applicationId && x.SequenceNumber == sequenceNumber).ToArray());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests
```

Expected: FAIL at compile time because `EctdPackageModelBuilder` does not exist.

- [ ] **Step 3: Implement metadata-only builder**

Create `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`:

```csharp
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Standards;

namespace RATools.Application.Publishing.PackageModel;

public sealed class EctdPackageModelBuilder(
    IApplicationRepository applicationRepository,
    IDocumentPlacementRepository placementRepository,
    IDocumentRepository documentRepository,
    IStandardsProfileProvider standardsProfileProvider) : IEctdPackageModelBuilder
{
    public async Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await applicationRepository.GetAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            throw new EctdPackageApplicationNotFoundException(request.ApplicationId);
        }

        var sequence = application.Sequences.SingleOrDefault(x => x.SequenceNumber == request.SequenceNumber);
        if (sequence is null)
        {
            throw new EctdPackageSequenceNotFoundException(request.ApplicationId, request.SequenceNumber);
        }

        var profile = standardsProfileProvider.GetProfile(application.EctdTemplateKey);
        var metadata = sequence.PublishingMetadata;
        var applicationMetadata = new EctdApplicationMetadata(
            application.ApplicationNumber,
            application.SponsorName,
            application.Region,
            application.EctdTemplateKey,
            metadata?.ApplicationType);
        var sequenceMetadata = new EctdSequenceMetadata(
            sequence.SequenceNumber,
            metadata?.SubmissionType ?? sequence.SubmissionType,
            metadata?.SubmissionSubtype,
            metadata?.SequenceDescription ?? sequence.Description,
            metadata?.ApplicantName ?? application.SponsorName,
            metadata?.FormType);

        await placementRepository.ListBySequenceAsync(request.ApplicationId, request.SequenceNumber, cancellationToken);
        await placementRepository.ListByApplicationAsync(request.ApplicationId, cancellationToken);
        await documentRepository.ListAsync(cancellationToken);

        return new EctdSequencePackage(
            application.Id,
            application.ApplicationNumber,
            sequence.SequenceNumber,
            profile.DisplayName,
            profile.IchEctdVersion,
            profile.UsRegionalModule1Version,
            applicationMetadata,
            sequenceMetadata,
            [],
            [],
            []);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests
```

Expected: PASS.

- [ ] **Step 5: Commit metadata builder**

Run:

```powershell
git add -f tests\RATools.Tests\Publishing\PackageModel\EctdPackageModelBuilderTests.cs
git add src\RATools.Application\Publishing\PackageModel
git commit -m "feat: build eCTD package metadata"
```

## Task 3: Build Leaves and Published File Inventory

**Files:**
- Modify: `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`
- Modify: `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`

- [ ] **Step 1: Add failing tests for leaves**

Add tests covering deterministic leaf fields, M1/M2-M5 split, title fallback, duplicate file inventory collapse, and missing documents.

```csharp
[Fact]
public async Task BuildAsync_CreatesDeterministicLeavesAndSplitsModules()
{
    var applicationId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    var oldTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var newTime = oldTime.AddMinutes(1);
    var application = CreateApplication(applicationId, "0001");
    var module1Document = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "cover.pdf", "C:/workspace/ANDA123456/0001/m1/us/cover.pdf", 10, "sha-cover");
    var module3Document = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "quality.pdf", "C:/workspace/ANDA123456/0001/m3/32-body-data/quality.pdf", 20, "sha-quality");
    var module3PlacementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    var module1PlacementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    var placements = new[]
    {
        DocumentPlacement.Rehydrate(module3PlacementId, module3Document.Id, applicationId, "0001", "m3.2", DocumentPlacementOperation.New, null, null, newTime),
        DocumentPlacement.Rehydrate(module1PlacementId, module1Document.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.New, "Cover Letter", null, oldTime)
    };
    var builder = CreateBuilder(application, placements, [module1Document, module3Document]);

    var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001"));

    var module1Leaf = Assert.Single(package.Module1Leaves);
    Assert.Equal(module1PlacementId, module1Leaf.PlacementId);
    Assert.Equal("leaf-bbbbbbbb000000000000000000000001", module1Leaf.LeafId);
    Assert.Equal("m1", module1Leaf.Module);
    Assert.Equal("new", module1Leaf.Operation);
    Assert.Equal("Cover Letter", module1Leaf.Title);
    Assert.Equal("m1/us/cover.pdf", module1Leaf.Href);
    Assert.Equal("cover.pdf", module1Leaf.FileName);
    Assert.Equal("application/pdf", module1Leaf.MediaType);
    Assert.Equal(module1Document.StoragePath, module1Leaf.SourcePath);
    Assert.Equal(10, module1Leaf.FileSize);
    Assert.Equal("sha-cover", module1Leaf.Sha256);
    Assert.Null(module1Leaf.Lifecycle);

    var module3Leaf = Assert.Single(package.IchBackboneLeaves);
    Assert.Equal(module3PlacementId, module3Leaf.PlacementId);
    Assert.Equal("m3", module3Leaf.Module);
    Assert.Equal("quality.pdf", module3Leaf.Title);
    Assert.Equal("m3/32-body-data/quality.pdf", module3Leaf.Href);
}

[Fact]
public async Task BuildAsync_CreatesPublishedFileInventoryWithoutDuplicates()
{
    var applicationId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    var application = CreateApplication(applicationId, "0001");
    var document = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "same.pdf", "C:/workspace/ANDA123456/0001/m1/us/same.pdf", 30, "sha-same");
    var placements = new[]
    {
        DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.New, "First", null, DateTime.UtcNow),
        DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "m1.2", DocumentPlacementOperation.New, "Second", null, DateTime.UtcNow.AddMinutes(1))
    };
    var builder = CreateBuilder(application, placements, [document]);

    var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001"));

    var file = Assert.Single(package.PublishedFiles);
    Assert.Equal(document.Id, file.DocumentId);
    Assert.Equal("m1/us/same.pdf", file.Href);
    Assert.Equal("same.pdf", file.FileName);
    Assert.Equal(30, file.FileSize);
    Assert.Equal("sha-same", file.Sha256);
}

[Fact]
public async Task BuildAsync_ThrowsWhenPlacementDocumentIsMissing()
{
    var applicationId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    var missingDocumentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
    var placementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004");
    var application = CreateApplication(applicationId, "0001");
    var placement = DocumentPlacement.Rehydrate(placementId, missingDocumentId, applicationId, "0001", "m1.1", DocumentPlacementOperation.New, "Missing", null, DateTime.UtcNow);
    var builder = CreateBuilder(application, [placement], []);

    var exception = await Assert.ThrowsAsync<EctdPackageDocumentNotFoundException>(
        () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

    Assert.Equal(applicationId, exception.ApplicationId);
    Assert.Equal("0001", exception.SequenceNumber);
    Assert.Equal(placementId, exception.PlacementId);
    Assert.Equal(missingDocumentId, exception.DocumentId);
}
```

Add helper methods:

```csharp
private static SubmissionApplication CreateApplication(Guid applicationId, params string[] sequenceNumbers)
{
    return SubmissionApplication.Rehydrate(
        applicationId,
        "ANDA123456",
        "US",
        "Acme Pharma",
        DateTime.UtcNow,
        sequenceNumbers.Select(x => SubmissionSequence.Rehydrate(x, "original-application", $"Sequence {x}", DateTime.UtcNow)).ToArray(),
        "C:/workspace/ANDA123456",
        "us-fda-ectd-322");
}

private static SubmissionDocument CreateDocument(Guid documentId, string fileName, string storagePath, long fileSize, string sha256)
{
    return SubmissionDocument.Rehydrate(documentId, fileName, "application/pdf", fileSize, storagePath: storagePath, sha256: sha256, createdUtc: DateTime.UtcNow);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests
```

Expected: FAIL because leaves and published file inventory are empty and missing documents are not checked.

- [ ] **Step 3: Implement leaf conversion**

Update `EctdPackageModelBuilder` to load placements/documents, map leaves, split modules, and build published files.

Key methods to add:

```csharp
private static IReadOnlyCollection<EctdLeaf> BuildLeaves(
    Guid applicationId,
    string sequenceNumber,
    IReadOnlyCollection<DocumentPlacement> placements,
    IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
{
    return placements
        .OrderBy(x => x.CtdSection, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.CreatedUtc)
        .ThenBy(x => x.Id)
        .Select(placement => BuildLeaf(applicationId, sequenceNumber, placement, documentById))
        .ToArray();
}

private static EctdLeaf BuildLeaf(
    Guid applicationId,
    string sequenceNumber,
    DocumentPlacement placement,
    IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
{
    if (!documentById.TryGetValue(placement.DocumentId, out var document))
    {
        throw new EctdPackageDocumentNotFoundException(applicationId, sequenceNumber, placement.Id, placement.DocumentId);
    }

    var module = ClassifyModule(applicationId, sequenceNumber, placement);
    return new EctdLeaf(
        placement.Id,
        placement.DocumentId,
        $"leaf-{placement.Id:N}",
        placement.SequenceNumber,
        placement.CtdSection,
        module,
        MapOperation(applicationId, sequenceNumber, placement),
        placement.Title ?? document.FileName,
        PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber),
        document.FileName,
        document.MediaType,
        document.StoragePath,
        document.FileSize,
        document.Sha256,
        null);
}

private static string ClassifyModule(Guid applicationId, string sequenceNumber, DocumentPlacement placement)
{
    var module = placement.CtdSection.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()?.ToLowerInvariant();
    return module is "m1" or "m2" or "m3" or "m4" or "m5"
        ? module
        : throw new EctdPackageInvalidSectionException(applicationId, sequenceNumber, placement.Id, placement.CtdSection);
}

private static string MapOperation(Guid applicationId, string sequenceNumber, DocumentPlacement placement)
{
    return placement.Operation switch
    {
        DocumentPlacementOperation.New => "new",
        DocumentPlacementOperation.Replace => "replace",
        DocumentPlacementOperation.Delete => "delete",
        DocumentPlacementOperation.Append => "append",
        _ => throw new EctdPackageUnsupportedOperationException(applicationId, sequenceNumber, placement.Id, (int)placement.Operation)
    };
}

private static IReadOnlyCollection<EctdPublishedFile> BuildPublishedFiles(IReadOnlyCollection<EctdLeaf> leaves)
{
    return leaves
        .GroupBy(x => x.DocumentId)
        .Select(x => x.First())
        .Select(x => new EctdPublishedFile(x.DocumentId, x.SourcePath, x.Href, x.FileName, x.FileSize, x.Sha256))
        .OrderBy(x => x.Href, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
```

In `BuildAsync`, replace the empty collections with:

```csharp
var placements = await placementRepository.ListBySequenceAsync(request.ApplicationId, request.SequenceNumber, cancellationToken);
await placementRepository.ListByApplicationAsync(request.ApplicationId, cancellationToken);
var documents = await documentRepository.ListAsync(cancellationToken);
var documentById = documents.ToDictionary(x => x.Id, x => x);
var leaves = BuildLeaves(request.ApplicationId, request.SequenceNumber, placements, documentById);
var module1Leaves = leaves.Where(x => x.Module == "m1").ToArray();
var ichBackboneLeaves = leaves.Where(x => x.Module is "m2" or "m3" or "m4" or "m5").ToArray();
var publishedFiles = BuildPublishedFiles(leaves);
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests
```

Expected: PASS.

- [ ] **Step 5: Commit leaf builder**

Run:

```powershell
git add -f tests\RATools.Tests\Publishing\PackageModel\EctdPackageModelBuilderTests.cs
git add src\RATools.Application\Publishing\PackageModel
git commit -m "feat: build eCTD package leaves"
```

## Task 4: Resolve Lifecycle References and Invalid Inputs

**Files:**
- Modify: `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`
- Modify: `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`

- [ ] **Step 1: Add failing lifecycle and invalid input tests**

Add tests for `replace` lifecycle href, missing target, same/future target sequence, cross-section target, invalid section, and unsupported enum values.

```csharp
[Fact]
public async Task BuildAsync_ResolvesLifecycleModifiedFileHrefForReplaceOperation()
{
    var applicationId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    var application = CreateApplication(applicationId, "0000", "0001");
    var historicalDocument = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"), "old.pdf", "C:/workspace/ANDA123456/0000/m1/us/old.pdf", 40, "sha-old");
    var currentDocument = CreateDocument(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006"), "new.pdf", "C:/workspace/ANDA123456/0001/m1/us/new.pdf", 50, "sha-new");
    var historicalPlacementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005");
    var historicalPlacement = DocumentPlacement.Rehydrate(historicalPlacementId, historicalDocument.Id, applicationId, "0000", "m1.1", DocumentPlacementOperation.New, "Old", null, DateTime.UtcNow);
    var currentPlacement = DocumentPlacement.Rehydrate(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000006"), currentDocument.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.Replace, "New", historicalPlacementId, DateTime.UtcNow.AddDays(1));
    var builder = CreateBuilder(application, [historicalPlacement, currentPlacement], [historicalDocument, currentDocument]);

    var package = await builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001"));

    var leaf = Assert.Single(package.Module1Leaves);
    Assert.Equal("replace", leaf.Operation);
    Assert.NotNull(leaf.Lifecycle);
    Assert.Equal(historicalPlacementId, leaf.Lifecycle.TargetPlacementId);
    Assert.Equal(historicalDocument.Id, leaf.Lifecycle.TargetDocumentId);
    Assert.Equal("0000", leaf.Lifecycle.TargetSequenceNumber);
    Assert.Equal("m1/us/old.pdf", leaf.Lifecycle.ModifiedFileHref);
}

[Fact]
public async Task BuildAsync_ThrowsWhenLifecycleTargetIsMissing()
{
    var applicationId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    var application = CreateApplication(applicationId, "0001");
    var document = CreateDocument(Guid.NewGuid(), "new.pdf", "C:/workspace/ANDA123456/0001/m1/us/new.pdf", 50, "sha-new");
    var placement = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.Replace, "New", null, DateTime.UtcNow);
    var builder = CreateBuilder(application, [placement], [document]);

    var exception = await Assert.ThrowsAsync<EctdPackageLifecycleTargetException>(
        () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

    Assert.Equal(placement.Id, exception.PlacementId);
    Assert.Null(exception.TargetPlacementId);
}

[Theory]
[InlineData("0001", "target sequence is not earlier than current sequence")]
[InlineData("0002", "target sequence is not earlier than current sequence")]
public async Task BuildAsync_ThrowsWhenLifecycleTargetSequenceIsNotEarlier(string targetSequenceNumber, string expectedReason)
{
    var applicationId = Guid.Parse("abababab-abab-abab-abab-abababababab");
    var application = CreateApplication(applicationId, "0001", "0002");
    var targetDocument = CreateDocument(Guid.NewGuid(), "old.pdf", $"C:/workspace/ANDA123456/{targetSequenceNumber}/m1/us/old.pdf", 40, "sha-old");
    var currentDocument = CreateDocument(Guid.NewGuid(), "new.pdf", "C:/workspace/ANDA123456/0001/m1/us/new.pdf", 50, "sha-new");
    var targetPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), targetDocument.Id, applicationId, targetSequenceNumber, "m1.1", DocumentPlacementOperation.New, "Old", null, DateTime.UtcNow);
    var currentPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), currentDocument.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.Replace, "New", targetPlacement.Id, DateTime.UtcNow.AddDays(1));
    var builder = CreateBuilder(application, [targetPlacement, currentPlacement], [targetDocument, currentDocument]);

    var exception = await Assert.ThrowsAsync<EctdPackageLifecycleTargetException>(
        () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

    Assert.Equal(expectedReason, exception.Reason);
}

[Fact]
public async Task BuildAsync_ThrowsWhenLifecycleTargetIsInDifferentSection()
{
    var applicationId = Guid.Parse("bcbcbcbc-bcbc-bcbc-bcbc-bcbcbcbcbcbc");
    var application = CreateApplication(applicationId, "0000", "0001");
    var targetDocument = CreateDocument(Guid.NewGuid(), "old.pdf", "C:/workspace/ANDA123456/0000/m1/us/old.pdf", 40, "sha-old");
    var currentDocument = CreateDocument(Guid.NewGuid(), "new.pdf", "C:/workspace/ANDA123456/0001/m1/us/new.pdf", 50, "sha-new");
    var targetPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), targetDocument.Id, applicationId, "0000", "m1.2", DocumentPlacementOperation.New, "Old", null, DateTime.UtcNow);
    var currentPlacement = DocumentPlacement.Rehydrate(Guid.NewGuid(), currentDocument.Id, applicationId, "0001", "m1.1", DocumentPlacementOperation.Replace, "New", targetPlacement.Id, DateTime.UtcNow.AddDays(1));
    var builder = CreateBuilder(application, [targetPlacement, currentPlacement], [targetDocument, currentDocument]);

    var exception = await Assert.ThrowsAsync<EctdPackageLifecycleTargetException>(
        () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

    Assert.Equal("target placement is in a different CTD section", exception.Reason);
}

[Fact]
public async Task BuildAsync_ThrowsWhenCtdSectionModuleIsUnsupported()
{
    var applicationId = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");
    var application = CreateApplication(applicationId, "0001");
    var document = CreateDocument(Guid.NewGuid(), "bad.pdf", "C:/workspace/ANDA123456/0001/x1/bad.pdf", 10, "sha-bad");
    var placement = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "x1.1", DocumentPlacementOperation.New, "Bad", null, DateTime.UtcNow);
    var builder = CreateBuilder(application, [placement], [document]);

    var exception = await Assert.ThrowsAsync<EctdPackageInvalidSectionException>(
        () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

    Assert.Equal("x1.1", exception.CtdSection);
}

[Fact]
public async Task BuildAsync_ThrowsWhenOperationValueIsUnsupported()
{
    var applicationId = Guid.Parse("dededede-dede-dede-dede-dededededede");
    var application = CreateApplication(applicationId, "0001");
    var document = CreateDocument(Guid.NewGuid(), "bad.pdf", "C:/workspace/ANDA123456/0001/m1/us/bad.pdf", 10, "sha-bad");
    var placement = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, applicationId, "0001", "m1.1", (DocumentPlacementOperation)999, "Bad", null, DateTime.UtcNow);
    var builder = CreateBuilder(application, [placement], [document]);

    var exception = await Assert.ThrowsAsync<EctdPackageUnsupportedOperationException>(
        () => builder.BuildAsync(new BuildEctdPackageRequest(applicationId, "0001")));

    Assert.Equal(999, exception.OperationValue);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests
```

Expected: FAIL because lifecycle references are null and invalid lifecycle targets are not validated.

- [ ] **Step 3: Implement lifecycle resolution**

Update leaf construction to accept all application placements:

```csharp
private static EctdLifecycleReference? BuildLifecycle(
    Guid applicationId,
    string sequenceNumber,
    DocumentPlacement placement,
    IReadOnlyDictionary<Guid, DocumentPlacement> placementById,
    IReadOnlyDictionary<Guid, SubmissionDocument> documentById)
{
    if (placement.Operation is DocumentPlacementOperation.New)
    {
        return null;
    }

    if (placement.Operation is not (DocumentPlacementOperation.Replace or DocumentPlacementOperation.Delete or DocumentPlacementOperation.Append))
    {
        return null;
    }

    if (placement.LifecycleTargetPlacementId is null)
    {
        throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, null, "target placement is missing");
    }

    if (!placementById.TryGetValue(placement.LifecycleTargetPlacementId.Value, out var targetPlacement))
    {
        throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target placement was not found");
    }

    if (targetPlacement.ApplicationId != applicationId)
    {
        throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target placement belongs to a different application");
    }

    if (!string.Equals(targetPlacement.CtdSection, placement.CtdSection, StringComparison.OrdinalIgnoreCase))
    {
        throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target placement is in a different CTD section");
    }

    if (CompareSequenceNumbers(targetPlacement.SequenceNumber, placement.SequenceNumber) >= 0)
    {
        throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target sequence is not earlier than current sequence");
    }

    if (!documentById.TryGetValue(targetPlacement.DocumentId, out var targetDocument))
    {
        throw new EctdPackageLifecycleTargetException(applicationId, sequenceNumber, placement.Id, placement.LifecycleTargetPlacementId, "target document was not found");
    }

    return new EctdLifecycleReference(
        targetPlacement.Id,
        targetPlacement.DocumentId,
        targetPlacement.SequenceNumber,
        PublishOutputNaming.BuildPublishedDocumentRelativePath(targetDocument, targetPlacement.SequenceNumber));
}

private static int CompareSequenceNumbers(string left, string right)
{
    if (int.TryParse(left, out var leftNumber) && int.TryParse(right, out var rightNumber))
    {
        return leftNumber.CompareTo(rightNumber);
    }

    return string.Compare(left, right, StringComparison.Ordinal);
}
```

Use it in `BuildLeaf`:

```csharp
var lifecycle = BuildLifecycle(applicationId, sequenceNumber, placement, placementById, documentById);
```

Pass `lifecycle` into the returned `EctdLeaf`.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests
```

Expected: PASS.

- [ ] **Step 5: Commit lifecycle builder**

Run:

```powershell
git add -f tests\RATools.Tests\Publishing\PackageModel\EctdPackageModelBuilderTests.cs
git add src\RATools.Application\Publishing\PackageModel
git commit -m "feat: resolve eCTD package lifecycle references"
```

## Task 5: Register Builder in Dependency Injection

**Files:**
- Modify: `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`
- Modify: `src/RATools.Application/DependencyInjection.cs`

- [ ] **Step 1: Add failing DI registration test**

Add this test:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RATools.Application;

[Fact]
public void AddApplication_RegistersPackageModelBuilder()
{
    var services = new ServiceCollection();

    services.AddApplication();

    var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IEctdPackageModelBuilder));
    Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    Assert.Equal(typeof(EctdPackageModelBuilder), descriptor.ImplementationType);
}
```

- [ ] **Step 2: Run tests to verify it fails**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests.AddApplication_RegistersPackageModelBuilder
```

Expected: FAIL because `IEctdPackageModelBuilder` is not registered.

- [ ] **Step 3: Register the builder**

Modify `src/RATools.Application/DependencyInjection.cs`:

```csharp
using RATools.Application.Publishing.PackageModel;
```

Add in `AddApplication()` near publishing services:

```csharp
services.AddScoped<IEctdPackageModelBuilder, EctdPackageModelBuilder>();
```

- [ ] **Step 4: Run focused DI test**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests.AddApplication_RegistersPackageModelBuilder
```

Expected: PASS.

- [ ] **Step 5: Commit DI registration**

Run:

```powershell
git add -f tests\RATools.Tests\Publishing\PackageModel\EctdPackageModelBuilderTests.cs
git add src\RATools.Application\DependencyInjection.cs
git commit -m "feat: register eCTD package model builder"
```

## Task 6: Full Verification

**Files:**
- Inspect all files changed in previous tasks.

- [ ] **Step 1: Run package model tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests
```

Expected: PASS.

- [ ] **Step 2: Run backend test suite**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj
```

Expected: PASS.

- [ ] **Step 3: Run frontend test suite**

Run from `frontend`:

```powershell
npm test
```

Expected: PASS. Existing React/AntD warnings may appear, but there must be zero failing tests.

- [ ] **Step 4: Review final diff**

Run:

```powershell
git status --short
git diff --stat HEAD
git diff HEAD -- src\RATools.Application\Publishing\PackageModel src\RATools.Application\DependencyInjection.cs tests\RATools.Tests\Publishing\PackageModel\EctdPackageModelBuilderTests.cs
```

Expected: only package model builder implementation, DI registration, and package model tests are changed after the last task commit.

- [ ] **Step 5: Final implementation commit if uncommitted changes remain**

If `git status --short` shows uncommitted implementation changes, run:

```powershell
git add src\RATools.Application\Publishing\PackageModel src\RATools.Application\DependencyInjection.cs
git add -f tests\RATools.Tests\Publishing\PackageModel\EctdPackageModelBuilderTests.cs
git commit -m "feat: add eCTD package model builder"
```

## Self-Review

- Spec coverage: tasks cover package records, standards profile metadata, sequence metadata fallback and overrides, deterministic leaf construction, M1/M2-M5 split, published file inventory, lifecycle target hrefs, package exceptions, DI registration, and backend/frontend verification.
- Scope: plan does not change `BackboneService`, XML generation, file writing, zip generation, or frontend behavior.
- Type consistency: file names, namespaces, constructor dependencies, and record names match the design and inspected repository APIs.
