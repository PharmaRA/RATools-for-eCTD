# RATools Hardening Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the agreed improvement order into independently shippable work: finish the validation rule engine, green frontend gates, split large modules, then add PDF compliance and multi-region support.

**Architecture:** This is a master plan, not a single mega-feature branch. Each phase is a release-sized slice with its own tests and stop gate. Existing detailed specs remain authoritative for their phase-level design; this plan defines the execution order and the concrete first-pass tasks needed to move from the current worktree to green, maintainable follow-on branches.

**Tech Stack:** .NET 8, xUnit, EF Core/PostgreSQL, React 19, TypeScript, Vite, Vitest, ESLint, Ant Design, lucide-react.

---

## Scope Check

This roadmap spans multiple independent subsystems. Do not implement it as one branch. Execute in this order:

1. Validation rule engine closure.
2. Frontend gate recovery.
3. Sequence workspace refactor.
4. Publish job service decomposition.
5. PDF compliance validation.
6. Multi-region architecture.

Each phase should end with a green gate and a commit. Later phases must not start until the prior phase's stop gate is met.

## File Structure Map

### Phase 1: Validation Rule Engine Closure

- Modify: `src/RATools.Application/Validation/Rules/EctdValidationRule.cs`
- Modify: `src/RATools.Application/Validation/Rules/EctdValidationEngine.cs`
- Modify: `src/RATools.Application/Validation/Rules/FdaEctdRuleSetProvider.cs`
- Modify: `src/RATools.Application/Validation/Rules/FileNamingConventionRule.cs`
- Modify: `src/RATools.Application/Validation/PublishReadinessService.cs`
- Modify: `src/RATools.Application/DependencyInjection.cs`
- Create: `tests/RATools.Tests/Validation/Rules/EctdValidationEngineTests.cs`
- Create: `tests/RATools.Tests/Validation/Rules/FdaEctdRuleSetProviderTests.cs`
- Create: `tests/RATools.Tests/Validation/Rules/FileNamingConventionRuleTests.cs`
- Modify: `tests/RATools.Tests/Validation/PublishReadinessServiceTests.cs`

### Phase 2: Frontend Gate Recovery

- Modify: `frontend/src/PathPickerFormHosts.test.tsx`
- Modify: `frontend/src/components/publishing/ArtifactsPanel.tsx`
- Modify: `frontend/src/components/publishing/PackageReviewPanel.tsx`
- Modify: `frontend/src/components/publishing/PublishHistoryTab.tsx`
- Modify: `frontend/src/components/publishing/ReportPanel.tsx`
- Modify: `frontend/src/pages/ApplicationsPage.tsx`
- Modify: `frontend/src/pages/ApplicationDetailsPage.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.tsx`
- Modify: `frontend/src/pages/appShared.ts`
- Modify: `frontend/src/PublishHistoryDetail.test.tsx`

### Phase 3A: Sequence Workspace Refactor

- Follow design: `docs/superpowers/specs/2026-06-18-sequence-workspace-refactor-design.md`
- Create: `frontend/src/prePublishChecklist.ts`
- Create: `frontend/src/prePublishChecklist.test.ts`
- Create: `frontend/src/pages/workspace/useWorkspaceData.ts`
- Create: `frontend/src/pages/workspace/useWorkspaceDragDrop.ts`
- Create: `frontend/src/pages/workspace/WorkspaceTree.tsx`
- Create: `frontend/src/pages/workspace/PublishModal.tsx`
- Create: `frontend/src/pages/workspace/ValidationSummaryPanel.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.validation.test.tsx`

### Phase 3B: Publish Job Service Decomposition

- Follow design: `docs/superpowers/specs/2026-06-18-publish-job-service-decomposition-design.md`
- Create: `src/RATools.Application/Publishing/PublishArtifactResolver.cs`
- Create: `src/RATools.Application/Publishing/PublishReportStore.cs`
- Create: `src/RATools.Application/Abstractions/Publishing/IPublishArtifactStore.cs`
- Create: `src/RATools.Infrastructure/Publishing/LocalPublishArtifactStore.cs`
- Modify: `src/RATools.Application/Publishing/PublishJobService.cs`
- Modify: `src/RATools.Application/Publishing/BackboneService.cs`
- Modify: `src/RATools.Application/Abstractions/Publishing/IBackboneFileWriter.cs`
- Modify: `src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs`
- Modify: `src/RATools.Application/DependencyInjection.cs`
- Modify: `src/RATools.Infrastructure/DependencyInjection.cs`
- Create: `tests/RATools.Tests/Publishing/PublishArtifactResolverTests.cs`
- Create: `tests/RATools.Tests/Publishing/PublishReportStoreTests.cs`
- Create: `tests/RATools.Tests/Infrastructure/LocalPublishArtifactStoreTests.cs`

