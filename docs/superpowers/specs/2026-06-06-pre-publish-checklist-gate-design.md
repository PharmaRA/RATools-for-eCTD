# Pre-Publish Checklist Gate Design

## Goal

Add a clearer pre-publish checklist gate to the sequence workspace so users can see why publishing is blocked before they choose an export directory.

The gate reuses the existing validation-first publish flow. It does not add a new backend endpoint, does not persist reviewer decisions, and does not remove the backend validation safety net in `PublishJobService`.

## Current Flow

`SequenceWorkspacePage` already validates before publishing:

- Clicking `Publish Sequence` calls `/api/validation/sequence` through `validateSequence`.
- If `ValidationReport.isValid` is `false`, the page shows the validation summary and does not open the export directory modal.
- If validation passes, the export directory modal opens and `triggerPublish` calls `/api/publish-jobs/execute`.
- The backend publish service validates again before generating artifacts.

This design keeps that control flow and changes the summary into a checklist-oriented gate.

## User Decisions

- Only validation issues with severity `Error` block publishing.
- Warnings and non-standard section matches are visible for reviewer awareness, but they do not block publishing.
- The gate should reuse the existing page-level validation summary rather than adding a separate modal or drawer.

## User Experience

Clicking `Publish Sequence` still starts validation immediately.

When validation cannot run or returns blocking errors:

- The export directory modal stays closed.
- The page shows a `Pre-publish Checklist` summary at the top of the workspace.
- Failed rows explain what blocks publishing.
- Existing `Locate` actions remain available for issues, lifecycle rows, and abnormal section rows.

When validation returns no blocking errors:

- The page shows the same checklist in a passing state.
- The existing `Publish Sequence` modal opens for the export directory.
- The modal includes a short checklist summary, such as `Pre-publish checks passed. 3 warning(s) remain for reviewer awareness.`

Warnings remain visible in the page-level checklist so users can review them without losing the normal publish flow.

## Checklist Rules

The frontend derives checklist rows from `ValidationReport` and the existing API error fallback.

Strict blocking rows:

- `Validation API reachable`: passes when `/api/validation/sequence` returns a report; fails on API errors.
- `No blocking validation errors`: passes when no issue has `severity` equal to `Error` case-insensitively; fails otherwise.

Awareness rows:

- `Lifecycle targets resolved`: shows lifecycle rows whose `resultCode` is not `MATCHED`. These rows are expected to correspond to blocking error issues, but the blocking decision still comes from issue severity.
- `Section paths acceptable`: invalid sections block through `INVALID_SECTION_PATH` error issues; non-standard sections are shown as warnings only.
- `Warnings reviewed`: shows the warning count and warning details, but always stays non-blocking.

The overall gate blocks publishing only when the validation API failed or the validation report contains at least one `Error` severity issue.

## Data Flow

No API contract changes are required.

`openPublishModal` keeps this sequence:

1. Clear previous validation result and close any existing publish modal.
2. Call `validateSequenceProvider({ applicationId, sequenceNumber })`.
3. Convert the returned report into a checklist view model.
4. Store the report so the page renders the gate.
5. Open the export directory modal only when the gate has no blocking failure.

API errors are converted into the existing synthetic validation report with `API_ERROR`, severity `Error`, and profile `Validation API`.

`triggerPublish` does not run another frontend validation. The backend publish service remains responsible for final validation at execution time.

## Component Design

Keep the change local to `SequenceWorkspacePage` unless small local helpers become too hard to read.

The current `validationSummary` view model becomes a checklist-focused view model with:

- `severity`: success or error for the top-level `Alert`.
- `blockingIssueCount`: number of `Error` severity issues.
- `warningCount`: number of non-error issues.
- `canProceed`: false for API errors or blocking issues; true otherwise.
- `checklistRows`: rows with `label`, `status`, `detail`, and optional awareness semantics.
- Existing issue, lifecycle, and section row collections used by the current detail rendering.

The existing issue, lifecycle, and section detail blocks can remain below the checklist rows. Their labels should shift from generic validation language to gate language, for example `Blocking Issues`, `Warnings`, `Lifecycle Targets`, and `Section Matches`.

The export directory modal stays the same except for a small informational `Alert` above the form when validation has passed.

## Error Handling

Validation API failure is a blocking gate failure. The page should show:

- Top-level status: `Pre-publish checks failed`.
- Checklist row failure: `Validation API reachable`.
- Synthetic issue code: `API_ERROR`.
- No export directory modal.

Stale `Locate` targets keep the existing warning: `Could not locate this validation issue in the workspace tree.`

If validation passes but publish execution later fails, existing publish error handling remains unchanged.

## Tests

Extend `frontend/src/pages/SequenceWorkspacePage.validation.test.tsx`.

Required coverage:

- Error issues block publishing, keep the export directory modal closed, and render a failed pre-publish checklist.
- Warning-only reports do not block publishing, open the export directory modal, and show warning awareness in the checklist.
- API validation errors block publishing and fail the `Validation API reachable` row.
- Non-standard section warnings do not block publishing.
- Existing `Locate` behavior still works from checklist detail rows.
- Publish submission still calls `createAndExecutePublishJobProvider` only after validation succeeds and an export directory is provided.

No backend tests are required for this design because no backend behavior or API contract changes are planned.

## Non-Goals

- No new backend preflight or readiness endpoint.
- No persisted checklist, reviewer signature, or audit event.
- No user acknowledgement checkbox for warnings.
- No second frontend validation inside `triggerPublish`.
- No changes to publish artifact generation, package review, or publish history.

## Future Extensions

Possible later additions:

- Persist reviewer acknowledgement for warnings.
- Move checklist computation into a backend readiness endpoint for automation clients.
- Add exportable pre-publish readiness reports.
- Add links from failed checklist rows to publish/package review documentation.
