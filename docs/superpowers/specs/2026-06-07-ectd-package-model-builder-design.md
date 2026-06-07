# eCTD Package Model Builder Design

## Goal

Add an application-layer package model builder that converts RATools application, sequence, document, placement, standards, and FDA publishing metadata into deterministic eCTD package facts.

This is the third implementation step in the FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3 compliance roadmap. It prepares a stable input model for future ICH `index.xml` generation, US Regional `us-regional.xml` generation, and compliance validation without changing current publish output behavior.

## Scope

In scope:

- Build an `EctdSequencePackage` for a single application sequence.
- Include standards profile metadata from `IStandardsProfileProvider`.
- Include application metadata and sequence metadata.
- Use `SequencePublishingMetadata` when present, with deterministic fallback to application and sequence values.
- Convert current sequence placements into package leaves.
- Separate M1 leaves from M2-M5 leaves.
- Resolve published document href values using `PublishOutputNaming`.
- Resolve lifecycle target href values for `replace`, `delete`, and `append`.
- Surface clear errors for missing application, missing sequence, missing document, unsupported operation, and invalid lifecycle targets.
- Add focused unit tests for model construction and error cases.

Out of scope:

- Generating `index.xml`.
- Generating `m1/us/us-regional.xml`.
- Writing files, copying assets, calculating package checksums, or creating zip archives.
- Replacing current `BackboneService` behavior.
- Frontend changes.
- Full FDA validation rules. The package model should expose facts for validation, not perform all validation itself.

## Current Inputs

The builder should use existing repositories and models:

- `IApplicationRepository`
- `IDocumentPlacementRepository`
- `IDocumentRepository`
- `IStandardsProfileProvider`
- `SubmissionApplication`
- `SubmissionSequence`
- `SequencePublishingMetadata`
- `DocumentPlacement`
- `SubmissionDocument`
- `PublishOutputNaming`

This keeps the builder independent from controllers, EF Core, local file writing, and XML writing.

## New Module

Create a new namespace under:

`RATools.Application.Publishing.PackageModel`

Primary service:

- `IEctdPackageModelBuilder`
- `EctdPackageModelBuilder`

Request:

- `BuildEctdPackageRequest(Guid ApplicationId, string SequenceNumber)`

The service should be registered in `AddApplication()` as scoped, because it depends on scoped repositories.

## Package Records

Use immutable record types for the package facts.

### EctdSequencePackage

Fields:

- `Guid ApplicationId`
- `string ApplicationNumber`
- `string SequenceNumber`
- `string StandardsProfile`
- `string IchEctdVersion`
- `string UsRegionalModule1Version`
- `EctdApplicationMetadata Application`
- `EctdSequenceMetadata Sequence`
- `IReadOnlyCollection<EctdLeaf> Module1Leaves`
- `IReadOnlyCollection<EctdLeaf> IchBackboneLeaves`
- `IReadOnlyCollection<EctdPublishedFile> PublishedFiles`

### EctdApplicationMetadata

Fields:

- `string ApplicationNumber`
- `string SponsorName`
- `string Region`
- `string TemplateKey`
- `string? ApplicationType`

### EctdSequenceMetadata

Fields:

- `string SequenceNumber`
- `string SubmissionType`
- `string? SubmissionSubtype`
- `string Description`
- `string ApplicantName`
- `string? FormType`

### EctdLeaf

Fields:

- `Guid PlacementId`
- `Guid DocumentId`
- `string LeafId`
- `string SequenceNumber`
- `string CtdSection`
- `string Module`
- `string Operation`
- `string Title`
- `string Href`
- `string FileName`
- `string MediaType`
- `string SourcePath`
- `long FileSize`
- `string Sha256`
- `EctdLifecycleReference? Lifecycle`

Rules:

- `LeafId` is `leaf-{placementId:N}`.
- `Operation` is lower-case: `new`, `replace`, `delete`, or `append`.
- `Title` uses placement title when present; otherwise the document file name.
- `Href` is `PublishOutputNaming.BuildPublishedDocumentRelativePath(document, placement.SequenceNumber)`.
- `Module` is derived from the first CTD section segment, lower-case.

### EctdLifecycleReference

Fields:

- `Guid TargetPlacementId`
- `Guid TargetDocumentId`
- `string TargetSequenceNumber`
- `string ModifiedFileHref`