### Phase 4: PDF Compliance Validation

- Follow design: `docs/superpowers/specs/2026-06-18-pdf-compliance-validation-design.md`
- Create: `src/RATools.Application/Publishing/Validation/Pdf/IPdfInspector.cs`
- Create: `src/RATools.Infrastructure/Publishing/Validation/Pdf/PdfPigPdfInspector.cs`
- Create: PDF rule files under `src/RATools.Application/Validation/Rules/Pdf/`
- Modify: `src/RATools.Application/Validation/Rules/FdaEctdRuleSetProvider.cs`
- Modify: `src/RATools.Application/DependencyInjection.cs`
- Modify: `src/RATools.Infrastructure/DependencyInjection.cs`
- Create: PDF inspector and rule tests under `tests/RATools.Tests/Publishing/Validation/Pdf/` and `tests/RATools.Tests/Validation/Rules/Pdf/`

### Phase 5: Multi-Region Architecture

- Follow design: `docs/superpowers/specs/2026-06-18-multi-region-architecture-design.md`
- Create: regional writer registry and EU provider files under `src/RATools.Application/Publishing/Regions/` and `src/RATools.Application/Standards/`
- Modify: `src/RATools.Application/Applications/EctdTemplates/EctdTemplateRegistry.cs`
- Modify: XML writer and DTD validation abstractions only after FDA regression tests are pinned.
- Add EU writer/profile tests under `tests/RATools.Tests/Publishing/` and `tests/RATools.Tests/Standards/`

---

### Task 1: Preserve Current WIP Boundary

**Files:**
- Inspect only: current modified files and untracked `src/RATools.Application/Validation/Rules/`

- [ ] **Step 1: Capture current status**

Run:

```powershell
git status --short
git diff --stat
```

Expected: only the current validation rule engine WIP and new roadmap docs are present. If unrelated user changes appear, leave them untouched and note them in the task handoff.

- [ ] **Step 2: Confirm phase documents exist**

Run:

```powershell
Test-Path docs\superpowers\specs\2026-07-01-ratools-hardening-roadmap-design.md
Test-Path docs\superpowers\plans\2026-07-01-ratools-hardening-roadmap.md
```

Expected: both commands print `True`.

- [ ] **Step 3: Commit only after user approval**

No automatic commit is required for the roadmap docs. If the user asks to commit, stage only the two roadmap files first:

```powershell
git add -f docs\superpowers\specs\2026-07-01-ratools-hardening-roadmap-design.md docs\superpowers\plans\2026-07-01-ratools-hardening-roadmap.md
git commit -m "docs: add RATools hardening roadmap"
```

Expected: commit succeeds without staging validation rule engine WIP.

---

### Task 2: Finish Rule Engine Skeleton Tests

**Files:**
- Create: `tests/RATools.Tests/Validation/Rules/EctdValidationEngineTests.cs`
- Create: `tests/RATools.Tests/Validation/Rules/FdaEctdRuleSetProviderTests.cs`
- Create: `tests/RATools.Tests/Validation/Rules/FileNamingConventionRuleTests.cs`

- [ ] **Step 1: Write failing engine mapping tests**

Create `tests/RATools.Tests/Validation/Rules/EctdValidationEngineTests.cs`:

```csharp
using RATools.Application.Standards;
using RATools.Application.Validation.Requests;
using RATools.Application.Validation.Rules;

namespace RATools.Tests.Validation.Rules;

public sealed class EctdValidationEngineTests
{
    [Fact]
    public void Evaluate_MapsHighAndMediumFindingsToErrorsAndLowFindingsToWarnings()
    {
        var rule = new StubRule(
            new EctdValidationFinding("HIGH-RULE", "CategoryA", EctdValidationSeverity.High, "High message", "Fix high"),
            new EctdValidationFinding("MEDIUM-RULE", "CategoryB", EctdValidationSeverity.Medium, "Medium message", "Fix medium"),
            new EctdValidationFinding("LOW-RULE", "CategoryC", EctdValidationSeverity.Low, "Low message", "Fix low"));
        var provider = new StubRuleSetProvider(rule);
        var engine = new EctdValidationEngine(provider);

        var findings = engine.Evaluate(CreateContext());

        Assert.Collection(
            findings,
            first =>
            {
                Assert.Equal("ValidationCriteria", first.Source);
                Assert.Equal("Error", first.Severity);
                Assert.Equal("HIGH-RULE", first.Code);
                Assert.Equal("CategoryA", first.Category);
                Assert.Equal("Fix high", first.RecommendedAction);
            },
            second =>
            {
                Assert.Equal("Error", second.Severity);
                Assert.Equal("MEDIUM-RULE", second.Code);
            },
            third =>
            {
                Assert.Equal("Warning", third.Severity);
                Assert.Equal("LOW-RULE", third.Code);
            });
    }

    private static EctdValidationContext CreateContext()
    {
        var profile = new StandardsProfile(
            "us-fda-ectd-3.2.2",
            "US FDA eCTD 3.2.2",
            "US",
            "3.2.2",
            "3.3",
            "4.5",
            []);
        return new EctdValidationContext(profile, new ValidateSequenceRequest(Guid.NewGuid(), "0000"), null, null);
    }

    private sealed class StubRuleSetProvider(params IEctdValidationRule[] rules) : IEctdValidationRuleSetProvider
    {
        public EctdValidationRuleSet GetRuleSet(StandardsProfile profile)
        {
            return new EctdValidationRuleSet(profile.TemplateKey, profile.ValidationCriteriaVersion, rules);
        }
    }

    private sealed class StubRule(params EctdValidationFinding[] findings) : IEctdValidationRule
    {
        public string RuleId => "STUB";
        public string Category => "Stub";
        public EctdValidationSeverity DefaultSeverity => EctdValidationSeverity.Low;

        public IEnumerable<EctdValidationFinding> Evaluate(EctdValidationContext context)
        {
            return findings;
        }
    }
}
```

- [ ] **Step 2: Run engine test and confirm current behavior**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~EctdValidationEngineTests"
```

Expected: pass if current mapping is already correct. If it fails, fix only `EctdValidationEngine.MapFinding`.

- [ ] **Step 3: Write provider selection tests**

Create `tests/RATools.Tests/Validation/Rules/FdaEctdRuleSetProviderTests.cs`:

```csharp
using RATools.Application.Standards;
using RATools.Application.Validation.Rules;

namespace RATools.Tests.Validation.Rules;

public sealed class FdaEctdRuleSetProviderTests
{
    [Fact]
    public void GetRuleSet_ReturnsFdaRulesForFda322Criteria45()
    {
        var rule = new NoopRule();
        var provider = new FdaEctdRuleSetProvider([rule]);

        var ruleSet = provider.GetRuleSet(CreateProfile("us-fda-ectd-3.2.2", "4.5"));

        Assert.Equal("us-fda-ectd-3.2.2", ruleSet.ProfileKey);
        Assert.Equal("4.5", ruleSet.ValidationCriteriaVersion);
        Assert.Same(rule, Assert.Single(ruleSet.Rules));
    }

    [Fact]
    public void GetRuleSet_ThrowsForUnknownProfile()
    {
        var provider = new FdaEctdRuleSetProvider([new NoopRule()]);

        var exception = Assert.Throws<EctdValidationRuleSetNotFoundException>(
            () => provider.GetRuleSet(CreateProfile("eu-ectd-3.2.2", "4.5")));

        Assert.Equal("eu-ectd-3.2.2", exception.TemplateKey);
        Assert.Equal("4.5", exception.ValidationCriteriaVersion);
    }

    private static StandardsProfile CreateProfile(string templateKey, string criteriaVersion)
    {
        return new StandardsProfile(templateKey, templateKey, "US", "3.2.2", "3.3", criteriaVersion, []);
    }

