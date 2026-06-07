# FDA eCTD 3.2.2 Standards Profile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a typed, test-covered standards profile inventory for FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3.

**Architecture:** Keep standards metadata in `RATools.Application` because publishing, validation, and reporting all need the same regulatory baseline. Introduce immutable DTO-style records and a singleton provider that resolves the existing `us-fda-ectd-3.2.2` template to a profile containing official standard versions, provenance URLs, and local DTD asset checksums. Do not change publish output behavior in this first batch.

**Tech Stack:** .NET 8, C# records, xUnit, existing dependency injection via `Microsoft.Extensions.DependencyInjection`.

---

## File Structure

- Create `src/RATools.Application/Standards/StandardsAsset.cs`
  - One asset in the standards inventory, with provenance, local path, and SHA-256 checksum.
- Create `src/RATools.Application/Standards/StandardsProfile.cs`
  - Profile-level metadata for the selected FDA/ICH baseline.
- Create `src/RATools.Application/Standards/IStandardsProfileProvider.cs`
  - Interface used by publishing and validation services later.
- Create `src/RATools.Application/Standards/FdaEctd322StandardsProfileProvider.cs`
  - Singleton provider for `us-fda-ectd-3.2.2`, initially exposing the two bundled DTD assets.
- Modify `src/RATools.Application/DependencyInjection.cs`
  - Register `IStandardsProfileProvider`.
- Create `tests/RATools.Tests/Standards/FdaEctd322StandardsProfileProviderTests.cs`
  - Unit tests for profile metadata, asset paths, checksum calculation, unsupported template behavior, and DI registration.

---

### Task 1: Standards Records

**Files:**
- Create: `src/RATools.Application/Standards/StandardsAsset.cs`
- Create: `src/RATools.Application/Standards/StandardsProfile.cs`

- [ ] **Step 1: Write the failing profile shape test**

Create `tests/RATools.Tests/Standards/FdaEctd322StandardsProfileProviderTests.cs` with this initial test:

```csharp
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Standards;

namespace RATools.Tests.Standards;

public sealed class FdaEctd322StandardsProfileProviderTests
{
    [Fact]
    public void GetProfile_ReturnsOfficialBaselineMetadata()
    {
        var provider = new FdaEctd322StandardsProfileProvider();

        var profile = provider.GetProfile(EctdTemplateRegistry.DefaultTemplateKey);

        Assert.Equal("us-fda-ectd-3.2.2", profile.TemplateKey);
        Assert.Equal("FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3", profile.DisplayName);
        Assert.Equal("FDA CDER/CBER", profile.RegulatoryAgency);
        Assert.Equal("United States", profile.Region);
        Assert.Equal("3.2.2", profile.IchEctdVersion);
        Assert.Equal("3.3", profile.UsRegionalModule1Version);
        Assert.Equal("1.9", profile.TechnicalConformanceGuideVersion);
        Assert.Equal("4.5", profile.ValidationCriteriaVersion);
        Assert.Contains(profile.OfficialReferences, x => x.Contains("fda.gov", StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter FullyQualifiedName~FdaEctd322StandardsProfileProviderTests.GetProfile_ReturnsOfficialBaselineMetadata
```

Expected: FAIL because `RATools.Application.Standards` and `FdaEctd322StandardsProfileProvider` do not exist.

- [ ] **Step 3: Add the immutable records**

Create `src/RATools.Application/Standards/StandardsAsset.cs`:

```csharp
namespace RATools.Application.Standards;

public sealed record StandardsAsset(
    string Key,
    string DisplayName,
    string Category,
    string Version,
    string LocalRelativePath,
    string SourceUrl,
    DateOnly? SupportedFrom,
    string Sha256);
```

Create `src/RATools.Application/Standards/StandardsProfile.cs`:

```csharp
namespace RATools.Application.Standards;

public sealed record StandardsProfile(
    string TemplateKey,
    string DisplayName,
    string RegulatoryAgency,
    string Region,
    string IchEctdVersion,
    string UsRegionalModule1Version,
    string TechnicalConformanceGuideVersion,
    string ValidationCriteriaVersion,
    IReadOnlyCollection<string> OfficialReferences,
    IReadOnlyCollection<StandardsAsset> Assets);
```

- [ ] **Step 4: Run the test again and verify the remaining failure**

Run:

```powershell
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter FullyQualifiedName~FdaEctd322StandardsProfileProviderTests.GetProfile_ReturnsOfficialBaselineMetadata
```

Expected: FAIL because the provider and interface do not exist yet.

- [ ] **Step 5: Commit**

