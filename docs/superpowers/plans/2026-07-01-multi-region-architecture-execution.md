# Multi-Region Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a controlled multi-region eCTD architecture that preserves current FDA output while introducing EU template/profile/writer support.

**Architecture:** Keep ICH M2-M5 generation shared, move regional Module 1 generation behind `IRegionalBackboneWriter`, and route standards profiles through a composite provider. Parameterize XML DTD metadata from standards profile data, but use FDA regression tests to prove existing output remains byte-stable before EU behavior is enabled.

**Tech Stack:** .NET 8, xUnit, LINQ to XML, existing eCTD package model, existing FDA profile/writers, frontend npm quality gates.

---

## Scope Check

This plan implements roadmap Task 9 from `docs/superpowers/plans/2026-07-01-ratools-hardening-roadmap.md` using `docs/superpowers/specs/2026-06-18-multi-region-architecture-design.md` as the acceptance source.

Keep this task focused on architecture and a minimal second region. Do not implement a complete EU regulatory rule set, PDF regional policies, or a new runtime configuration system. EU support is intentionally skeletal but executable through readiness: template/profile metadata, regional writer selection, bundled DTD validation, and dry-run coverage.

## File Structure Map

- Modify: `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`
  - Add byte-stable FDA output tests for ICH namespace, DTD system id, checksum type, and path shape.
- Modify: `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs`
  - Add byte-stable FDA output tests for US regional namespace, DTD system id, relative path, and M1 href behavior.
- Create: `src/RATools.Application/Publishing/Regions/IRegionalBackboneWriter.cs`
  - Defines the regional writer boundary.
- Create: `src/RATools.Application/Publishing/Regions/IRegionalBackboneWriterRegistry.cs`
  - Defines the registry boundary.
- Create: `src/RATools.Application/Publishing/Regions/RegionalBackboneWriterRegistry.cs`
  - Resolves writers by region key.
- Create: `src/RATools.Application/Publishing/Regions/RegionalBackboneWriterNotFoundException.cs`
  - Reports unsupported regions.
- Create: `src/RATools.Application/Publishing/UsRegional/UsRegionalBackboneWriter.cs`
  - Wraps `IUsRegionalXmlWriter` as region key `us`.
- Modify: `src/RATools.Application/Publishing/BackboneService.cs`
  - Replace direct `IUsRegionalXmlWriter` usage with registry resolution.
- Modify: `src/RATools.Application/Validation/PublishReadinessService.cs`
  - Replace direct `IUsRegionalXmlWriter` usage with registry resolution.
- Modify: `src/RATools.Application/DependencyInjection.cs`
  - Register regional writer registry and US regional writer.
- Create: `tests/RATools.Tests/Publishing/Regions/RegionalBackboneWriterRegistryTests.cs`
  - Cover US resolution and unsupported region errors.
- Create: `src/RATools.Application/Standards/BackboneXmlProfile.cs`
  - Holds ICH/regional XML namespace, DTD version, document type, and DTD system id metadata.
- Modify: `src/RATools.Application/Standards/StandardsProfile.cs`
  - Add `BackboneXmlProfile BackboneXml` while preserving existing profile fields.
- Modify: `src/RATools.Application/Standards/FdaEctd322StandardsProfileProvider.cs`
  - Populate `BackboneXml` with current hard-coded FDA values.
- Modify: `src/RATools.Application/Publishing/Ich/IchIndexXmlWriter.cs`
  - Read namespace, DTD version, and system id from the package/profile metadata without changing FDA output.
- Modify: `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs`
  - Read namespace, DTD version, document type, and system id from the package/profile metadata without changing FDA output.
- Modify: `src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs`
  - Carry the selected `StandardsProfile` or `BackboneXmlProfile` in `EctdSequencePackage`.
- Modify: `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`
  - Attach the standards profile selected by application template key.
- Modify: `src/RATools.Application/Publishing/Validation/EctdXmlValidator.cs`
  - Drive allowed DTD names from bundled standards assets instead of a static two-file list.
- Create: `src/RATools.Application/Standards/CompositeStandardsProfileProvider.cs`
  - Routes `GetProfile(templateKey)` across multiple providers.
- Create: `src/RATools.Application/Standards/EuEctd322StandardsProfileProvider.cs`
  - Returns EU eCTD 3.2.2 metadata and DTD assets.
- Modify: `src/RATools.Application/Applications/EctdTemplates/EctdTemplateRegistry.cs`
  - Add `eu-ectd-3.2.2` template metadata.
- Create: `src/RATools.Application/Publishing/EuRegional/IEuRegionalXmlWriter.cs`
  - Defines minimal EU Module 1 writer contract.
