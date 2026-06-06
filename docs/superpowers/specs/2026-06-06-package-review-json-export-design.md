# Package Review JSON Export Design

## Summary

Add a frontend-only JSON export to the Package Review drawer. The export gives reviewers a machine-readable snapshot of the package review state using data that `PackageReviewPanel` already loads from the publish report and artifact endpoints.

The first version exports JSON only. It does not add a backend endpoint, persistence, signatures, PDF, Markdown, upload, or automatic archive behavior.

## Goals

- Add a `Download Review JSON` action to the top button area of `PackageReviewPanel`.
- Generate the export from the currently loaded report and artifact data without making new API requests.
- Keep existing `Download Package` and `Download Report` actions unchanged.
- Allow partial exports when either the report or artifacts request succeeds.
- Disable export only when both report and artifacts are unavailable.
- Cover export behavior with frontend tests in `frontend/src/PublishHistoryDetail.test.tsx`.

## Non-Goals

- PDF export.
- Markdown export.
- Backend persistence.
- Reviewer signatures.
- A new API endpoint.
- Automatic upload or archival of the review file.

## User Experience

`PackageReviewPanel` keeps its current header layout. The button group adds a third action:

- `Download Review JSON`
- `Download Package`
- `Download Report`

`Download Review JSON` is enabled when at least one of these conditions is true:

- The report request completed successfully and `report` is available.
- The artifacts request completed successfully and the normalized artifacts array is available.

It is disabled when both requests fail or neither has produced usable data. During the existing loading state the drawer shows the spinner, so the button is not available until loading completes.

Clicking the button creates a JSON `Blob`, downloads it in the browser, and does not call any API. The filename is:

```text
package-review-${sequenceNumber || 'unknown'}-${publishJobId}.json
```

`sequenceNumber` comes from `report.sequenceNumber` when available. If the report is unavailable, it falls back to `unknown`. `publishJobId` is the current `jobId` prop.

If JSON generation or download setup throws, the UI shows:

```text
Failed to export package review.
```

## Export Shape

The export root object uses this version marker:

```json
{
  "reportVersion": "package-review-export-v1"
}
```

A complete export has this shape:

```json
{
  "reportVersion": "package-review-export-v1",
  "generatedAtUtc": "2026-06-06T00:00:00.000Z",
  "publishJobId": "job-1",
  "sequenceNumber": "0001",
  "validationProfile": "US FDA eCTD 3.2.2",
  "verdict": "NotReadyForSubmission",
  "checklist": [
    {
      "key": "publish-succeeded",
      "check": "Publish succeeded",
      "status": "Pass",
      "detail": "Publish completed successfully."
    }
  ],
  "riskSummary": {
    "validationErrors": 1,
    "warnings": 2,
    "lifecycleIssues": 1,
    "missingFiles": 0,
    "missingZipEntries": 1,
    "mismatchedArtifacts": 0
  },
  "requiredArtifacts": [
    {
      "name": "PackageZip",
      "exists": true,
      "sizeBytes": 2048,
      "contentType": "application/zip"
    }
  ],
  "integrityFindings": [
    {
      "severity": "Error",
      "type": "MissingZipEntry",
      "path": "m1/us/11-forms/leaf.pdf",
      "message": "Output file is missing from package zip."
    }
  ]
}
```

### Field Rules

- `reportVersion`: constant string `package-review-export-v1`.
- `generatedAtUtc`: `new Date().toISOString()` at click time.
- `publishJobId`: current `jobId`.
- `sequenceNumber`: `report.sequenceNumber` when available; otherwise `null` in the JSON export.
- `validationProfile`: `report.validationProfile` when available; otherwise `null`.
- `verdict`: `ReadyForSubmission` when every checklist row passes; otherwise `NotReadyForSubmission`.
- `checklist`: derived from the same checklist rows rendered in the review drawer.
- `checklist[].status`: `Pass` for passing rows and `Fail` for failing rows.
- `riskSummary.validationErrors`: `report.errorCount` when available; otherwise `null`.
- `riskSummary.warnings`: `report.warningCount` when available; otherwise `null`.
- `riskSummary.lifecycleIssues`: count of lifecycle matches whose `resultCode` is not `MATCHED` when report data is available; otherwise `null`.
- `riskSummary.missingFiles`: `report.integritySummary.missingFilesCount` when available; otherwise `null`.
- `riskSummary.missingZipEntries`: `report.integritySummary.missingZipEntriesCount` when available; otherwise `null`.
- `riskSummary.mismatchedArtifacts`: `report.integritySummary.mismatchedArtifactsCount` when available; otherwise `null`.
- `requiredArtifacts`: the same required artifact set used by the drawer: `BackboneXml`, `PublishReport`, `PackageZip`. Each row includes `name`, `exists`, `sizeBytes`, and `contentType`; missing required artifacts are represented as `{ "name": "...", "exists": false }`.
- `integrityFindings`: `report.integrityEvidence.findings` when available; otherwise an empty array.

## Partial Export Errors

When one source fails and the other succeeds, export still works and includes an `errors` object.

If the report request fails:

```json
{
  "errors": {
    "report": {
      "message": "Publish report is corrupted.",
      "status": 422
    }
  }
}
```

If the artifacts request fails:

```json
{
  "errors": {
    "artifacts": {
      "message": "Artifacts unavailable.",
      "status": 404
    }
  }
}
```

`status` is included only when the error is an `ApiRequestError` with a numeric status. Non-API errors include only `message`.

When both report and artifacts fail, `Download Review JSON` is disabled. The existing warning alerts remain visible to explain the failures.

## Implementation Notes

The implementation should keep the change inside `PackageReviewPanel` unless extracting a tiny helper makes testing or readability materially better. The existing data normalization remains the source of truth for artifacts.

The export handler should:

- Build the export object from current React state.
- Serialize with `JSON.stringify(exportObject, null, 2)`.
- Create an `application/json` blob.
- Create a temporary object URL.
- Create and click a temporary anchor with the computed filename.
- Revoke the object URL and remove the anchor after triggering the download.

The handler should not call `/report`, `/artifacts`, or any download endpoint.

## Testing

Extend `frontend/src/PublishHistoryDetail.test.tsx` to verify:

- The review drawer shows `Download Review JSON`.
- Clicking `Download Review JSON` creates a JSON blob.
- The JSON contains `verdict`, `checklist`, `riskSummary`, `requiredArtifacts`, and `integrityFindings`.
- When the artifact endpoint fails but the report endpoint succeeds, export still works and includes `errors.artifacts`.
- When both report and artifact endpoints fail, `Download Review JSON` is disabled.

Tests can mock browser download primitives such as `URL.createObjectURL`, `URL.revokeObjectURL`, and anchor clicking as needed. Assertions should inspect the generated `Blob` text instead of relying on the file system.

## Acceptance Criteria

- `PackageReviewPanel` exposes `Download Review JSON` alongside the existing package and report download actions.
- The button is enabled when report or artifacts data is available and disabled when both are unavailable.
- The generated JSON matches `package-review-export-v1` and includes the required review, risk, artifact, finding, and partial error fields.
- The export action performs no API request.
- Existing package and report download buttons retain their current behavior.
- The frontend test suite covers successful export, partial export, and disabled export states.