    private sealed class NoopRule : IEctdValidationRule
    {
        public string RuleId => "NOOP";
        public string Category => "Noop";
        public EctdValidationSeverity DefaultSeverity => EctdValidationSeverity.Low;
        public IEnumerable<EctdValidationFinding> Evaluate(EctdValidationContext context) => [];
    }
}
```

- [ ] **Step 4: Write file naming rule tests**

Create `tests/RATools.Tests/Validation/Rules/FileNamingConventionRuleTests.cs` with package builders that use existing `EctdPackageRecords` constructors. Inspect `src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs` before writing the test so constructor arguments match the current records.

Required test cases:

```csharp
[Fact]
public void Evaluate_ReportsUppercaseOrSpaceInFileName()
{
    // Arrange a package with a leaf file name like "Study Report.PDF".
    // Assert the rule returns one finding:
    // Code/RuleId = FDA-NAMING-1, Category = FileNaming, Severity = Medium.
}

[Fact]
public void Evaluate_ReportsPublishedHrefLongerThanLimit()
{
    // Arrange a package with a leaf href longer than 230 characters.
    // Assert one FDA-NAMING-1 finding mentions path-length.
}

[Fact]
public void Evaluate_ReturnsNoFindingsForLowercaseHyphenatedPdf()
{
    // Arrange a package with file name "study-report.pdf" and a short href.
    // Assert no findings.
}
```

- [ ] **Step 5: Run rule tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RATools.Tests.Validation.Rules"
```

Expected: all rule tests pass.

- [ ] **Step 6: Commit phase test skeleton**

After tests pass and implementation is minimal:

```powershell
git add src\RATools.Application\Validation\Rules tests\RATools.Tests\Validation\Rules
git commit -m "test: cover eCTD validation rule engine"
```

Expected: commit contains only rule engine source and tests.

---

### Task 3: Finish Rule Engine Readiness Integration

**Files:**
- Modify: `src/RATools.Application/Validation/PublishReadinessService.cs`
- Modify: `src/RATools.Application/DependencyInjection.cs`
- Modify: `tests/RATools.Tests/Validation/PublishReadinessServiceTests.cs`
- Modify: `tests/RATools.Tests/Publishing/PublishJobServiceRealEctdIntegrationTests.cs`

- [ ] **Step 1: Add readiness integration assertion**

In `tests/RATools.Tests/Validation/PublishReadinessServiceTests.cs`, add or update a test that creates a valid package with a non-conforming file name and asserts:

```csharp
Assert.False(report.IsReady);
var finding = Assert.Single(report.Findings, x => x.Code == "FDA-NAMING-1");
Assert.Equal("ValidationCriteria", finding.Source);
Assert.Equal("Error", finding.Severity);
Assert.Equal("FileNaming", finding.Category);
Assert.Contains("Rename the file", finding.RecommendedAction);
Assert.Contains(report.CategorySummaries, x =>
    x.Category == "FileNaming"
    && x.BlockingErrorCount == 1
    && x.FindingCount == 1);
```

- [ ] **Step 2: Run targeted readiness tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~PublishReadinessServiceTests"
```

Expected: pass after integration is correct. If it fails because severity mapping does not match the desired gate, decide in code whether `Medium` blocks publish or lower the rule severity intentionally. Record the decision in the test name.

- [ ] **Step 3: Verify DI registration**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~ProgramTests|FullyQualifiedName~PublishReadinessApiTests"
```

Expected: pass. This protects application startup and API wiring after adding rule-engine dependencies.