- Create: `src/RATools.Application/Publishing/EuRegional/EuRegionalXmlWriter.cs`
  - Emits minimal EU regional backbone XML for Module 1 leaves.
- Create: `src/RATools.Application/Publishing/EuRegional/EuRegionalBackboneWriter.cs`
  - Wraps EU regional writer as region key `eu`.
- Add: `reference/dtd/eu-regional.dtd`
  - Minimal bundled DTD used by tests and readiness dry-run.
- Modify: project file or existing copy settings if DTD assets require explicit inclusion.
- Add tests under `tests/RATools.Tests/Standards`, `tests/RATools.Tests/Applications`, `tests/RATools.Tests/Publishing/EuRegional`, and `tests/RATools.Tests/Validation`.

## Task 1: Pin FDA XML Output Regressions

- [ ] **Step 1: Add failing/guarding ICH output test**

Add a test in `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs` that creates the existing minimal package fixture and asserts:

- result file name is `index.xml`;
- XML contains `<!DOCTYPE ectd:ectd SYSTEM "util/dtd/ich-ectd-3-2.dtd">`;
- root namespace remains `http://www.ich.org/ectd`;
- `dtd-version="3.2"` remains present;
- leaf checksum type remains `md5`.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~IchIndexXmlWriterTests"
```

Expected: pass now and remain green through later refactors.

- [ ] **Step 2: Add guarding US regional output test**

Add a test in `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs` that creates the existing minimal package fixture and asserts:

- result relative path is `m1/us/us-regional.xml`;
- XML contains `<!DOCTYPE fda-regional:fda-regional SYSTEM "../../util/dtd/us-regional-v3-3.dtd">`;
- root namespace remains `http://www.ich.org/fda`;
- `dtd-version="3.3"` remains present;
- Module 1 hrefs remain relative to `m1/us/`.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~UsRegionalXmlWriterTests"
```

Expected: pass now and remain green through later refactors.

## Task 2: Introduce Regional Writer Registry With US Only

- [ ] **Step 1: Write failing registry tests**

Create `tests/RATools.Tests/Publishing/Regions/RegionalBackboneWriterRegistryTests.cs`.

Cover:

- resolving `US` returns a writer whose `RegionKey` is `us`;
- resolving `us` is case-insensitive;
- resolving `EU` before registration throws `RegionalBackboneWriterNotFoundException`.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RegionalBackboneWriterRegistryTests"
```

Expected: fail because the registry types do not exist.

- [ ] **Step 2: Add registry contracts and US adapter**

Create the regional writer files under `src/RATools.Application/Publishing/Regions/` and `src/RATools.Application/Publishing/UsRegional/`.

`IRegionalBackboneWriter.WriteRegionalBackbones(EctdSequencePackage package)` must return `IReadOnlyList<BackboneGeneratedFile>`.

`UsRegionalBackboneWriter` should depend on `IUsRegionalXmlWriter`, call `Write(package)`, and return one `BackboneGeneratedFile` using `result.RelativePath` and `result.XmlContent`.

- [ ] **Step 3: Update services to resolve regional writer**

Modify `BackboneService` and `PublishReadinessService` constructors to depend on `IRegionalBackboneWriterRegistry` instead of `IUsRegionalXmlWriter`.

Resolve with `package.ApplicationMetadata.Region` and validate each returned regional backbone file.

Keep the existing exception mappings for `UsRegionalXmlMetadataException` and `UsRegionalXmlSectionMappingException`.

- [ ] **Step 4: Register DI**

In `src/RATools.Application/DependencyInjection.cs`, register:

```csharp
services.AddSingleton<IRegionalBackboneWriter, UsRegionalBackboneWriter>();
services.AddSingleton<IRegionalBackboneWriterRegistry, RegionalBackboneWriterRegistry>();
```

Keep `IUsRegionalXmlWriter` registered because the US adapter wraps it.