```powershell
git add src/RATools.Application/Standards/StandardsAsset.cs src/RATools.Application/Standards/StandardsProfile.cs tests/RATools.Tests/Standards/FdaEctd322StandardsProfileProviderTests.cs
git commit -m "feat: add standards profile records"
```

---

### Task 2: FDA Standards Profile Provider

**Files:**
- Create: `src/RATools.Application/Standards/IStandardsProfileProvider.cs`
- Create: `src/RATools.Application/Standards/FdaEctd322StandardsProfileProvider.cs`
- Modify: `tests/RATools.Tests/Standards/FdaEctd322StandardsProfileProviderTests.cs`

- [ ] **Step 1: Add failing tests for asset inventory and unsupported templates**

Append these tests to `FdaEctd322StandardsProfileProviderTests`:

```csharp
[Fact]
public void GetProfile_IncludesBundledDtdAssetsWithChecksums()
{
    var provider = new FdaEctd322StandardsProfileProvider();

    var profile = provider.GetProfile(EctdTemplateRegistry.DefaultTemplateKey);

    var ichDtd = Assert.Single(profile.Assets, x => x.Key == "ich-ectd-3-2-dtd");
    Assert.Equal("ICH eCTD DTD", ichDtd.DisplayName);
    Assert.Equal("DTD", ichDtd.Category);
    Assert.Equal("3.2.2", ichDtd.Version);
    Assert.Equal("reference/dtd/ich-ectd-3-2.dtd", ichDtd.LocalRelativePath);
    Assert.StartsWith("https://", ichDtd.SourceUrl, StringComparison.Ordinal);
    Assert.Matches("^[a-f0-9]{64}$", ichDtd.Sha256);

    var regionalDtd = Assert.Single(profile.Assets, x => x.Key == "us-regional-v3-3-dtd");
    Assert.Equal("US Regional DTD", regionalDtd.DisplayName);
    Assert.Equal("3.3", regionalDtd.Version);
    Assert.Equal("reference/dtd/us-regional-v3-3.dtd", regionalDtd.LocalRelativePath);
    Assert.Matches("^[a-f0-9]{64}$", regionalDtd.Sha256);
}

[Fact]
public void GetProfile_ThrowsForUnsupportedTemplate()
{
    var provider = new FdaEctd322StandardsProfileProvider();

    var exception = Assert.Throws<StandardsProfileNotFoundException>(() => provider.GetProfile("eu-ectd-3.2.2"));

    Assert.Contains("Unsupported standards profile", exception.Message);
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter FullyQualifiedName~FdaEctd322StandardsProfileProviderTests
```

Expected: FAIL because `IStandardsProfileProvider`, `StandardsProfileNotFoundException`, and `FdaEctd322StandardsProfileProvider` do not exist.

- [ ] **Step 3: Add the provider interface**

Create `src/RATools.Application/Standards/IStandardsProfileProvider.cs`:

```csharp
namespace RATools.Application.Standards;

public sealed class StandardsProfileNotFoundException(string message) : Exception(message);

public interface IStandardsProfileProvider
{
    StandardsProfile GetProfile(string templateKey);
}
```

- [ ] **Step 4: Add the FDA provider**

Create `src/RATools.Application/Standards/FdaEctd322StandardsProfileProvider.cs`:

```csharp
using System.Security.Cryptography;
using RATools.Application.Applications.EctdTemplates;

namespace RATools.Application.Standards;

public sealed class FdaEctd322StandardsProfileProvider : IStandardsProfileProvider
{
    private const string StandardsPageUrl = "https://www.fda.gov/drugs/electronic-regulatory-submission-and-review/ectd-submission-standards-ectd-v322-and-regional-m1";
    private const string EctdOverviewUrl = "https://www.fda.gov/ectd";
    private const string IchSpecificationUrl = "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf";

    public StandardsProfile GetProfile(string templateKey)
    {
        if (!string.Equals(templateKey, EctdTemplateRegistry.DefaultTemplateKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new StandardsProfileNotFoundException($"Unsupported standards profile '{templateKey}'.");
        }

        return new StandardsProfile(
            EctdTemplateRegistry.DefaultTemplateKey,
            "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
            "FDA CDER/CBER",
            "United States",
            "3.2.2",
            "3.3",
            "1.9",
            "4.5",
            [StandardsPageUrl, EctdOverviewUrl, IchSpecificationUrl],
            [
                BuildAsset(
                    "ich-ectd-3-2-dtd",
                    "ICH eCTD DTD",
                    "DTD",
                    "3.2.2",
                    "reference/dtd/ich-ectd-3-2.dtd",
                    StandardsPageUrl,
                    new DateOnly(2008, 7, 16)),
                BuildAsset(
                    "us-regional-v3-3-dtd",
                    "US Regional DTD",
                    "DTD",
                    "3.3",
                    "reference/dtd/us-regional-v3-3.dtd",
                    StandardsPageUrl,
                    new DateOnly(2015, 12, 1))
            ]);
    }

    private static StandardsAsset BuildAsset(
        string key,
        string displayName,
        string category,
        string version,
        string localRelativePath,
        string sourceUrl,
        DateOnly? supportedFrom)
    {
        var path = ResolveLocalAssetPath(localRelativePath);
        var sha256 = File.Exists(path) ? ComputeSha256(path) : string.Empty;
        return new StandardsAsset(key, displayName, category, version, localRelativePath, sourceUrl, supportedFrom, sha256);
    }

    private static string ResolveLocalAssetPath(string localRelativePath)
        => Path.Combine(AppContext.BaseDirectory, localRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }
}
```