Rules:

- Only `replace`, `delete`, and `append` operations require lifecycle references.
- Target placement must exist.
- Target placement must belong to the same application.
- Target placement must be in the same CTD section.
- Target sequence number must be earlier than the current placement sequence number.
- Target document must exist.
- `ModifiedFileHref` is built from the target document and target placement sequence number.

### EctdPublishedFile

Fields:

- `Guid DocumentId`
- `string SourcePath`
- `string Href`
- `string FileName`
- `long FileSize`
- `string Sha256`

Rules:

- Include each referenced current-sequence document once.
- Sort deterministically by `Href`, case-insensitive.

## Data Flow

`EctdPackageModelBuilder.BuildAsync()` should:

1. Load the application.
2. Return a not-found style exception if the application does not exist.
3. Resolve the sequence from `application.Sequences`.
4. Return a not-found style exception if the sequence does not exist.
5. Resolve the standards profile from `application.EctdTemplateKey`.
6. Load current sequence placements.
7. Load application placements for lifecycle resolution.
8. Load documents and create a document lookup.
9. Convert application and sequence metadata.
10. Convert placements to leaves in deterministic order.
11. Split leaves into Module 1 and ICH M2-M5 collections.
12. Build the published file inventory from current sequence leaves.
13. Return `EctdSequencePackage`.

Deterministic ordering:

- Leaves sort by `CtdSection`, then `CreatedUtc`, then `PlacementId`.
- Module collections preserve that sorted order.
- Published files sort by `Href`.

## Error Handling

Add package-model-specific exceptions:

- `EctdPackageApplicationNotFoundException`
- `EctdPackageSequenceNotFoundException`
- `EctdPackageDocumentNotFoundException`
- `EctdPackageUnsupportedOperationException`
- `EctdPackageInvalidSectionException`
- `EctdPackageLifecycleTargetException`

These exceptions should include enough context for later conversion into compliance findings:

- application id
- sequence number
- placement id when applicable
- document id when applicable
- target placement id when applicable

The builder should not silently skip invalid placements or missing documents. Missing data would produce an incomplete package model, so it must fail fast.

## Module Classification

Classify leaves by the first segment of `CtdSection`.

- `m1`: Module 1 collection.
- `m2`, `m3`, `m4`, `m5`: ICH backbone collection.
- anything else: fail with `EctdPackageInvalidSectionException`.

## Integration Boundary

This feature should not change `BackboneService` yet.

Future tasks will:

- update `BackboneService` to delegate to `IEctdPackageModelBuilder`;
- implement an ICH XML writer consuming `EctdSequencePackage.IchBackboneLeaves`;
- implement a US regional XML writer consuming `EctdSequencePackage.Module1Leaves` and `EctdSequenceMetadata`;
- implement compliance validation from the same package facts.

Keeping the builder unused by publish flow initially is acceptable because its value is to provide a tested package contract for the next step.

## Tests

Add tests under:

`tests/RATools.Tests/Publishing/PackageModel`

Required coverage:

- Builds package metadata using standards profile and default sequence metadata.
- Uses stored `SequencePublishingMetadata` when present.
- Creates deterministic leaves for new placements.
- Splits M1 leaves from M2-M5 leaves.
- Creates published file inventory without duplicates.
- Resolves lifecycle `modified-file` href for replace operations.
- Throws when application is missing.
- Throws when sequence is missing.
- Throws when a placement document is missing.
- Throws when lifecycle target is missing or invalid.
- Throws when CTD section module is unsupported.

Tests should use in-memory stub repositories rather than EF Core. EF persistence for `SequencePublishingMetadata` is already covered by the previous batch.

## Acceptance Criteria

The package model builder is complete when:

- `IEctdPackageModelBuilder` is registered in application DI.
- `EctdPackageModelBuilder.BuildAsync()` can build a deterministic `EctdSequencePackage`.
- Package metadata includes FDA standards baseline values.
- Sequence metadata reflects `SequencePublishingMetadata` overrides and fallback defaults.
- Leaves expose operation, title, href, source path, media type, size, SHA-256, and lifecycle facts.
- M1 leaves and M2-M5 leaves are separated.
- Lifecycle target hrefs are resolved for supported lifecycle operations.
- Invalid inputs fail with package-model-specific exceptions.
- All package model tests pass.
- Existing backend and frontend test suites still pass.
