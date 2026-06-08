# eCTD DTD Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local DTD validation for generated `index.xml` and `m1/us/us-regional.xml` before the real eCTD delivery package is written.

**Architecture:** Introduce an application-layer `IEctdXmlValidator` with a locked-down local DTD resolver. `BackboneService` calls the validator for both generated XML files before `IBackboneFileWriter.SaveAsync`, so non-DTD-compliant XML fails the publish job before files are written.

**Tech Stack:** .NET 8, `System.Xml`, xUnit, existing package writer abstractions and bundled DTD assets.

---

## File Structure

- Create `src/RATools.Application/Publishing/Validation/IEctdXmlValidator.cs`
  - Validator contract.
- Create `src/RATools.Application/Publishing/Validation/EctdXmlValidationException.cs`
  - Exception with `RelativePath` and validation message.
- Create `src/RATools.Application/Publishing/Validation/EctdXmlValidator.cs`
  - DTD validation implementation and local DTD resolver.
- Modify `src/RATools.Application/Publishing/BackboneService.cs`
  - Validate generated XML files before file writing.
- Modify `src/RATools.Application/DependencyInjection.cs`
  - Register `IEctdXmlValidator`.
- Create `tests/RATools.Tests/Publishing/Validation/EctdXmlValidatorTests.cs`
  - Unit tests for valid ICH, valid US regional, invalid XML, and unknown DTD blocking.
- Modify `tests/RATools.Tests/Publishing/BackboneServiceTests.cs`
  - Add validator fake and verify validation occurs before writing.

## Task 1: DTD Validator Contract and Implementation

- [ ] **Step 1: Write failing validator tests**

Create `EctdXmlValidatorTests` with:

- `Validate_PassesForWriterGeneratedIchIndexXml`
- `Validate_PassesForWriterGeneratedUsRegionalXml`
- `Validate_ThrowsForDtdValidationError`
- `Validate_ThrowsForUnknownDtdSystemId`

- [ ] **Step 2: Run validator tests to verify failure**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Validation.EctdXmlValidatorTests
```

Expected: compile failure because validator types do not exist.

- [ ] **Step 3: Implement validator**

Implement:

- `IEctdXmlValidator.Validate(BackboneGeneratedFile file)`
- `EctdXmlValidationException`
- `EctdXmlValidator`
- private resolver that only permits `ich-ectd-3-2.dtd` and `us-regional-v3-3.dtd`

- [ ] **Step 4: Run validator tests**

Run the command from Step 2.

Expected: PASS.

## Task 2: Publish Orchestration Integration

- [ ] **Step 1: Write failing BackboneService validation test**

Update `BackboneServiceTests` so the service constructor receives a recording validator. Assert the validator sees `index.xml` and `m1/us/us-regional.xml` before the file writer is invoked.

- [ ] **Step 2: Run BackboneService test to verify failure**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.BackboneServiceTests
```

Expected: compile failure or assertion failure because `BackboneService` does not yet depend on the validator.

- [ ] **Step 3: Inject and call validator**

Update `BackboneService` constructor to accept `IEctdXmlValidator`, build generated files, validate each file, then call the file writer.

- [ ] **Step 4: Register validator**

Register `IEctdXmlValidator` in `AddApplication()`.

- [ ] **Step 5: Run publishing tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RATools.Tests.Publishing.Validation|FullyQualifiedName~RATools.Tests.Publishing.BackboneServiceTests|FullyQualifiedName~RATools.Tests.Publishing.Ich|FullyQualifiedName~RATools.Tests.Publishing.UsRegional"
```

Expected: PASS.

## Task 3: Verification and Commit

- [ ] **Step 1: Run full backend tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj
```

Expected: PASS with only existing NuGet vulnerability feed network warnings.

- [ ] **Step 2: Run diff check**

Run:

```powershell
git diff --check
```

Expected: exit code 0.

- [ ] **Step 3: Commit**

Commit message:

```powershell
git commit -m "feat: validate eCTD XML against bundled DTDs"
```
