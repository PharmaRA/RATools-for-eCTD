# PDF navigation processing — P5-06

The workspace PDF panel captures an immutable source, previews a version-bound
navigation plan, starts a durable processing job, and lets the operator explicitly
adopt a checked result into the same editable draft. An adopted document keeps its
placement, leaf ID, node, order, lifecycle target and delivery href. The source and
result captures stay separate from the editable working copy.

## Supported operations

- Replace, add, reorder or clear a Unicode bookmark tree with explicit hierarchy
  and page/named destinations. `null` preserves the tree; `[]` explicitly clears it.
- Change PageMode without changing other navigation or page content.
- Edit one inspected page annotation at a time: internal page or named destination,
  remote PDF page or named destination, or an external URI.
- Resolve remote targets through explicit application placement/version identities
  and capture their bytes. Target paths, current bytes and destination validity are
  checked before approving a plan and again before result adoption.

Remote fragments use the same case-sensitive contract in preview and execution:
`#page=2` means page 2, `#nameddest=page=2` means the literal name `page=2`, and
`#PAGE=2` is the literal name `PAGE=2`. An empty fragment selects the first page.
The UI offers explicit page and named-destination modes.

Encrypted PDFs, signature-bearing PDFs and incomplete navigation inspections cannot
be edited through this workflow. Signature presence does not assert signature
validity. Unsupported font/OCR/conformance properties are not promoted to verified.

## Runtime and storage

Configure `PdfProcessing:Engine:ExecutablePath` with a separately installed qpdf
12.4.1 executable. Environment binding uses `PdfProcessing__Engine__ExecutablePath`.
No qpdf binary or runtime PyMuPDF dependency is shipped by this change. Deployment
and redistribution conditions remain in `PDF-ENGINE-QUALIFICATION.md`.

The default limits are a 512 MiB PDF, 64 MiB JSON and a five-minute execution
timeout. Structured process arguments avoid a shell. Output is bounded while
streaming to an exclusive staging file; cancellation/error stops and awaits the
process tree. The final result path appears only after checks pass. A separate
worker processes one PDF job at a time without consuming the publish queue.
This is not an OS-level memory sandbox or a Linux qualification result.

The PostgreSQL migration `20261104161515_AddDocumentProcessing` stores immutable
versions/plans, processing jobs and adoption audit records. A request key is bound
to its full plan. Leases use owner, token and expiration; crashed attempts stop
after three claims. Definitively corrupted queued requests become failed records
so that they cannot starve later jobs. Database guards reject mutation/truncation
of retained evidence and reject downgrade when evidence exists.

Source captures and outputs live under the application's `_document-versions`.
Adoption materializes an independent draft copy; the established delivery-path
resolver continues to emit the original logical href. Adoption updates the new
document, placement, revision and audit in one transaction. Existing draft deletion
paths refuse documents/sequences/applications with processing evidence. Failed
attempt directories are retained for operational review; no automatic physical
file deletion is introduced.

## API and UI

Routes are rooted at
`/api/applications/{applicationId}/sequences/{sequenceNumber}/pdf-processing`:

| Method/path | Behavior |
| --- | --- |
| `POST placements/{placementId}/inspection` | Capture source and inspect navigation at an expected revision |
| `POST placements/{placementId}/plans` | Verify source, edits and targets; return a digest-bound plan |
| `POST jobs` | Enqueue/replay the complete request with plan ID, digest and idempotency key |
| `GET jobs/{jobId}` | Read status/result/adoption within the requested workspace |
| `POST jobs/{jobId}/cancel` | Request cancellation |
| `POST jobs/{jobId}/adopt` | Adopt at the reviewed revision; replay the same adoption |
| `GET versions/{versionId}/content` | Verified PDF content with Range and ETag |

The frontend invalidates an edited preview, saves the complete enqueue handle
before sending it, and can recover the same request after a lost response or
component remount. Stale requests cannot overwrite a newer handle. A changed draft
can still query/cancel/download a task but cannot adopt it. Successful adoption
invalidates the old validation/readiness results even after selection changes.
Downloads use the authenticated API, verify SHA-256 and do not prevent cancellation.

## Verification on Windows, 2026-11-05

- Four current layers compiled with .NET 8/C# 12, recommended analyzers and warnings
  as errors. Compiler informational diagnostics are not warnings.
- 50 processing tests passed, including real qpdf, independent PdfPig inspection,
  actual HTTP worker/Range/adoption/cancel/timeout, source retention, revision and
  finalization guards, replay and rollback.
- Six tests passed against the dedicated PostgreSQL 16.14 database, including
  concurrent enqueue/claim, lease fencing, corrupt-head isolation, attempt limits,
  evidence immutability and adoption commit/rollback. The database has 26 applied
  migrations; repeating migration leaves the applied set unchanged.
- 42 frontend tests passed across the processing panel/editors, target paths and
  workspace data. Local TypeScript/ESLint and actual-controller OpenAPI generation
  are checked separately.
- Regression failures were observed before fixing indirect navigation objects,
  remote-fragment disagreement, unselected navigation/annotation loss, annotation
  identity changes, same-page comparison and poisoned queue head handling.
  Read-only review findings were fixed and re-reviewed.

Current run evidence is under
`.artifacts/current-head-review-20261104/p506-final-review-20261105-2/`.
These are scoped checks, not a full repository or browser qualification. Large-file
and deployment qualification, rename impact execution, OCR/conformance processing
and combined workflow qualification remain P5-07–09 work. Regional/independent
validator qualification remains P6 work; no formal submission readiness is asserted.
