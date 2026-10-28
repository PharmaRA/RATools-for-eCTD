# Changelog

All notable changes to RATools for eCTD are documented in this file. Versions use
Semantic Versioning, and release dates use ISO 8601.

## [Unreleased]

### Added

- Independent package validation UI (`/package-validations`) with input registration
  (upload, authorized server path, existing input), explicit target/comparison/history
  selection, idempotent runs, progress/cancel, findings filtering, file location and
  report/evidence download.
- Exact captured-input file and text preview endpoints (`/file`, `/text`) restricted to
  manifest members; surrounding-file scope is explicit.
- Finding filters by archived rule category and node-path substring, backed by the
  `20261011023436_AddPackageFindingNodePath` migration and its JSON backfill.
- `ZIP-LIMITS` per-run coverage observation (NotApplicable when no source is a ZIP).
- Append-only external comparison evidence per validation run (`/external-reports`,
  `/external-report`, `/external-report/text`) bound to the run's exact package digest,
  with required per-difference explanations and idempotent exact replay.
- Archived cumulative dossier projection with `history`, `current` and `sequence`
  views, bounded paging and explicit incompleteness instead of inferred effectiveness.
- Publish jobs can reference a completed validation run; the reference is verified
  (completed, same application/sequence, readable archived report) and never inferred.
- Qualification matrix executing every shipped invalid-package fixture case.
- Immutable finalized-sequence snapshots bound to the exact validation run, manifest,
  history baseline and standards profile, with append-only delivery records and
  receipts that keep transmission, technical and business outcomes separate.
- Preview-first bulk assembly from a bounded RFC 4180 CSV: every row is resolved
  against an explicit workspace revision with full diagnostics, and execution
  requires the previewed plan digest.
- Finalized write boundary (P4-07): a sequence with a finalized snapshot rejects every
  draft write with `409 WorkspaceFinalized`, reports the state on the workspace snapshot,
  and becomes editable only through `POST /api/finalized-sequences/{id}/draft-revisions`,
  an idempotent continuation that is unique per snapshot and records the source revision.
  Sequences and applications with finalized evidence refuse draft-path deletion.
- Immutable release capture (P4-02/P4-03): finalization copies the deliverable into
  `_releases/<sequence>/<manifest-digest>/` with an independent payload, the pinned
  standards assets, the archived report and a delivery package, and records a manifest
  digest it derives from those bytes instead of accepting one from the caller. Each
  delivered document is verified against the validation run's captured input, each copy
  is re-hashed while it is written, and `GET /api/finalizations/{id}/manifest` re-verifies
  the stored manifest against the snapshot.
- Finalized history binding (P3-07): a selected historical input becomes a
  `HistorySourceKind.Finalized` baseline entry with its snapshot and validation-run
  identity only when a finalized snapshot recorded exactly those bytes; nothing the
  caller sends can assert trust. Each inspection re-verifies that evidence, so a lost or
  replaced snapshot fails the run instead of silently downgrading verified history.

### Changed

- Delivery/file inspector engine versions advanced to `package-delivery-v3` and
  `package-file-inspector-v2`; queued runs bound to older versions fail with
  `VALIDATION_VERSION_CHANGED` and require a new run key.
- Package-validation deadline failures return `408`; size/count limits return `413`.
- Startup validation now requires `PackageValidation:StorageRoot` to be inside an
  allowed workspace root; expiry recovery in the validation worker runs on a coarse
  cadence instead of every poll.

### Fixed

- ZIP-only rules no longer leave an unexplained required `NotEvaluated` gap when a ZIP
  was supplied.
- A failed original-file download no longer hides an already verified text preview, and
  closing the input dialog now aborts an in-flight registration.
- The PostgreSQL import regression restores the shared test schema after an intentional
  downgrade, so later tests no longer see a spuriously pending migration.

## [0.1.0] - 2026-08-19

### Added

- Local-only production images and a loopback Caddy/PostgreSQL/Prometheus topology.
- Independent, repeatable database migrations before API startup.
- Liveness, readiness, publish metrics, alert rules, and structured production logs.
- Atomic database and publish-data backups with isolated automated restore drills.
- FDA eCTD publishing, lifecycle validation, package review, audit history, and
  authenticated document/workspace operations established before this baseline.

### Security

- File-backed runtime secrets, non-root containers, read-only root filesystems,
  security headers, bounded uploads, and guarded destructive workspace operations.
- Path containment and link/reparse-point defenses across workspace, upload, import,
  publish, download, and deletion flows.

### Operations

- Versioned assemblies and OCI image metadata tied to source revision and build time.
- CycloneDX source and runtime-image SBOM generation in CI.

[Unreleased]: https://github.com/PharmaRA/RATools-for-eCTD/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/PharmaRA/RATools-for-eCTD/releases/tag/v0.1.0
