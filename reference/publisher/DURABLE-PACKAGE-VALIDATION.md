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
| `GET /api/package-validations/profiles` | Explicit development profile scopes and qualification status. |
| `POST /api/package-validations` | Enqueue input/profile/mode/idempotency key plus optional comparison input and historical input/profile selections; return 202 and the stable run identity. |
| `GET /api/package-validations` | Paginated run summaries with optional application filter; no report or request-manifest payloads are loaded for this query. |
| `GET /api/package-validations/{id}` | State, phase, attempts, cancellation request, exact binding and persisted coverage counts. |
| `POST /api/package-validations/{id}/cancel` | Cancel queued work immediately or request/acknowledge running cancellation. Terminal states are unchanged. |
| `GET /api/package-validations/{id}/report` | Versioned full report, digest-verified against its run; 409 while active, 410 for a terminal run without a completed report. |
| `GET /api/package-validations/{id}/findings` | Stable ordinal pagination, filtered by rule, severity, check status and exact logical path. Limits are 1–500. |
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

Migrations `20261002235629_AddIndependentPackageValidation` and
`20261003003246_AddPackageValidationCoverageSummary` add the storage and summary
projection. Run the independent database migrator before API startup. Downgrade is
refused with SQLSTATE 55000 when it would remove registered input or run evidence.
The InMemory provider follows the same behavioral contract for development/tests;
it is not durable storage.

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
Automatic publication/finalization binding is later roadmap work.

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
registered input or run evidence. Windows evidence does not substitute for Linux,
external-validator or regional qualification.
