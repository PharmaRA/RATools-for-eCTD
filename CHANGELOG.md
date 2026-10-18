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
