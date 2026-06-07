# FDA eCTD 3.2.2 Compliance Delivery Design

## Goal

Move RATools-for-eCTD from a functional eCTD publishing prototype to a first production-oriented compliance baseline for FDA CDER/CBER submissions using eCTD v3.2.2 and US Regional Module 1 v3.3.

The first compliance release must generate a complete, verifiable sequence package suitable for pre-submission review. It must not claim FDA gateway acceptance or replace external regulatory validation tools, but it must align the project architecture, generated artifacts, validation model, and UI workflow with the official FDA/ICH standard set.

## Regulatory Scope

In scope:

- FDA CDER and CBER human drug and biologic eCTD submissions.
- eCTD v3.2.2 for Modules 2 through 5.
- US Regional Module 1 v3.3.
- The existing project template key `us-fda-ectd-3.2.2`.
- Sequence package generation, local verification, package review, and artifact download.
- Local validation based on bundled standards assets and rules derived from FDA/ICH published requirements.

Out of scope for this release:

- eCTD v4.0.
- EU, JP, CA, AU, or other regional Module 1 implementations.
- ESG gateway submission, account testing, acknowledgement processing, or FDA receipt reconciliation.
- Study data standard validation beyond package/file-level eCTD checks.
- Commercial-grade PDF content validation beyond file extension, path, size, and checksum checks unless a local parser is already available.
- A guarantee that FDA will accept every generated package. The product provides deterministic package generation and transparent local evidence, not FDA infrastructure certification.

## Official Standards Baseline

The implementation should treat the FDA standards page as the source of truth for supported files and versions. The first baseline is:

- eCTD Technical Conformance Guide: version 1.9, supported by FDA from 2024-09-09.
- eCTD Backbone File Specification for Modules 2 through 5: version 3.2.2.
- Study Tagging File specification: version 2.6.1, tracked as a future package extension.
- Specifications for eCTD Validation Criteria: version 4.5, supported by FDA from 2025-10-20.
- US Regional Module 1 specification: version 3.3.
- US Regional DTD: `us-regional-v3-3.dtd`.
- ICH eCTD DTD: `ich-ectd-3-2.dtd`.
- FDA valid-values and other M1 supportive files must be added to the standards inventory before M1 metadata validation can be considered complete.

The repository already includes:

- `reference/dtd/ich-ectd-3-2.dtd`
- `reference/dtd/us-regional-v3-3.dtd`

These files should become managed standards assets with provenance metadata, not anonymous static files.

## Current State

The current backend already has useful building blocks:

- `SubmissionApplication` and `SubmissionSequence` capture application and sequence identity.
- `SubmissionDocument` records file name, media type, size, SHA-256, and storage path.
- `DocumentPlacement` records CTD section, operation, title, and lifecycle target placement.
- `SequenceValidationService` validates business readiness, section matching, lifecycle targets, duplicate output paths, and file existence.
- `PublishJobService` coordinates validation, backbone generation, artifact verification, final report writing, and audit logging.
- `BackboneService` currently emits a simplified XML document and package artifacts.
- `PublishOutputVerifier` already collects integrity evidence for generated outputs.
- Frontend publish flow already has a validation-first gate and package review panels.

The main gap is that the generated backbone and package structure are not a real FDA eCTD v3.2.2 + US M1 v3.3 package. `BackboneService` uses an example namespace and custom XML shape, so it must be replaced by standards-aware package generation rather than incrementally expanded.

## Architecture

### Standards Assets

Add a standards asset layer responsible for locating, versioning, and describing bundled regulatory assets.

Responsibilities:

- Expose a `StandardsProfile` for `us-fda-ectd-3.2.2`.
- Record source URLs, version labels, supported dates, local file paths, and checksum values for each bundled asset.
- Resolve DTD and supportive files needed during package generation.
- Give validation and publish reports enough metadata to state exactly which standards baseline was used.

Expected concepts:

- `StandardsProfile`
- `StandardsAsset`
- `IStandardsProfileProvider`
- `FdaEctd322StandardsProfileProvider`

### Package Model

Create an internal package model between application data and XML/file output. This prevents XML generation from directly querying repositories and spreading eCTD rules across services.

Responsibilities:

