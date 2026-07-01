# RATools Hardening Roadmap Design

## Overview

RATools-for-eCTD has crossed the basic CRUD and package-generation threshold. The backend can manage applications and sequences, validate placements, generate FDA eCTD 3.2.2 backbone files, execute publish jobs, persist reports and artifacts, and run an end-to-end smoke test. The frontend can create/import applications, enter sequence workspaces, validate, publish, and review history.

The next improvement arc should therefore prioritize trust, maintainability, and regulatory value over adding more surface area. The recommended sequence is:

1. Finish and green-light the current validation rule engine work.
2. Clear frontend quality gates.
3. Refactor large frontend and backend modules.
4. Extend compliance depth with PDF checks.
5. Expand to multi-region only after the FDA-centered path is stable.

This design is a coordination layer over existing focused designs. It does not replace the detailed specs already present for the validation rule engine, frontend quality gates, sequence workspace refactor, publish job service decomposition, PDF compliance validation, or multi-region architecture.

## Current Evidence

The current working tree has active validation rule engine changes:

- `src/RATools.Application/DependencyInjection.cs` registers `IEctdValidationRule`, `IEctdValidationRuleSetProvider`, and `IEctdValidationEngine`.
- `src/RATools.Application/Validation/PublishReadinessService.cs` calls the rule engine after dry-run package construction and DTD validation.
- `src/RATools.Application/Validation/Rules/` is untracked and contains the rule engine skeleton plus `FileNamingConventionRule`.
- Backend tests recently passed: `dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release` reported 160 passed tests, with analyzer and NuGet vulnerability-source warnings.
- Frontend build recently passed: `npm run build`.
- Frontend gates are not green: `npm run lint` reported 59 errors, and `npm test` reported 1 failed test. The failed test is in `frontend/src/PathPickerFormHosts.test.tsx` and is caused by the test not mocking the newer `/api/validation/publish-readiness` call before the publish modal opens.

Existing detailed designs that this roadmap coordinates:

- `docs/superpowers/specs/2026-06-18-ectd-validation-rule-engine-design.md`
- `docs/superpowers/specs/2026-06-18-build-quality-gates-design.md`
- `docs/superpowers/specs/2026-06-18-sequence-workspace-refactor-design.md`
- `docs/superpowers/specs/2026-06-18-publish-job-service-decomposition-design.md`
- `docs/superpowers/specs/2026-06-18-pdf-compliance-validation-design.md`
- `docs/superpowers/specs/2026-06-18-multi-region-architecture-design.md`

## Goals

1. Turn the validation rule engine from partially integrated WIP into a tested, stable publish-readiness extension point.
2. Restore a clean frontend development baseline: lint, build, and tests all pass.
3. Reduce the cost of future work by splitting large modules at natural seams before adding deeper compliance logic.
4. Add PDF technical compliance validation through the rule engine rather than a parallel validation system.
5. Defer multi-region implementation until FDA rule execution, PDF checks, and core maintainability work are stable.
6. Keep each phase independently shippable and verifiable.

## Non-Goals

- Do not implement all phases in a single branch.
- Do not start EU or multi-region behavior before the FDA validation/reporting path is green.
- Do not add a second validation reporting model for PDF checks; PDF findings must flow through the same rule engine and readiness DTOs.
- Do not treat broad refactors as license to change user-visible workflow semantics.
- Do not clean or remove existing user WIP without explicit approval.

## Approach Options

### Option A: Finish Compliance Core First, Then Refactor

Finish the validation rule engine and PDF checks first, then refactor UI and service internals. This maximizes user-facing compliance value early, but it builds new behavior on top of modules that are already large and noisy. It risks making later refactors harder.

### Option B: Refactor Everything First, Then Add Compliance

Clear frontend/backend structure before touching validation depth. This improves the development surface, but delays the most product-defining work: reliable technical rejection risk detection.

### Option C: Stabilize Current WIP, Green Gates, Then Refactor and Extend

Complete the current validation rule engine WIP, restore frontend gates, then refactor large modules before adding PDF and multi-region. This is the recommended path. It respects current momentum, prevents the project from drifting with broken gates, and creates cleaner boundaries before the next complex features arrive.

## Recommended Design

### Phase 1: Validation Rule Engine Closure

Complete the active `RATools.Application.Validation.Rules` work as the next backend feature slice. The first stable rule-engine release should include:

- Rule abstractions, rule set provider, engine, and DI registration.
- FDA eCTD 3.2.2 / validation criteria 4.5 rule set selection from `StandardsProfile`.
- `PublishReadinessService` integration that merges rule findings into existing readiness findings and category summaries.
- At least one concrete FDA rule, currently file naming/path length, covered by unit tests and readiness integration tests.
- Explicit behavior for unknown profile/version combinations.
- Full backend test pass.

This phase should not attempt to implement every planned FDA rule. It should make the extension point stable enough that later rules are straightforward and low-risk.

### Phase 2: Frontend Gate Recovery

Restore the frontend to a dependable baseline:

- Fix `PathPickerFormHosts.test.tsx` to reflect the current publish flow, including `/api/validation/publish-readiness`.
- Remove or type the explicit `any` usage currently causing lint failures.
- Fix React hook lint errors in publishing panels and page components.
- Keep `npm run build`, `npm test`, and `npm run lint` green.

This phase is a gate before larger UI refactors. Refactoring with failing lint/tests makes it too easy to hide regressions in noise.

### Phase 3: Large Module Decomposition

Use existing focused specs to split high-friction modules:

- Frontend: split `SequenceWorkspacePage.tsx` into pure checklist logic, data hooks, drag/drop hook, workspace tree, publish modal, and validation summary panel.
- Backend: split `PublishJobService` into publishing orchestration plus report/artifact collaborators, and move direct file IO behind storage/path policy abstractions.

These refactors should be behavior-preserving except for explicitly documented fixes already in their specs, such as visible workspace data fetch errors and multi-file drag/drop handling.

### Phase 4: PDF Compliance Validation

After the rule engine and large-module boundaries are stable, add PDF technical checks through the rule engine:

- Define `IPdfInspector` in Application and an infrastructure implementation using a license-friendly library.
- Extract PDF metadata and links.
- Implement PDF validation rules for encryption, searchable text, font embedding, bookmarks, version policy, and broken links.
- Return findings through `PublishReadinessFindingDto`.

PDF work should depend on the rule engine rather than adding direct checks inside `PublishReadinessService`.

### Phase 5: Multi-Region Architecture

Start multi-region only after the FDA-centered compliance path is stable:

- Introduce regional writer registry and composite profile provider.
- Parameterize DTD assets and writer metadata through selected profiles.
- Add EU templates and regional writer only after the abstraction can preserve FDA output.
- Keep EU validation rules separate from FDA rules and selected by profile/version.

Multi-region should be the architecture expansion, not the first place the validation engine proves itself.

## Data Flow

The intended compliance flow after Phase 4 is:

1. `PublishJobService` asks `ISequenceValidationService` for baseline sequence validation.
2. `PublishReadinessService` builds an `EctdSequencePackage` dry run.
3. XML writers generate in-memory backbone files.
4. DTD validation runs on generated backbone files.
5. `IStandardsProfileProvider` resolves the application template profile.
6. `IEctdValidationEngine` selects a rule set by template/profile/version.
7. Rules evaluate the package, generated files, and any rule-specific adapters such as `IPdfInspector`.
8. Rule findings map into `PublishReadinessFindingDto`.
9. Existing readiness summaries determine whether publish can proceed.

## Error Handling

- Rule engine configuration errors, such as unknown profile/version, should fail fast and be covered by tests.
- Rule evaluation for inspectable content, such as PDF parsing, should convert malformed file inspection failures into actionable readiness findings when the package itself is otherwise buildable.
- Frontend publish flow should fail closed: if validation or readiness cannot return a structurally usable response, the publish modal must not open.
- Refactor phases must preserve existing API error mapping and problem response behavior.

## Testing Strategy

Each phase has its own gate:

- Phase 1: backend rule and readiness tests plus `dotnet test tests\RATools.Tests\RATools.Tests.csproj --configuration Release`.
- Phase 2: `npm run lint`, `npm run build`, and `npm test`.
- Phase 3 frontend: targeted component/hook tests plus full frontend gates.
- Phase 3 backend: targeted publish service/report/artifact tests plus full backend tests.
- Phase 4: PDF inspector tests, PDF rule tests, readiness integration tests, and full backend tests.
- Phase 5: FDA regression tests, EU profile/writer tests, DTD validation tests, and full backend tests.

## Risks

**Risk:** The roadmap is too broad for one implementation branch.

**Mitigation:** Treat this roadmap as a sequence of independently shippable branches. Each phase has its own acceptance gate and can be paused after it is green.

**Risk:** The rule engine duplicates existing validation findings.

**Mitigation:** Phase 1 should explicitly test that rule findings are additive and that existing `SequenceValidationService` issues are not duplicated under new rule IDs.

**Risk:** Frontend refactors change behavior while lint is already broken.

**Mitigation:** Phase 2 must clear the frontend baseline before Phase 3 starts.

**Risk:** PDF parsing introduces dependency or performance risk.

**Mitigation:** Isolate the PDF library behind `IPdfInspector`, lock the package version, and convert parser failures into findings rather than unhandled exceptions where possible.

**Risk:** Multi-region changes accidentally alter FDA output.

**Mitigation:** Parameterize while keeping FDA values byte-for-byte equivalent, and require existing FDA writer and readiness tests to pass before EU behavior is enabled.

## Acceptance Criteria

The roadmap design is satisfied when:

- A coordinating implementation plan exists and orders the phases as rule engine, frontend gates, large module refactors, PDF, and multi-region.
- Phase 1 has a concrete executable plan that starts from the current validation rule engine WIP.
- Phase 2 has a concrete executable plan that addresses the current frontend lint/test failures.
- Later phases point to their existing detailed specs and define clear start gates, stop gates, and verification commands.
- No implementation code is changed as part of this roadmap documentation step.