- [ ] **Step 5: Verify US-only registry slice**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RegionalBackboneWriterRegistryTests|FullyQualifiedName~BackboneServiceTests|FullyQualifiedName~PublishReadinessServiceTests|FullyQualifiedName~UsRegionalXmlWriterTests"
```

Expected: pass with FDA behavior unchanged.

## Task 3: Parameterize XML DTD and Namespace Selection

- [ ] **Step 1: Write profile metadata tests**

Add tests asserting FDA `StandardsProfile.BackboneXml` contains the current values:

- ICH root name `ectd:ectd`;
- ICH namespace `http://www.ich.org/ectd`;
- ICH DTD version `3.2`;
- ICH DTD system id `util/dtd/ich-ectd-3-2.dtd`;
- US regional root name `fda-regional:fda-regional`;
- US regional namespace `http://www.ich.org/fda`;
- US regional DTD version `3.3`;
- US regional DTD system id `../../util/dtd/us-regional-v3-3.dtd`.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~FdaEctd322StandardsProfileProviderTests"
```

Expected: fail until `BackboneXmlProfile` is added.

- [ ] **Step 2: Add backbone XML profile records**

Create `BackboneXmlProfile`, `IchBackboneXmlProfile`, and `RegionalBackboneXmlProfile` records in `src/RATools.Application/Standards/BackboneXmlProfile.cs`.

Append `BackboneXmlProfile BackboneXml` to `StandardsProfile`.

Update all `new StandardsProfile(...)` calls in tests and production.

- [ ] **Step 3: Attach profile to package model**

Add `StandardsProfile StandardsProfile` or `BackboneXmlProfile BackboneXml` to `EctdSequencePackage`.

In `EctdPackageModelBuilder`, call `IStandardsProfileProvider.GetProfile(application.EctdTemplateKey)` and attach the result.

- [ ] **Step 4: Read XML profile in writers**

Update `IchIndexXmlWriter` and `UsRegionalXmlWriter` to read namespace, DTD version, document type name, and system id from `package.StandardsProfile.BackboneXml`.

Keep fallback-free behavior: if profile data is missing, throw the writer exception with a clear message instead of silently using hard-coded defaults.

- [ ] **Step 5: Parameterize DTD validator allow list**

Modify `EctdXmlValidator.Validate` to accept an optional `StandardsProfile` or bundled DTD asset list. Use `StandardsProfile.Assets` entries where `Category == "DTD"` to allow DTD file names.

Update call sites in `BackboneService` and `PublishReadinessService` to pass the selected package profile.

Keep a compatibility overload `Validate(BackboneGeneratedFile file)` that uses the current FDA DTD file list for existing tests that do not build a package profile.

- [ ] **Step 6: Verify FDA output remains unchanged**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~IchIndexXmlWriterTests|FullyQualifiedName~UsRegionalXmlWriterTests|FullyQualifiedName~EctdXmlValidatorTests|FullyQualifiedName~BackboneServiceTests|FullyQualifiedName~PublishReadinessServiceTests"
```

Expected: pass; FDA output tests from Task 1 remain green.

## Task 4: Introduce Composite Standards Profile Provider

- [ ] **Step 1: Write failing composite tests**

Create `tests/RATools.Tests/Standards/CompositeStandardsProfileProviderTests.cs`.

Cover:

- FDA key resolves through the FDA provider;
- unknown key throws `StandardsProfileNotFoundException`;
- duplicate providers for the same key throw a deterministic exception or fail fast during resolution.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~CompositeStandardsProfileProviderTests"
```

Expected: fail because composite provider does not exist.

- [ ] **Step 2: Implement composite provider**

Create `CompositeStandardsProfileProvider` that receives `IEnumerable<IStandardsProfileProvider>` internal providers, tries each provider, and returns the first matching profile.

Avoid self-injection loops in DI by registering concrete providers separately, for example:

```csharp
services.AddSingleton<FdaEctd322StandardsProfileProvider>();
services.AddSingleton<IStandardsProfileProvider>(sp =>
    new CompositeStandardsProfileProvider([
        sp.GetRequiredService<FdaEctd322StandardsProfileProvider>()
    ]));
```

- [ ] **Step 3: Verify FDA provider behavior is preserved**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~FdaEctd322StandardsProfileProviderTests|FullyQualifiedName~CompositeStandardsProfileProviderTests"
```

Expected: pass; FDA provider still rejects EU keys directly.

## Task 5: Add EU Template and Standards Profile Metadata

- [ ] **Step 1: Write failing EU metadata tests**

Add tests for:

- `EctdTemplateRegistry.Resolve("eu-ectd-3.2.2")` returns region `EU`;
- composite standards provider resolves `eu-ectd-3.2.2`;
- FDA provider alone still rejects `eu-ectd-3.2.2`.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~EctdTemplateRegistryTests|FullyQualifiedName~CompositeStandardsProfileProviderTests|FullyQualifiedName~FdaEctd322StandardsProfileProviderTests"
```

Expected: fail until EU template/provider exists.

- [ ] **Step 2: Add EU template and provider**

Update `EctdTemplateRegistry.All` to include:

```csharp
new EctdTemplateDefinition(
    "eu-ectd-3.2.2",
    "EU eCTD 3.2.2",
    "EU",
    "eCTD",
    "3.2.2",
    "eu-ectd-3.2.2",
    "EU M1")
