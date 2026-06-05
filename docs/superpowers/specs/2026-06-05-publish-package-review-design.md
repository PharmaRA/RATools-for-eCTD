# Publish Package Review MVP Design

## Goal

Add a first-pass publish package review drawer that lets a user decide whether a completed publish package is ready for submission without opening separate report and artifact drawers.

The MVP focuses on a strict go/no-go checklist built from existing publish report and artifact APIs. It does not add a new backend review endpoint, does not change artifact download behavior, and does not introduce persisted review decisions.

## User Experience

Publish history rows gain a `Review` action before the existing `Report` and `Artifacts` actions. Clicking `Review` opens a `PackageReviewPanel` drawer.

The drawer presents a review-first layout:

```text
Package Review
------------------------------------------------------------
Not Ready for Submission              [Download Package] [Download Report]
Sequence 0001 | Completed | US FDA eCTD 3.2.2

Submission Readiness Checklist
[pass/fail] Publish succeeded
[pass/fail] Validation errors: 0
[pass/fail] Lifecycle issues: 0
[pass/fail] Integrity consistent
[pass/fail] Required artifacts present

Risk Summary
Validation errors | warnings | lifecycle issues
Missing files | missing zip entries | mismatched artifacts

Evidence Preview
Integrity Findings table, shown when findings exist
Required Artifacts table for BackboneXml, PublishReport, PackageZip
```

The top verdict is `Ready for Submission` only when every strict checklist item passes. Otherwise it is `Not Ready for Submission`.

## Verdict Rules

The frontend computes the MVP verdict from existing data returned by `/api/publish-jobs/{id}/report` and `/api/publish-jobs/{id}/artifacts`.

A package is ready only when all conditions are true:

- `report.succeeded === true`
- `report.errorCount === 0`
- lifecycle issue count is `0`
- `report.integritySummary?.isConsistent === true`
- required artifacts `BackboneXml`, `PublishReport`, and `PackageZip` are present and `exists === true`

Warnings do not block readiness, but they are shown in the risk summary.

Missing or unavailable report/artifact data makes the relevant checklist item fail and shows an explanatory alert in the drawer.

## Data Flow

`PublishHistoryTab` manages a new `selectedReviewJobId` state and renders `PackageReviewPanel` alongside the existing `ReportPanel` and `ArtifactsPanel`.

`PackageReviewPanel` fetches report and artifacts when opened:

- `GET /api/publish-jobs/{jobId}/report`
- `GET /api/publish-jobs/{jobId}/artifacts`

The two requests can run independently. The panel shows a loading state until both settle. If one request fails, the panel still renders the data it has and marks checks depending on the failed data as failed or unavailable.

## Component Design

Create `frontend/src/components/publishing/PackageReviewPanel.tsx`.

The component owns only presentation and MVP verdict computation. It should keep helper functions local unless existing app-shared helpers already cover the behavior.

Key helpers:

- `getLifecycleIssueCountFromReport(report)`: derives lifecycle issue count from `validationReport.lifecycleMatches` using the same result-code semantics as current report UI.
- `getRequiredArtifactStatus(artifacts)`: checks required artifact names and existence.
- `buildChecklist(report, artifacts, errors)`: returns rows for display and the final boolean verdict.

The drawer should use existing Ant Design patterns already used by `ReportPanel` and `ArtifactsPanel`: `Drawer`, `Alert`, `Card`, `Descriptions`, `Table`, `Tag`, `Button`, `Spin`, and `Space`.

## Error Handling

If the report endpoint returns:

- `404`: show report missing, fail report-dependent checks.
- `409`: show job not ready, fail readiness.
- `410`: show report unavailable, fail report-dependent checks.
- `422`: show report corrupted, fail report-dependent checks.

If artifacts fail to load, show an alert and fail the required artifacts check.

The drawer should not close automatically on errors.

## Downloads

The drawer includes direct links for existing downloads:

- Package: `/api/publish-jobs/{jobId}/artifacts/PackageZip/download`
- Report: `/api/publish-jobs/{jobId}/artifacts/PublishReport/download`

Download buttons are enabled only when the relevant artifact exists in the artifact response. If artifact data is unavailable, buttons are disabled.

## Tests

Extend `frontend/src/PublishHistoryDetail.test.tsx`.

Test coverage:

- Publish history shows a `Review` action.
- Opening Review fetches both report and artifacts.
- Strict `Not Ready for Submission` verdict appears when integrity or lifecycle checks fail.
- Checklist shows pass/fail rows for publish success, validation errors, lifecycle issues, integrity consistency, and required artifacts.
- Evidence preview shows integrity findings and required artifact rows.
- `Ready for Submission` appears when all strict checks pass.
- Artifact endpoint failure leaves the drawer open, shows an alert, and fails required artifacts.

No backend tests are required for the MVP because it uses existing backend endpoints without changing backend behavior.

## Non-Goals

- No new backend `review` endpoint.
- No persisted review state, reviewer signature, or audit event.
- No package contents browser beyond the evidence/required artifact preview.
- No export of the review checklist.
- No changes to publish execution or artifact generation.

## Future Extensions

If the review verdict needs to be shared with API clients or automation, move verdict computation into a backend `PublishPackageReviewDto` endpoint while preserving the drawer UI.

Possible later additions:

- Review checklist export as JSON or PDF.
- Reviewer acknowledgement/audit trail.
- Deep package manifest browser.
- Direct navigation from failed checks to matching report tab rows.