- [ ] **Step 4: Run full backend tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release
```

Expected: 0 failed tests. Analyzer warnings can remain during this phase if they existed before and are not promoted to errors.

- [ ] **Step 5: Commit validation engine closure**

```powershell
git add src\RATools.Application\DependencyInjection.cs src\RATools.Application\Validation\PublishReadinessService.cs src\RATools.Application\Validation\Rules tests\RATools.Tests\Validation tests\RATools.Tests\Publishing
git commit -m "feat: finish eCTD validation rule engine integration"
```

Expected: commit succeeds and backend tests were run immediately before commit.

---

### Task 4: Fix Current Frontend Test Failure

**Files:**
- Modify: `frontend/src/PathPickerFormHosts.test.tsx`

- [ ] **Step 1: Add publish readiness mock to the failing test**

Inside the `validates then submits publish sequence with outputDirectoryPath` fetch mock, add this branch before the generic fallback:

```ts
if (url === '/api/validation/publish-readiness') {
  return Promise.resolve({
    ok: true,
    json: vi.fn().mockResolvedValue({
      applicationId: 'app-1',
      sequenceNumber: '0000',
      isReady: true,
      status: 'Ready',
      blockingErrorCount: 0,
      warningCount: 0,
      validationReport: {
        applicationId: 'app-1',
        sequenceNumber: '0000',
        validationProfile: 'US FDA eCTD 3.2.2',
        isValid: true,
        issues: [],
        sectionMatches: [],
        lifecycleMatches: [],
      },
      missingMetadataFields: [],
      categorySummaries: [],
      findings: [],
    }),
  })
}
```

- [ ] **Step 2: Run the targeted test**

Run:

```powershell
npm test -- src/PathPickerFormHosts.test.tsx
```

Working directory: `frontend`

Expected: 3 tests pass.

- [ ] **Step 3: Run all frontend tests**

Run:

```powershell
npm test
```

Working directory: `frontend`

Expected: 107 tests pass.

- [ ] **Step 4: Commit test recovery**

```powershell
git add frontend\src\PathPickerFormHosts.test.tsx
git commit -m "test: update publish path picker flow for readiness"
```

Expected: commit contains only the test mock update.

---

### Task 5: Clear Frontend Lint Gate

**Files:**
- Modify: `frontend/src/PublishHistoryDetail.test.tsx`
- Modify: `frontend/src/components/publishing/ArtifactsPanel.tsx`
- Modify: `frontend/src/components/publishing/PackageReviewPanel.tsx`
- Modify: `frontend/src/components/publishing/PublishHistoryTab.tsx`
- Modify: `frontend/src/components/publishing/ReportPanel.tsx`
- Modify: `frontend/src/pages/ApplicationDetailsPage.tsx`
- Modify: `frontend/src/pages/ApplicationsPage.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.tsx`
- Modify: `frontend/src/pages/appShared.ts`

- [ ] **Step 1: Reproduce lint baseline**

Run:

```powershell
npm run lint
```

Working directory: `frontend`

Expected: lint fails with current explicit errors. Save the file list from output in the task notes.

- [ ] **Step 2: Remove the unused variable in publish history test**

In `frontend/src/PublishHistoryDetail.test.tsx`, replace the unused placeholder binding reported by lint with an omitted parameter or a named variable that is actually used. For example, if the code is:

```ts
const [_, options] = call
```

change it to:

```ts
const [, options] = call
```

- [ ] **Step 3: Replace page-level `any` shapes with shared types**

In `frontend/src/pages/appShared.ts`, define shared interfaces that match current UI usage:

```ts
export type SequenceSummary = {
  sequenceNumber: string
  submissionType?: string | null
  description?: string | null
}

export type ApplicationSummary = {
  id: string
  applicationNumber: string
  sponsorName: string
  ectdTemplateKey?: string | null
  ectdTemplateDisplayName?: string | null
  workingDirectoryPath?: string | null
  createdUtc?: string | null
  sequences: SequenceSummary[]
}
```

Then replace `any[]` usage in `ApplicationsPage.tsx` and `ApplicationDetailsPage.tsx` with these types.

- [ ] **Step 4: Type publish report and artifact rows**

Add local or shared types for rows currently rendered as `any`, using only fields read by the UI. For artifacts:

```ts
type PublishArtifact = {
  name: string
  type: string
  path?: string | null
  exists: boolean
  sizeBytes: number
  contentType?: string | null
}
```

For publish history rows, include:

```ts
type PublishHistoryRow = {
  id: string
  applicationId: string
  sequenceNumber: string
  status: string
  outputPath?: string | null
  packagePath?: string | null
  createdUtc?: string | null
  completedUtc?: string | null
  failureReason?: string | null
  publishReadiness?: {
    isReady?: boolean
    status?: string
    blockingErrorCount?: number
    warningCount?: number
    missingMetadataFields?: string[]
  } | null
}
```

Use similar minimal read-shape types in `ReportPanel.tsx` and `PackageReviewPanel.tsx`.

- [ ] **Step 5: Fix hook lint errors without changing behavior**

For `react-hooks/set-state-in-effect`, prefer initializing loading state before async calls through a memoized loader function called from effects. For example:

```ts
const loadArtifacts = useCallback(async () => {
  if (!jobId) {
    setArtifacts([])
    return
  }

  setLoading(true)
  try {
    const data = await apiFetch(`/api/publish-jobs/${jobId}/artifacts`)
    setArtifacts(data.artifacts || [])
  } catch (err) {
    const messageText = err instanceof Error ? err.message : 'Unknown error'
    message.error('Failed to load artifacts: ' + messageText)
  } finally {
    setLoading(false)
  }
}, [jobId])