- [ ] **Step 5: Run provider tests and verify they pass**

Run:

```powershell
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter FullyQualifiedName~FdaEctd322StandardsProfileProviderTests
```

Expected: PASS. If the checksum assertions fail with an empty string, verify `Directory.Build.props` is copying `reference/dtd/*.dtd` into the test output.

- [ ] **Step 6: Commit**

```powershell
git add src/RATools.Application/Standards/IStandardsProfileProvider.cs src/RATools.Application/Standards/FdaEctd322StandardsProfileProvider.cs tests/RATools.Tests/Standards/FdaEctd322StandardsProfileProviderTests.cs
git commit -m "feat: add FDA eCTD standards profile provider"
```

---

### Task 3: Asset Existence Contract

**Files:**
- Modify: `tests/RATools.Tests/Standards/FdaEctd322StandardsProfileProviderTests.cs`
- Modify: `src/RATools.Application/Standards/FdaEctd322StandardsProfileProvider.cs`

- [ ] **Step 1: Add a failing test for missing local assets**

Append this test:

```csharp
[Fact]
public void GetProfile_FailsWhenBundledDtdAssetIsMissing()
{
    var provider = new FdaEctd322StandardsProfileProvider(assetRootPath: Path.Combine(Path.GetTempPath(), $"missing-assets-{Guid.NewGuid():N}"));

    var exception = Assert.Throws<StandardsAssetMissingException>(() => provider.GetProfile(EctdTemplateRegistry.DefaultTemplateKey));

    Assert.Contains("Bundled standards asset", exception.Message);
    Assert.Contains("ich-ectd-3-2.dtd", exception.Message);
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter FullyQualifiedName~FdaEctd322StandardsProfileProviderTests.GetProfile_FailsWhenBundledDtdAssetIsMissing
```

Expected: FAIL because the provider does not accept `assetRootPath` and `StandardsAssetMissingException` does not exist.

- [ ] **Step 3: Add the missing asset exception and injectable asset root**

Modify `src/RATools.Application/Standards/IStandardsProfileProvider.cs`:

```csharp
namespace RATools.Application.Standards;

public sealed class StandardsProfileNotFoundException(string message) : Exception(message);

public sealed class StandardsAssetMissingException(string message) : Exception(message);

public interface IStandardsProfileProvider
{
    StandardsProfile GetProfile(string templateKey);
}
```

Modify `FdaEctd322StandardsProfileProvider` constructor and asset path resolution:

```csharp
public sealed class FdaEctd322StandardsProfileProvider : IStandardsProfileProvider
{
    private const string StandardsPageUrl = "https://www.fda.gov/drugs/electronic-regulatory-submission-and-review/ectd-submission-standards-ectd-v322-and-regional-m1";
    private const string EctdOverviewUrl = "https://www.fda.gov/ectd";
    private const string IchSpecificationUrl = "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf";
    private readonly string _assetRootPath;

    public FdaEctd322StandardsProfileProvider(string? assetRootPath = null)
    {
        _assetRootPath = string.IsNullOrWhiteSpace(assetRootPath)
            ? AppContext.BaseDirectory
            : assetRootPath;
    }
```

Change `BuildAsset` from static to instance and replace the missing file behavior:

```csharp
    private StandardsAsset BuildAsset(
        string key,
        string displayName,
        string category,
        string version,
        string localRelativePath,
        string sourceUrl,
        DateOnly? supportedFrom)
    {
        var path = ResolveLocalAssetPath(localRelativePath);
        if (!File.Exists(path))
        {
            throw new StandardsAssetMissingException($"Bundled standards asset '{localRelativePath}' was not found at '{path}'.");
        }

        return new StandardsAsset(key, displayName, category, version, localRelativePath, sourceUrl, supportedFrom, ComputeSha256(path));
    }

    private string ResolveLocalAssetPath(string localRelativePath)
        => Path.Combine(_assetRootPath, localRelativePath.Replace('/', Path.DirectorySeparatorChar));
```

- [ ] **Step 4: Run all provider tests**

Run:

```powershell
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter FullyQualifiedName~FdaEctd322StandardsProfileProviderTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/RATools.Application/Standards/IStandardsProfileProvider.cs src/RATools.Application/Standards/FdaEctd322StandardsProfileProvider.cs tests/RATools.Tests/Standards/FdaEctd322StandardsProfileProviderTests.cs
git commit -m "test: enforce bundled standards asset availability"
```

---

### Task 4: Dependency Injection Registration

**Files:**
- Modify: `src/RATools.Application/DependencyInjection.cs`
- Modify: `tests/RATools.Tests/Standards/FdaEctd322StandardsProfileProviderTests.cs`

- [ ] **Step 1: Add a failing DI registration test**

Append this test:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RATools.Application;
```

If the file already has `using` statements, place these at the top with the existing imports. Then append:

```csharp
[Fact]
public void AddApplication_RegistersStandardsProfileProvider()
{
    var services = new ServiceCollection();

    services.AddApplication();
    using var provider = services.BuildServiceProvider();

    var standardsProvider = provider.GetRequiredService<IStandardsProfileProvider>();
    var profile = standardsProvider.GetProfile(EctdTemplateRegistry.DefaultTemplateKey);

    Assert.Equal("FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3", profile.DisplayName);
}
```

- [ ] **Step 2: Run the DI test and verify it fails**

Run:

```powershell
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter FullyQualifiedName~FdaEctd322StandardsProfileProviderTests.AddApplication_RegistersStandardsProfileProvider
```

Expected: FAIL because `IStandardsProfileProvider` is not registered.

- [ ] **Step 3: Register the provider**

Modify `src/RATools.Application/DependencyInjection.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Auditing;
using RATools.Application.Applications;
using RATools.Application.Documents;
using RATools.Application.EctdStructure;
using RATools.Application.Publishing;
using RATools.Application.Standards;
using RATools.Application.Validation;

namespace RATools.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IApplicationDeletionTransaction, PassthroughApplicationDeletionTransaction>();
        services.AddScoped<IApplicationDeletionCoordinator, ApplicationDeletionCoordinator>();
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IApplicationImportService, ApplicationImportService>();
        services.AddScoped<IApplicationPublishHistoryService, ApplicationPublishHistoryService>();
        services.AddSingleton<IEctdStructureService, EctdStructureService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentPlacementService, DocumentPlacementService>();
        services.AddScoped<IBackboneService, BackboneService>();
        services.AddSingleton<IStandardsProfileProvider, FdaEctd322StandardsProfileProvider>();
        services.AddSingleton<IEctdWorkspacePathResolver, EctdWorkspacePathResolver>();
        services.AddSingleton<PublishOutputVerifier>();
        services.AddScoped<IPublishJobService, PublishJobService>();
        services.AddScoped<ISequenceValidationService, SequenceValidationService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        return services;
    }
}
```

- [ ] **Step 4: Run the DI test and provider tests**

Run:

```powershell
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter FullyQualifiedName~FdaEctd322StandardsProfileProviderTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/RATools.Application/DependencyInjection.cs tests/RATools.Tests/Standards/FdaEctd322StandardsProfileProviderTests.cs
git commit -m "feat: register standards profile provider"
```

---

### Task 5: Full Verification

**Files:**
- No source file changes expected.

- [ ] **Step 1: Run backend tests**

Run:

```powershell
dotnet test tests/RATools.Tests/RATools.Tests.csproj
```

Expected: PASS.

- [ ] **Step 2: Run frontend tests**

Run:

```powershell
Set-Location frontend
npm test
```

Expected: PASS.

- [ ] **Step 3: Inspect final diff**

Run:

```powershell
git status --short
git log --oneline -5
```

Expected:

- no unstaged source changes
- recent commits include the four standards profile commits from this plan

- [ ] **Step 4: Record follow-up implementation boundary**

Create the next implementation plan for FDA publishing metadata and API after this standards profile batch is merged or accepted. That next plan should depend on `IStandardsProfileProvider` rather than duplicating baseline constants.
