# Changelog

All notable changes to RATools for eCTD are documented in this file. Versions use
Semantic Versioning, and release dates use ISO 8601.

## [Unreleased]

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