useEffect(() => {
  void loadArtifacts()
}, [loadArtifacts])
```

Apply the same pattern to report and history fetchers while including all dependencies in dependency arrays.

- [ ] **Step 6: Run lint until clean**

Run:

```powershell
npm run lint
```

Working directory: `frontend`

Expected: 0 errors, 0 warnings if current config treats warnings as pass. If warnings remain, fix them before continuing.

- [ ] **Step 7: Run frontend build and tests**

Run:

```powershell
npm run build
npm test
```

Working directory: `frontend`

Expected: build succeeds and all tests pass.

- [ ] **Step 8: Commit frontend gate cleanup**

```powershell
git add frontend\src
git commit -m "fix: restore frontend quality gates"
```

Expected: commit contains lint/test recovery only, not component decomposition.

---

### Task 6: Execute Sequence Workspace Refactor

**Files:**
- Follow the file map in Phase 3A.
- Detailed spec: `docs/superpowers/specs/2026-06-18-sequence-workspace-refactor-design.md`

- [ ] **Step 1: Create a dedicated implementation plan**

Before code changes, create `docs/superpowers/plans/2026-07-01-sequence-workspace-refactor-execution.md` with `apply_patch`.

The plan must split tasks in this order:

1. Extract `prePublishChecklist.ts` and tests.
2. Extract `ValidationSummaryPanel`.
3. Extract `PublishModal`.
4. Extract `useWorkspaceData` with visible errors.
5. Extract `useWorkspaceDragDrop` and `WorkspaceTree`.
6. Add multi-file drag/drop and accessibility tests.
7. Run full frontend gates.

- [ ] **Step 2: Use existing spec as acceptance source**

Open and follow:

```powershell
Get-Content docs\superpowers\specs\2026-06-18-sequence-workspace-refactor-design.md
```

Expected: every acceptance criterion in that spec has at least one task in the phase-specific plan.

- [ ] **Step 3: Execute with frontend gates after each extraction**

After each extraction slice:

```powershell
npm test -- src/pages/SequenceWorkspacePage.validation.test.tsx
npm run lint
npm run build
```

Working directory: `frontend`

Expected: targeted tests and gates remain green.

- [ ] **Step 4: Commit sequence workspace refactor**

```powershell
git add frontend\src
git commit -m "refactor: split sequence workspace page"
```

Expected: commit is behavior-preserving except for documented visible fetch errors, multi-file drag/drop, and accessibility improvements.

---

### Task 7: Execute Publish Job Service Decomposition

**Files:**
- Follow the file map in Phase 3B.
- Detailed spec: `docs/superpowers/specs/2026-06-18-publish-job-service-decomposition-design.md`

- [ ] **Step 1: Create a dedicated implementation plan**

Before code changes, create `docs/superpowers/plans/2026-07-01-publish-job-service-decomposition-execution.md` with `apply_patch`.

The plan must split tasks in this order:

1. Add `IPublishArtifactStore` abstraction and tests.
2. Add infrastructure store with `IWorkspacePathPolicy.EnsureAllowed` tests.
3. Extract `PublishArtifactResolver`.
4. Extract `PublishReportStore`.
5. Remove direct artifact/report IO from `PublishJobService`.
6. Remove the `"{}"` placeholder report write from `BackboneService` / writer path.
7. Run full backend tests.

- [ ] **Step 2: Assert no direct IO remains in `PublishJobService`**

After decomposition, run:

```powershell
Select-String -Path src\RATools.Application\Publishing\PublishJobService.cs -Pattern 'File\.|Directory\.|FileInfo|DirectoryInfo'
```

Expected: no matches.

- [ ] **Step 3: Run backend verification**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release
```

Expected: 0 failed tests.

- [ ] **Step 4: Commit publish job decomposition**

```powershell
git add src\RATools.Application src\RATools.Infrastructure tests\RATools.Tests
git commit -m "refactor: decompose publish job file IO"
```

