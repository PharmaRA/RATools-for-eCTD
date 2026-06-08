# US Regional Admin Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add sequence-level US Regional admin metadata fields so real `us-regional.xml` publishing can receive applicant contact, telephone, and email values from persisted API data.

**Architecture:** Extend the existing `SequencePublishingMetadata` pipe end to end instead of creating a parallel metadata service. Keep new fields optional in API/domain persistence, and map nulls to empty strings in `EctdPackageModelBuilder` so `UsRegionalXmlWriter` remains the publish-time required-field gate.

**Tech Stack:** .NET 8, xUnit, ASP.NET Core API tests, EF Core, existing in-memory and EF repositories.

---

## File Structure

- Modify `src/RATools.Domain/Applications/SequencePublishingMetadata.cs`
  - Add normalized optional US regional admin fields.
- Modify `src/RATools.Application/Applications/Requests/UpdateSequencePublishingMetadataRequest.cs`
  - Add request fields.
- Modify `src/RATools.Application/Applications/Dtos/SequencePublishingMetadataDto.cs`
  - Add response fields.
- Modify `src/RATools.Application/Applications/SequencePublishingMetadataService.cs`
  - Map request/domain/DTO fields.
- Modify `src/RATools.Api/Contracts/UpdateSequencePublishingMetadataRequestBody.cs`
  - Add HTTP body properties.
- Modify `src/RATools.Api/Controllers/ApplicationsController.cs`
  - Pass fields into application request.
- Modify `src/RATools.Infrastructure/Persistence/EfCore/SequenceRecord.cs`
  - Add EF columns.
- Modify `src/RATools.Infrastructure/Persistence/EfCore/RAToolsDbContext.cs`
  - Configure lengths.
- Modify `src/RATools.Infrastructure/Persistence/EfCore/EfCoreApplicationRepository.cs`
  - Map fields both ways.
- Create `src/RATools.Infrastructure/Persistence/EfCore/Migrations/20260608234000_AddUsRegionalAdminMetadata.cs`
  - Add/drop nullable columns.
- Create `src/RATools.Infrastructure/Persistence/EfCore/Migrations/20260608234000_AddUsRegionalAdminMetadata.Designer.cs`
  - Update generated migration model.
- Modify `src/RATools.Infrastructure/Persistence/EfCore/Migrations/RAToolsDbContextModelSnapshot.cs`
  - Include new properties.
- Modify `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`
  - Populate `EctdUsRegionalMetadata` contact fields.
- Modify tests:
  - `tests/RATools.Tests/Applications/SequencePublishingMetadataServiceTests.cs`
  - `tests/RATools.Tests/Api/SequencePublishingMetadataApiTests.cs`
  - `tests/RATools.Tests/Persistence/EfCoreApplicationRepositoryTests.cs`
  - `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`

## Task 1: Application and API Metadata Contract

- [ ] **Step 1: Write failing service and API tests**

Update service and API round-trip tests to assert the new fields default to null and persist:

- `ApplicantContactName = "Jane Regulatory"`
- `ApplicantContactType = "regulatory"`
- `Telephone = "301-555-0100"`
- `TelephoneNumberType = "office"`
- `Email = "jane.regulatory@example.test"`

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RATools.Tests.Applications.SequencePublishingMetadataServiceTests|FullyQualifiedName~RATools.Tests.Api.SequencePublishingMetadataApiTests"
```

Expected: compile failure because DTO/request/domain fields do not exist.

- [ ] **Step 3: Implement domain, application, and API mapping**

Add fields to the domain record, request, DTO, API body, service mapping, and controller mapping.

- [ ] **Step 4: Run service and API tests**

Run the command from Step 2.

Expected: PASS.

## Task 2: EF Persistence

- [ ] **Step 1: Write failing EF repository test assertions**

Update `UpdateAsync_PersistsSequencePublishingMetadata` to assert the new fields reload from EF.

- [ ] **Step 2: Run EF test to verify failure**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Persistence.EfCoreApplicationRepositoryTests.UpdateAsync_PersistsSequencePublishingMetadata
```

Expected: compile failure or assertion failure until EF record/mapping is extended.

- [ ] **Step 3: Implement EF record, DbContext, repository mapping, migration, and snapshot**

Add nullable EF fields and map them in `ToRecord`, `UpdateAsync`, and `BuildPublishingMetadata`.

- [ ] **Step 4: Run EF test**

Run the command from Step 2.

Expected: PASS.

## Task 3: Package Model Mapping

- [ ] **Step 1: Write failing package model assertions**

Update `BuildAsync_UsesSequencePublishingMetadataWhenPresent` to set and assert the five US regional admin fields on `package.UsRegional`.

- [ ] **Step 2: Run package model test to verify failure**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests.BuildAsync_UsesSequencePublishingMetadataWhenPresent
```

Expected: FAIL because the package model builder still emits empty contact fields.

- [ ] **Step 3: Map fields in `EctdPackageModelBuilder`**

Use metadata fields for `ApplicantContactName`, `ApplicantContactType`, `Telephone`, `TelephoneNumberType`, and `Email`, with null mapped to `string.Empty`.

- [ ] **Step 4: Run focused publishing tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RATools.Tests.Publishing.PackageModel|FullyQualifiedName~RATools.Tests.Publishing.UsRegional|FullyQualifiedName~RATools.Tests.Publishing.BackboneServiceTests"
```

Expected: PASS.

## Task 4: Verification and Commit

- [ ] **Step 1: Run full backend tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj
```

Expected: PASS with only existing `NU1900` network warnings.

- [ ] **Step 2: Run diff check**

Run:

```powershell
git diff --check
```

Expected: exit code 0.

- [ ] **Step 3: Commit**

Commit message:

```powershell
git commit -m "feat: persist US regional admin metadata"
```