- Convert application, sequence, documents, and placements into a deterministic `EctdSequencePackage`.
- Assign stable leaf IDs.
- Resolve published hrefs.
- Resolve lifecycle `modified-file` targets.
- Separate ICH M2-M5 content from US M1 content.
- Produce validation-friendly package facts before files are written.

Expected concepts:

- `EctdSequencePackage`
- `EctdApplicationMetadata`
- `EctdSequenceMetadata`
- `EctdLeaf`
- `EctdLifecycleReference`
- `EctdPackageSection`
- `EctdPublishedFile`

### Backbone Generation

Replace the simplified `BackboneService` implementation with standards-aware generators.

Responsibilities:

- Generate sequence-level `index.xml` for ICH eCTD v3.2.2.
- Generate `m1/us/us-regional.xml` for US Regional M1 v3.3.
- Emit correct DTD references and namespace declarations.
- Write referenced documents into their published package locations.
- Write `index-md5.txt`.
- Copy required DTD/style/supportive files into `util` folders where required by the selected standards profile.
- Preserve deterministic output paths so validation, package review, and artifact download agree.

The current `IBackboneService` contract can remain as the public application service boundary, but the implementation should delegate to smaller units:

- package builder
- ICH backbone XML writer
- US regional XML writer
- package file writer
- compliance verifier

### Compliance Validation

Add a compliance validation layer distinct from the existing business validation service.

Business validation answers: "Is the workspace data internally publishable?"

Compliance validation answers: "Does the generated package model or package output follow the selected FDA/ICH standard baseline?"

Pre-write checks:

- Application and sequence metadata completeness.
- Required M1 metadata completeness.
- CTD section path is valid for the selected profile.
- Leaf operation is supported.
- Lifecycle operations have valid historical targets.
- Published hrefs are unique and legal.
- File names, extensions, path segments, and path lengths are legal.
- Source files exist and are readable.

Post-write checks:

- Required package files exist.
- `index.xml` and `us-regional.xml` parse as XML.
- DTD validation succeeds for files where local DTD validation is supported.
- MD5 checksum values match package contents.
- `index-md5.txt` matches `index.xml`.
- Zip contents match package directory contents.
- Package report and artifact inventory match files on disk.

Validation results should be structured as:

- severity: `Error`, `Warning`, or `Info`
- code
- message
- standard asset or rule source
- affected package path
- affected placement/document id when applicable
- phase: `PreWrite`, `XmlGeneration`, `PostWrite`, or `PackageArchive`

### Metadata Model

The existing domain model should not absorb every US M1 metadata field.

Add a publishing metadata model for FDA-specific submission metadata. It should be associated with application and sequence identity while remaining separate from generic document placement.

Initial metadata needed:

- application type
- submission type
- submission subtype where applicable
- sequence description
- sponsor/applicant display value
- regional M1 form/document classification
- sequence-level FDA regional values required by `us-regional.xml`

The first implementation may store this metadata as a structured owned record or JSON payload if the field set is still evolving, but it must expose typed DTOs at the service/API boundary. Avoid unstructured frontend-only metadata.

### Publish Flow

`PublishJobService` remains the orchestrator.

The compliant flow:

1. Ensure no active publish job exists for the application/sequence.
2. Run existing business validation.
3. Build `EctdSequencePackage`.
4. Run pre-write compliance validation.
5. Stop on any `Error`.
6. Generate package directory.
7. Run post-write compliance validation.
8. Stop on any `Error`, preserving diagnostic artifacts when possible.
9. Create zip archive.
10. Verify archive and artifact integrity.
11. Write final report.
12. Mark job completed and write audit logs.

Warnings do not block package generation by default, but they must remain visible in the final report and frontend package review.

### Frontend Workflow

The frontend should evolve the existing publish gate into a compliance publish workflow.

Views:

- Metadata completeness panel.
- Pre-publish business validation summary.
- Compliance validation summary.
- Package preview/review.
- Artifact integrity evidence.
- Publish history with standards baseline.

Rules:

- Any `Error` blocks final publish.
- Warnings are visible and countable.
- Users can locate document/placement-linked issues in the workspace where possible.
- Publish history must display standards profile name and version baseline.

The first UI pass should reuse `SequenceWorkspacePage`, `ReportPanel`, `PackageReviewPanel`, `ArtifactsPanel`, and `PublishHistoryTab` rather than introducing a new route.