```

Create `EuEctd322StandardsProfileProvider` with EU display metadata, ICH profile metadata, EU regional XML metadata, and DTD assets including `reference/dtd/eu-regional.dtd`.

- [ ] **Step 3: Register EU provider in composite**

Register `EuEctd322StandardsProfileProvider` as a concrete singleton and include it in `CompositeStandardsProfileProvider`.

- [ ] **Step 4: Verify metadata slice**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~EctdTemplateRegistryTests|FullyQualifiedName~CompositeStandardsProfileProviderTests|FullyQualifiedName~FdaEctd322StandardsProfileProviderTests"
```

Expected: pass.

## Task 6: Add EU Regional Writer and DTD Asset

- [ ] **Step 1: Write failing EU writer tests**

Create `tests/RATools.Tests/Publishing/EuRegional/EuRegionalXmlWriterTests.cs`.

Cover:

- EU writer returns `eu-regional.xml` at `m1/eu/eu-regional.xml`;
- XML uses the EU regional namespace and DTD system id from the EU profile;
- an empty Module 1 package still produces a valid regional root;
- a simple `m1.0` or supported minimal Module 1 leaf is emitted with md5 checksum and relative href.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~EuRegionalXmlWriterTests"
```

Expected: fail because EU writer does not exist.

- [ ] **Step 2: Add EU DTD asset**

Add `reference/dtd/eu-regional.dtd`.

For this implementation, use a minimal DTD matching the XML emitted by `EuRegionalXmlWriter` and sufficient for tests. Add a comment at the top noting it is the bundled test/architecture DTD placeholder for the controlled EU skeleton, not a full official EU validation rule set.

- [ ] **Step 3: Implement EU writer and regional adapter**

Create `IEuRegionalXmlWriter`, `EuRegionalXmlWriter`, and `EuRegionalBackboneWriter`.

Keep the EU writer intentionally narrow:

- accept Module 1 leaves only;
- emit a deterministic root and optional leaf list;
- use `package.StandardsProfile.BackboneXml.Regional`;
- throw a region-specific writer exception if the package profile is not EU.

- [ ] **Step 4: Register EU writer**

In `DependencyInjection`, register `IEuRegionalXmlWriter`, `EuRegionalXmlWriter`, and `IRegionalBackboneWriter` for `EuRegionalBackboneWriter`.

- [ ] **Step 5: Verify EU writer slice**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~EuRegionalXmlWriterTests|FullyQualifiedName~RegionalBackboneWriterRegistryTests|FullyQualifiedName~EctdXmlValidatorTests"
```

Expected: pass.

## Task 7: Add EU Readiness Dry-Run Tests

- [ ] **Step 1: Write failing EU readiness test**

Add a test in `tests/RATools.Tests/Validation/PublishReadinessServiceTests.cs` that creates an application with template key `eu-ectd-3.2.2`, region `EU`, valid minimal sequence metadata, and no unsupported placements.

Assert readiness can build and validate ICH plus EU regional backbones without returning US-specific metadata errors.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PublishReadinessServiceTests"
```

Expected: fail until EU registry/profile/writer integration is complete.

- [ ] **Step 2: Add package-builder and readiness integration fixes**

If the package builder still assumes US-only regional metadata for all templates, branch by `application.EctdTemplateKey` or `application.Region` so EU packages can be built without requiring US regional admin metadata.

Do not remove US metadata requirements from the US writer.

- [ ] **Step 3: Verify readiness slice**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PublishReadinessServiceTests|FullyQualifiedName~BackboneServiceTests|FullyQualifiedName~EuRegionalXmlWriterTests"
```

Expected: pass; US readiness behavior remains unchanged.

## Task 8: Full Verification and Commit

- [ ] **Step 1: Run FDA-focused regression tests**

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~IchIndexXmlWriterTests|FullyQualifiedName~UsRegionalXmlWriterTests|FullyQualifiedName~FdaEctd322StandardsProfileProviderTests"
```

Expected: pass.

- [ ] **Step 2: Run full backend tests**

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release
```

Expected: pass.

- [ ] **Step 3: Run frontend gates**

```powershell
npm run lint
npm run build
npm test
```

Run frontend commands from `frontend/`.

Expected: pass with only previously-known warnings.

- [ ] **Step 4: Commit**

```powershell
git add src tests reference frontend
git add -f docs\superpowers\plans\2026-07-01-multi-region-architecture-execution.md
git commit -m "feat: add multi-region eCTD architecture"
```

Expected: commit preserves FDA behavior and adds EU as a controlled second region.

## Self-Review Notes

- Spec coverage: regional writer registry, profile composition, EU template/profile metadata, DTD-driven validation, EU writer skeleton, FDA regression coverage, and readiness dry-run are represented.
- Placeholder scan: the only intentional placeholder is the EU DTD asset caveat, explicitly scoped as a controlled architecture skeleton rather than full EU regulatory validation.
- Type consistency: regional writer, standards provider, and XML profile names are used consistently across tasks.
