# Real eCTD Package Publisher Design

## Goal

Replace the prototype publish output path with a real FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3 sequence package publisher.

The first delivery scope is a single sequence package that contains the ICH backbone `index.xml`, US Regional Module 1 `m1/us/us-regional.xml`, referenced document files, bundled DTD assets under `util/dtd`, `index-md5.txt`, and a zip containing the same delivery tree.

## Current State

The application layer now has these real publishing pieces:

- `EctdPackageModelBuilder` builds an `EctdSequencePackage` from applications, sequences, placements, documents, lifecycle targets, and standards metadata.
- `IchIndexXmlWriter` generates ICH eCTD v3.2.2 `index.xml` for Modules 2 through 5.
- `UsRegionalXmlWriter` generates US Regional M1 v3.3 `m1/us/us-regional.xml` for Module 1.

The existing `BackboneService` still creates a prototype XML document in `http://example.org/ectd/backbone`. The infrastructure writer accepts one XML file plus `SubmissionDocument` objects, so it cannot write both XML backbones or copy standards assets as a package-level concern.

`PublishJobService.WriteFinalReportAsync` also recreates the package zip from the report directory after integrity verification. That can replace a valid delivery zip with an artifact-only zip. A real eCTD package must keep the delivery zip intact.

## Scope

In scope:

- Keep the public publish job API shape stable.
- Keep `IBackboneService.GenerateAsync` as the orchestration boundary used by `PublishJobService`.
- Change `BackboneService` internals to build an `EctdSequencePackage`, generate both XML documents, and ask the file writer to persist a complete delivery tree.
- Change `IBackboneFileWriter` so it writes multiple generated files and `EctdPublishedFile` entries.
- Update `LocalBackboneFileWriter` to copy documents, write generated XML files, copy bundled DTD files into `util/dtd`, create `index-md5.txt`, and create the package zip from the delivery tree.
- Update `PublishOutputVerifier` to recognize the local DTD-compatible xlink namespace `http://www.w3c.org/1999/xlink` as well as the common `http://www.w3.org/1999/xlink`.
- Stop `PublishJobService` from recreating the package zip from the report directory.
- Add focused backend tests for the writer, orchestrator, verifier, and publish finalization behavior.

Out of scope for this batch:

- Full DTD validation.
- UI/API fields for all US Regional admin metadata.
- Supporting `m1.15` promotional material attribute-heavy structures.
- Supporting non-FDA regions.
- Renaming externally visible artifact role `BackboneXml`; existing UI and reports can keep that label while the contents become real ICH `index.xml`.

## Architecture

`BackboneService` becomes a thin application-layer orchestrator:

1. Build `EctdSequencePackage` with `IEctdPackageModelBuilder`.
2. Generate `index.xml` with `IIchIndexXmlWriter`.
3. Generate `m1/us/us-regional.xml` with `IUsRegionalXmlWriter`.
4. Pass generated files and package `PublishedFiles` to `IBackboneFileWriter`.
5. Return `GeneratedBackboneDto` using the `index.xml` path and content as the primary output artifact.

`LocalBackboneFileWriter` owns filesystem layout:

- Delivery root: `{outputRoot}/{applicationNumber}/_jobs/{jobId}/{sequenceNumber}`.
- Report path: `{outputRoot}/{applicationNumber}/_artifacts/{sequenceNumber}/{jobId}/publish-report-...json`.
- Package path: `{outputRoot}/{applicationNumber}/_packages/{sequenceNumber}/{sequenceNumber}-{jobId}.zip`.
- Generated XML files are written by relative path.
- Published documents are copied to their package href paths.
- Bundled DTD files are copied from `AppContext.BaseDirectory/reference/dtd` to `util/dtd`.
- `index-md5.txt` is computed after documents, XML, and DTDs are present and excludes itself.
- The package zip is created from the delivery root only.

## Failure Behavior

Publishing fails fast if:

- a generated file path is absolute or attempts directory traversal;
- a published document source path is missing;
- a standards DTD source directory is missing;
- generated XML writers reject metadata or section mapping.

Validation remains the first gate, but the publisher must not silently omit referenced files.

## Testing

The batch should add or update tests that prove:

- `LocalBackboneFileWriter` writes multiple generated files, documents, DTD assets, md5 manifest, and zip entries.
- `BackboneService` calls the package builder and both XML writers, then passes all generated outputs to the file writer.
- `PublishOutputVerifier` sees references in the DTD-compatible `http://www.w3c.org/1999/xlink` namespace.
- `PublishJobService` leaves the package zip as a delivery zip after writing the final report.
- The focused publishing tests and full backend test suite pass.
