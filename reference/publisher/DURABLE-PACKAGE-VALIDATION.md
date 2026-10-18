# Durable independent validation runs (P2-06)

The independent engine is exposed through authenticated API endpoints and its own
PostgreSQL queue. It does not use PublishJob status to represent package findings
and does not require business application, document or placement rows. An explicit
application UUID groups external sequences and is part of every input digest.

## Inputs and requests

| Endpoint | Behavior |
| --- | --- |
| `POST /api/package-validation-inputs` | Register a directory or ZIP within configured `Security:AllowedWorkspaceRoots`. Accept application identity, path and optional explicit sequence/root selection. |
| `POST /api/package-validation-inputs/upload` | Stream multipart ZIP bytes (`file`, `applicationId`, optional `sequenceNumber`/`rootRelativePath`) to server-owned input storage, subject to upload and capture limits. |
| `GET /api/package-validation-inputs/{id}` | Public identity, digest, size/count and resource-limit digest; no server storage path. |
| `GET /api/package-validation-inputs/{id}/manifest` | Captured logical file/directory inventory, hashes and surrounding content. |
| `GET /api/package-validation-inputs/{id}/file?logicalPath=…&surrounding=…` | Exact manifest-member bytes only; never resolves caller paths against disk. Returns `application/octet-stream` with `nosniff` and `no-store`. |
| `GET /api/package-validation-inputs/{id}/text?logicalPath=…&line=…&surrounding=…` | Escaped, bounded text window for `.xml/.xsl/.txt/.dtd/.mod` only; never executes or fetches delivered XML/XSL. |
| `GET /api/package-validations/profiles` | Explicit development profile scopes and qualification status. |
| `POST /api/package-validations` | Enqueue input/profile/mode/idempotency key plus optional comparison input and historical input/profile selections; return 202 and the stable run identity. |
| `GET /api/package-validations` | Paginated run summaries with optional application filter; no report or request-manifest payloads are loaded for this query. |
| `GET /api/package-validations/{id}` | State, phase, attempts, cancellation request, exact binding and persisted coverage counts. |
| `POST /api/package-validations/{id}/cancel` | Cancel queued work immediately or request/acknowledge running cancellation. Terminal states are unchanged. |
| `GET /api/package-validations/{id}/report` | Versioned full report, digest-verified against its run; 409 while active, 410 for a terminal run without a completed report. |
| `GET /api/package-validations/{id}/findings` | Stable ordinal pagination, filtered by rule, severity, check status, exact logical path, archived rule category and node-path substring. Limits are 1–500; offsets must be nonnegative. |
| `GET /api/package-validations/{id}/evidence` | Archived request, exact rule catalog, explicit history manifest and configured read limits. Input IDs resolve to their independent manifests. |

All endpoints use the existing API-key authentication boundary. New validation
state enums use named strings in both HTTP and archived JSON. Existing API enum
contracts are unchanged. OpenAPI and generated TypeScript contracts are updated;
the coverage summary is explicitly nullable for pending and legacy runs.

Registration captures facts but does not declare a package compliant. Multiple
candidate roots return an explicit selection response. Every execution attempt
rereads all registered sources, reauthorizes server paths, verifies registration
digests and creates new owned temporary captures. Changed/disappeared inputs cannot
produce a completed report. Registered directories remain mutable source references;
uploaded ZIP bytes remain in server-owned storage. Failed uploads are removed only
before registration could have committed; ambiguous database outcomes retain bytes.
The persisted manifest's `capture` metadata retains the original ZIP SHA-256,
source kind, combined input digest and limits digest for later comparison evidence.

Requests cannot assert trusted/finalized history. External historical registrations
remain `ImportedExternal` / `Unverified`. A future trusted-input path must verify
persisted evidence rather than accept caller-provided trust enums. The full history
manifest, including trust and exact source identities, binds each run.

## Persistence and concurrency

`package_validation_inputs`, `package_validation_runs`,
`package_validation_reports` and `package_validation_findings` are independent of
business workspace deletion. Reports are database archives, not files under a
publish work directory. The terminal run, report SHA-256, coverage summary and
finding rows commit in one transaction. Full report reads verify the stored digest
and binding. Findings use database filtering/pagination; history lists select only
summary columns. Existing records without a coverage summary retain null rather
than fabricated counts.

The unique idempotency key compares a canonical digest of application, sequence,
the complete validation binding and the full stored request. Exact retries return
the same run; changed input IDs/digests, history, mode, limits, rules or engine
produce a conflict. A new run after changed rules/engine requires a new key. Queued
runs are rejected if the current engine/catalog no longer matches their binding.

Worker ownership has an expiring token and revision fencing. Heartbeats and state
changes use optimistic concurrency. Final archival locks the run row so heartbeat
writes cannot repeatedly invalidate a large findings transaction. Cancellation and
completion serialize at this boundary: a committed cancellation prevents a report;
a completed report remains completed. Expired tokens cannot renew or write results.