## API Impact

Expected additions:

- Endpoint to read or update FDA sequence publishing metadata.
- Endpoint or response section exposing compliance validation report.
- Publish execution response extended with standards baseline and compliance findings.
- Publish artifact inventory extended with package root, XML files, checksums, and standards assets.

Existing endpoints should remain compatible where possible:

- `POST /api/validation/sequence` continues to return business validation.
- `POST /api/publish-jobs/execute` remains the final publish entry point.
- `GET /api/publish-jobs/{id}/report` returns a richer report.
- `GET /api/publish-jobs/{id}/artifacts` returns a richer artifact list.

## Testing Strategy

Add golden sample coverage before replacing the current publish output behavior.

Golden samples:

- `0000` initial sequence with valid M2-M5 document leaves.
- `0000` sequence with M1 regional XML content.
- `0001` replace lifecycle operation targeting a historical leaf.
- `0002` delete lifecycle operation targeting a historical leaf.
- `0003` append lifecycle operation targeting a historical leaf.
- invalid section path.
- unsupported file extension.
- missing source file.
- checksum mismatch after package write.
- duplicate published href.
- broken lifecycle target.
- missing required M1 metadata.

Test layers:

- Unit tests for package model building.
- Unit tests for ICH XML writer.
- Unit tests for US regional XML writer.
- Unit tests for compliance validators.
- Integration tests for publish job execution.
- Artifact tests for zip contents and checksum files.
- Frontend tests for compliance gate display and blocking behavior.
- Smoke test extension for a minimal successful compliant package.

## Migration Strategy

Do not remove the existing publish history or DTO fields.

Recommended migration approach:

1. Add standards profile and package model behind existing publish flow.
2. Add compliance report DTO fields while preserving old fields.
3. Add metadata persistence and API contract.
4. Introduce compliant package generation behind the existing `us-fda-ectd-3.2.2` template.
5. Update frontend to render the richer report.
6. Update smoke test once compliant generation is the default.

If development needs a temporary fallback, expose it through explicit internal configuration such as `Publishing:UseLegacyBackboneGenerator`. The default for this project should move to compliant generation once golden tests pass.

## Risks and Controls

Risk: FDA standards assets change.

Control: version every bundled asset, show the baseline in reports, and keep standards updates as explicit code/data changes.

Risk: DTD validation behavior differs across platforms.

Control: wrap XML validation behind an interface and test it on Windows in CI/local development. If a DTD rule cannot be enforced locally, report it as an explicit unsupported local check instead of silently passing.

Risk: US M1 metadata is under-modeled.

Control: keep M1 metadata in a dedicated model and add required fields incrementally through golden samples and standards review.

Risk: Replacing `BackboneService` becomes too large.

Control: split package building, XML writing, file writing, and compliance validation into small services with independent tests.

Risk: The UI blocks users without clear remediation.

Control: every blocking finding should include code, message, affected path or workspace target, and remediation-oriented text.

## Acceptance Criteria

The first compliance release is acceptable when:

- A valid `us-fda-ectd-3.2.2` sequence produces a complete sequence directory and zip package.
- The package contains `index.xml`, `index-md5.txt`, `m1/us/us-regional.xml`, published documents, and required standards/support files for the selected baseline.
- XML files parse and pass all locally supported DTD checks.
- Business and compliance validation findings are reported separately and visibly.
- Publish blocks on compliance `Error` findings.
- Publish succeeds with warnings and records those warnings in the final report.
- Lifecycle `replace`, `delete`, and `append` operations produce valid historical references.
- Artifact integrity evidence includes file existence, size, checksum, and archive verification.
- Publish history displays the standards baseline used for each job.
- Backend and frontend tests cover successful and failing golden sample cases.

## Implementation Plan Outline

Implementation should be split into separate plans or plan tasks in this order:

1. Standards profile inventory and provenance metadata.
2. FDA publishing metadata model and API.
3. eCTD package model builder.
4. ICH eCTD v3.2.2 `index.xml` writer.
5. US Regional M1 v3.3 `us-regional.xml` writer.
6. Compliance validation service.
7. Package file writer and artifact inventory.
8. Publish job service integration.
9. Frontend compliance gate and metadata editing.
10. Golden sample and smoke test expansion.

Each step must preserve a running test suite and should be independently reviewable.
