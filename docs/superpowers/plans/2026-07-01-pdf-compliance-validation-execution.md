# PDF Compliance Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add PDF technical compliance findings to publish readiness through the existing eCTD validation rule engine.

**Architecture:** Define PDF inspection records and `IPdfInspector` in the application layer, implement a PdfPig-backed infrastructure adapter, and translate inspection results into `IEctdValidationRule` findings. Keep concrete PDF library usage isolated in infrastructure and keep `PublishReadinessService` unchanged except through rule registration.

**Tech Stack:** .NET 8, xUnit, PdfPig 0.1.15, existing eCTD validation rule engine.

---

## Scope Check

This plan implements roadmap Task 8 using `docs/superpowers/specs/2026-06-18-pdf-compliance-validation-design.md` as acceptance source.

Do not add bespoke PDF checks directly to `PublishReadinessService`. PDF readiness must flow through `IEctdValidationRule`, `EctdValidationEngine`, and `FdaEctdRuleSetProvider`. First pass focuses on deterministic technical findings from inspection results; deep real-world PDF fixture expansion can follow after the rule path is proven.

## File Structure Map

- Modify: `src/RATools.Infrastructure/RATools.Infrastructure.csproj`
  - Add `PdfPig` package reference pinned to `0.1.15`.
- Create: `src/RATools.Application/Publishing/Validation/Pdf/IPdfInspector.cs`
  - Defines `IPdfInspector`, `PdfInspectionResult`, `PdfLinkReference`, and `PdfLinkKind`.
- Create: `src/RATools.Infrastructure/Publishing/Validation/Pdf/PdfPigPdfInspector.cs`
  - Implements best-effort PDF inspection using PdfPig and converts parse failures into diagnostic inspection results.
- Create: `src/RATools.Application/Validation/Rules/Pdf/PdfComplianceRule.cs`
  - Emits PDF version, encrypted/security, searchable text, font embedding, bookmark, parse failure, and link findings.
- Modify: `src/RATools.Application/DependencyInjection.cs`
  - Registers `PdfComplianceRule` as `IEctdValidationRule`.
- Modify: `src/RATools.Infrastructure/DependencyInjection.cs`
  - Registers `IPdfInspector` as `PdfPigPdfInspector`.
- Create: `tests/RATools.Tests/Validation/Rules/Pdf/PdfComplianceRuleTests.cs`
  - Tests rule findings with a fake inspector.
- Create: `tests/RATools.Tests/Publishing/Validation/Pdf/PdfPigPdfInspectorTests.cs`
  - Tests readable text extraction and parse failure handling with tiny generated fixture files.
- Modify: `tests/RATools.Tests/Validation/PublishReadinessServiceTests.cs`
  - Adds a readiness integration assertion that PDF rule findings appear in readiness output.

## Task 1: Define PDF Inspection Port

- [ ] **Step 1: Create failing rule tests**

Create `tests/RATools.Tests/Validation/Rules/Pdf/PdfComplianceRuleTests.cs` using a fake `IPdfInspector`. Cover:

- encrypted PDFs emit `PDF_ENCRYPTED` high severity;
- no searchable text emits `PDF_NO_SEARCHABLE_TEXT` high severity;
- non-embedded fonts emit `PDF_FONT_NOT_EMBEDDED` high severity;
- missing inter-document target emits `PDF_BROKEN_INTER_LINK` high severity;
- compliant PDF emits no findings.

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PdfComplianceRuleTests"
```

Expected: fail because PDF inspection abstractions and rule do not exist.

- [ ] **Step 2: Add `IPdfInspector` records**

Create `src/RATools.Application/Publishing/Validation/Pdf/IPdfInspector.cs` with:

```csharp
public interface IPdfInspector
{
    PdfInspectionResult Inspect(Stream pdfStream, string relativeHref);
}

public sealed record PdfInspectionResult(
    string? PdfVersion,
    bool IsEncrypted,
    bool HasSecurityRestrictions,
    bool HasSearchableText,
    bool AllFontsEmbedded,
    IReadOnlyList<string> NonEmbeddedFonts,
    bool HasBookmarks,
    IReadOnlyList<PdfLinkReference> Links,
    string? ParseError = null);
```

## Task 2: Implement PDF Compliance Rule

- [ ] **Step 1: Implement `PdfComplianceRule`**

Create `src/RATools.Application/Validation/Rules/Pdf/PdfComplianceRule.cs`. It should inspect all package leaves where `MediaType == "application/pdf"` or `FileName` ends with `.pdf`, open `leaf.SourcePath`, and emit findings with category `PdfCompliance`.

Required codes:

- `PDF_PARSE_FAILED`
- `PDF_VERSION_UNSUPPORTED`
- `PDF_ENCRYPTED`
- `PDF_SECURITY_RESTRICTED`
- `PDF_NO_SEARCHABLE_TEXT`
- `PDF_FONT_NOT_EMBEDDED`
- `PDF_NO_BOOKMARKS`
- `PDF_BROKEN_INTER_LINK`

- [ ] **Step 2: Verify rule tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PdfComplianceRuleTests"
```

Expected: rule tests pass.

## Task 3: Implement PdfPig Adapter

- [ ] **Step 1: Add PdfPig package**

Run:

```powershell
dotnet add src\RATools.Infrastructure\RATools.Infrastructure.csproj package PdfPig --version 0.1.15
```

Expected: `src/RATools.Infrastructure/RATools.Infrastructure.csproj` contains a pinned `PdfPig` package reference.

- [ ] **Step 2: Write adapter tests**

Create `tests/RATools.Tests/Publishing/Validation/Pdf/PdfPigPdfInspectorTests.cs`. Use a tiny valid PDF string fixture to assert `HasSearchableText`, and an invalid byte fixture to assert `ParseError` is populated rather than throwing.

- [ ] **Step 3: Implement adapter**

Create `src/RATools.Infrastructure/Publishing/Validation/Pdf/PdfPigPdfInspector.cs`. Use PdfPig to open the stream, collect page text, basic version/encryption/bookmark/link/font information where the library exposes it, and return a best-effort result. Catch parser exceptions and return `ParseError`.

## Task 4: Register and Integrate Readiness

- [ ] **Step 1: Register services**

Register `PdfComplianceRule` as an `IEctdValidationRule` in application DI and `PdfPigPdfInspector` as `IPdfInspector` in infrastructure DI.

- [ ] **Step 2: Add readiness integration test**

In `PublishReadinessServiceTests`, add a service configured with a fake PDF inspector and assert a problematic PDF produces a readiness finding with category `PdfCompliance`.

## Task 5: Verification and Commit

- [ ] **Step 1: Run targeted tests**

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PdfComplianceRuleTests|FullyQualifiedName~PdfPigPdfInspectorTests|FullyQualifiedName~PublishReadinessServiceTests"
```

- [ ] **Step 2: Run full backend tests**

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release
```

- [ ] **Step 3: Commit**

```powershell
git add src tests
git add -f docs\superpowers\plans\2026-07-01-pdf-compliance-validation-execution.md
git commit -m "feat: add PDF compliance readiness rules"
```

## Self-Review Notes

- Spec coverage: includes inspection abstraction, infrastructure adapter, rule-engine integration, readiness output, version lock, and tests.
- Placeholder scan: no TBD/TODO placeholders.
- Type consistency: `IPdfInspector`, `PdfInspectionResult`, and `PdfComplianceRule` names are consistent across tasks.