Expired running leases requeue fresh attempts up to `MaxAttempts`, then fail with a
queryable code. Requested cancellation is terminal during recovery. Host shutdown
leaves an interrupted lease recoverable; ordinary input/version/execution failures
are queryable terminal states. A successful execution can contain failing rules;
Completed is separate from readiness, and all current profiles remain unqualified.

Migrations `20261002235629_AddIndependentPackageValidation`,
`20261003003246_AddPackageValidationCoverageSummary`,
`20261011023436_AddPackageFindingNodePath`, `20261018025650_AddPackageExternalComparison`
and `20261018042240_AddPackageValidationProjection` add the storage, summary projection,
indexable finding NodePath, append-only external comparison evidence and the archived
cumulative dossier projection. The NodePath migration backfills existing finding rows
from their archived JSON `location.nodePath`; it never invents a node for rule-level
findings. The projection is stored as the exact canonical JSON produced by the
completion commit, hash-verified on read, and bound to the report's input manifest and
history baseline digests; it is derived evidence, never a mutable current-state table.
Each downgrade is refused with SQLSTATE 55000 when it would remove committed evidence.
Run the independent database migrator before API startup. The InMemory provider
follows the same behavioral contract for development/tests; it is not durable storage.

## Configuration and execution limits

`PackageValidation` configures `Enabled`, `StorageRoot` (default
`App_Data/package-validation`), `PollInterval` (1 second), `HeartbeatInterval`
(2 seconds), `LeaseDuration` (30 seconds), `ExecutionTimeout` (10 minutes),
`MaxAttempts` (3) and the complete `ReadLimits` object. API request-size limits
also apply to uploads. Capture limits and the engine's overall delivery deadline
remain independently enforced and archived.

The monitor records timeout/cancellation independently of cooperative parser return.
Captures remain alive until their reader exits; another parser is not admitted by
that worker while the previous parser is still running. PdfPig is in-process and
does not have hard CPU/decompression/memory preemption. A parser that never returns
can delay subsequent work despite its run already being terminal; process isolation
remains an explicit operational limitation, not a qualified safety guarantee.

`PublishExecutionReportDto.PackageValidationRunId` is an optional reference. Old
reports lacking it deserialize as null and do not imply independent validation.
Automatic publication/finalization binding is later roadmap work; the review UI
must show an explicit "not bound" state until it is implemented, never a dead link.

## Review UI (P2-07)

`/package-validations` provides input registration (upload, authorized server path,
or existing input ID), explicit target/comparison/history selection with per-history
profiles, idempotent run creation, run history and findings review. Findings expose
the archived explicit file addresses (`ValidationFileAddress`) so the UI can open the
exact input/address space; legacy or ambiguous locations deliberately show no file
rather than guessing a same-named file. The engine versions in this change are
`package-delivery-v3` and `package-file-inspector-v2`: queued runs bound to older
versions fail with `VALIDATION_VERSION_CHANGED` and need a new run key.

A completed run also archives its derived cumulative dossier projection. `GET
/api/package-validations/{id}/projection` renders one explicit view (`history`,
`current` or `sequence`) and pages leaves; the complete archived document remains the
single source of truth, so no view is recomputed from the workspace. A projection
whose events could not all be resolved is archived as incomplete with its reasons and
retains unresolved events instead of presenting them as effective. External comparison
evidence is append-only: it binds the run's exact package digest, keeps the original
report text, requires an explanation per difference, and treats an exact replay as
idempotent while rejecting a different conclusion for the same run.

## Verification scope

Domain/store tests cover exact idempotency, expiry, retries, stale tokens, cancellation,
report binding and pagination. Real PostgreSQL tests exercise concurrent repository
instances, completion/cancellation races, restart recovery, atomic archives and
corruption detection without business application rows. Service/API tests use the
hand-authored external package and real ZIP bytes, selected history, source changes,
path boundaries, failed upload cleanup, timeout/cancellation, HTTP authentication,
report access and legacy report compatibility. A non-cooperative parser regression
checks that cancellation becomes queryable while captures remain alive until the
parser exits. Shutdown/recovery tests verify a new attempt rereads changed sources.
Migration tests exercise empty/old database upgrades, repeat invocation, preservation
of an older archived report with a null coverage summary, and downgrade refusal for
registered input or run evidence. API tests cover authentication, exact file/text
scope, traversal rejection, download headers, category/node filtering, paging bounds,
input-change conflicts, projection views/paging and external evidence register/replay.
Projection tests exercise replace/append/delete chains, a missing baseline, a combined
target+comparison digest, deterministic replay and change attribution. Engine-version
changes are tested as a terminal `VALIDATION_VERSION_CHANGED` result, and
`PackageValidation:Enabled=false` is tested to leave queued runs visibly Pending.
Windows evidence does not substitute for Linux, external-validator or regional
qualification.
