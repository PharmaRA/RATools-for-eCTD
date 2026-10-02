# Draft workspace revisions

Every delivery-affecting edit to an existing sequence requires the revision of
the draft displayed to the operator. Uploads, placement creation/removal, file
moves, leaf metadata, publishing metadata and CTD node writes advance that
revision. Revisions are nonnegative JSON-safe integers; the maximum safe integer
is reserved as the terminal revision and cannot be used for another edit.

Clients read `GET /api/applications/{applicationId}/sequences/{sequenceNumber}/workspace`
to obtain one revision together with application placements and documents.
Application history is included for lifecycle target selection. The revision
belongs to the selected sequence. This is a draft read, not a finalized snapshot
or a validated historical baseline.

JSON edits send `expectedRevision`; sequence uploads send `ExpectedRevision` in
multipart form data; placement/document DELETE sends `expectedRevision` in the
query string. Missing revisions return HTTP 428 (`WorkspaceRevisionRequired`),
stale revisions HTTP 409 (`WorkspaceRevisionConflict`, including `currentRevision`),
invalid revisions HTTP 400 and missing workspaces HTTP 404. Error bodies include
`message`, `code`, `traceId` and `location`. Legacy clients must refresh and supply
a revision; there is no implicit overwrite mode.

PostgreSQL writers use a shared application advisory lock, held through file
operations, transaction commit and compensation. Draft snapshot reads use the
same lock. Node writes and legacy backfill participate in that lock. Database
changes and revision advancement commit together. In-memory persistence stages
repository writes until commit and discards them on failure or cancellation.

When a file move fails, successful compensation restores the prior content.
If restoring the file fails and changed state must be retained, compensation
advances the revision. An ambiguous commit may already have advanced it;
compensation never decrements or advances it twice. Cancelled uploads remove
their partial file without deleting pre-existing files.

The UI retains the displayed revision, clears validation/readiness when editing,
and refreshes after failures. It does not automatically replay conflicted edits.
Upload followed by attachment uses the revision returned by the upload; detach
followed by document deletion uses the next revision. These are separate requests:
a conflict between them stops the second step and requires user review.

Regression evidence covers real PostgreSQL writer competition, cancellation and
compensation, SQLite file-move fault injection, API 428/409 responses, in-memory
rollback, partial-upload cleanup and client request sequencing. Linux execution
is provided by CI and must be observed separately from local Windows results.