Expected: commit does not change publish API contracts.

---

### Task 8: Implement PDF Compliance Through Rule Engine

**Files:**
- Follow Phase 4 file map.
- Detailed spec: `docs/superpowers/specs/2026-06-18-pdf-compliance-validation-design.md`

- [ ] **Step 1: Create a dedicated implementation plan**

Before code changes, create `docs/superpowers/plans/2026-07-01-pdf-compliance-validation-execution.md` with `apply_patch`.

The plan must split tasks in this order:

1. Select and add the PDF library package with version lock.
2. Define `IPdfInspector` and result records.
3. Implement infrastructure inspector.
4. Add inspector tests with small fixture PDFs.
5. Add PDF rules that consume `EctdValidationContext.Package`.
6. Register PDF rules in the FDA rule set.
7. Add readiness integration tests.
8. Run full backend tests.

- [ ] **Step 2: Require rule-engine path**

In code review, reject any PDF validation that directly adds bespoke checks to `PublishReadinessService` instead of using `IEctdValidationRule`.

- [ ] **Step 3: Run backend verification**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release
```

Expected: 0 failed tests.

- [ ] **Step 4: Commit PDF compliance**

```powershell
git add src tests
git commit -m "feat: add PDF compliance readiness rules"
```

Expected: commit includes rule-engine based PDF readiness findings.

---

### Task 9: Implement Multi-Region Architecture

**Files:**
- Follow Phase 5 file map.
- Detailed spec: `docs/superpowers/specs/2026-06-18-multi-region-architecture-design.md`

- [ ] **Step 1: Create a dedicated implementation plan**

Before code changes, create `docs/superpowers/plans/2026-07-01-multi-region-architecture-execution.md` with `apply_patch`.

The plan must split tasks in this order:

1. Pin FDA XML output regression tests.
2. Introduce regional writer registry with US writer only.
3. Parameterize DTD and namespace selection while preserving FDA output.
4. Introduce composite standards profile provider.
5. Add EU template/profile metadata.
6. Add EU regional writer and DTD assets.
7. Add EU readiness dry-run tests.
8. Run full backend and frontend tests.

- [ ] **Step 2: Verify FDA behavior before EU behavior**

Run FDA-focused tests before enabling EU behavior:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~IchIndexXmlWriterTests|FullyQualifiedName~UsRegionalXmlWriterTests|FullyQualifiedName~FdaEctd322StandardsProfileProviderTests"
```

Expected: FDA tests pass before and after writer/profile abstraction changes.

- [ ] **Step 3: Run full verification**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release
npm run lint
npm run build
npm test
```

Expected: backend and frontend gates pass.

- [ ] **Step 4: Commit multi-region architecture**

```powershell
git add src tests frontend docs
git commit -m "feat: add multi-region eCTD architecture"
```

Expected: commit preserves FDA behavior and adds EU as a controlled second region.

---

## Global Verification

After each phase, run the relevant full gate:

Backend:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release
```

Frontend:

```powershell
npm run lint
npm run build
npm test
```

Working directory for frontend commands: `frontend`.

Smoke test after Phase 3B or later:

```powershell
dotnet run --project src\RATools.Api\RATools.Api.csproj
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

Expected: smoke test covers application/sequence creation, upload, reassignment, validation, publish execution, report retrieval, artifact listing/download, history filters, audit logs, duplicate file-name handling, and generated backbone metadata.

## Self-Review Notes

- Spec coverage: the plan covers the ordered phases requested by the user and maps each phase to files, existing specs, tests, and stop gates.
- Placeholder scan: no implementation step depends on an unspecified future decision. Later large phases explicitly require dedicated execution plans before code changes because their existing specs are broad enough to deserve separate branches.
- Type consistency: terms match existing code names such as `IEctdValidationEngine`, `PublishReadinessService`, `PublishJobService`, `SequenceWorkspacePage`, and `PublishReadinessFindingDto`.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-01-ratools-hardening-roadmap.md`. Two execution options:

1. **Subagent-Driven (recommended)** - dispatch a fresh subagent per phase/task, review between tasks, fast iteration.
2. **Inline Execution** - execute tasks in this session using executing-plans, with checkpoints after each phase.

Choose the execution mode only after reviewing the roadmap spec and this master plan.
