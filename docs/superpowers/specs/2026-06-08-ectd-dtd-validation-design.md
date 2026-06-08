# eCTD DTD Validation Design

## Goal

Validate generated ICH `index.xml` and US Regional `m1/us/us-regional.xml` against the bundled FDA eCTD v3.2.2 / US Regional M1 v3.3 DTD assets before writing the delivery package.

This moves publishing from "real XML generation" to "real XML generation with a standards gate." If the generated XML does not satisfy the local DTDs, publishing should fail before the output writer creates the delivery zip.

## Scope

In scope:

- Add an application-layer `IEctdXmlValidator`.
- Validate generated XML files represented as `BackboneGeneratedFile`.
- Resolve only bundled local DTD files from `AppContext.BaseDirectory/reference/dtd`.
- Support ICH `index.xml` with system id `util/dtd/ich-ectd-3-2.dtd`.
- Support US Regional `m1/us/us-regional.xml` with system id `../../util/dtd/us-regional-v3-3.dtd`.
- Report validation failures with the package-relative XML path and XML parser message.
- Wire validation into `BackboneService` after both XML writers run and before `IBackboneFileWriter.SaveAsync`.
- Register the validator in application dependency injection.
- Add unit tests for success, invalid XML/DTD failure, unknown DTD blocking, and orchestration behavior.

Out of scope:

- Full FDA validation criteria checks beyond DTD validation.
- Schema validation for standards outside the current FDA baseline.
- Network resolution of remote DTDs or external entities.
- Making currently unsupported DTD-required ICH attributes pass by inventing values.
- UI surfacing beyond the existing publish job failure message path.

## Architecture

Create:

- `RATools.Application.Publishing.Validation.IEctdXmlValidator`
- `RATools.Application.Publishing.Validation.EctdXmlValidator`
- `RATools.Application.Publishing.Validation.EctdXmlValidationException`

The validator uses `XmlReader` with:

- `DtdProcessing.Parse`
- `ValidationType.DTD`
- a private `XmlResolver` that maps known DTD file names to `AppContext.BaseDirectory/reference/dtd`

The resolver denies unknown entities. This keeps validation deterministic and avoids accidental filesystem or network access.

## Path Handling

`BackboneGeneratedFile.RelativePath` is package-root relative. The validator uses that path as the XML base URI context so system ids are interpreted consistently with the final package layout:

- `index.xml` -> `util/dtd/ich-ectd-3-2.dtd`
- `m1/us/us-regional.xml` -> `../../util/dtd/us-regional-v3-3.dtd`

The resolver ultimately maps by allowed DTD file name, but keeping base URI context makes error handling and future path-specific checks explicit.

## Failure Behavior

The validator throws `EctdXmlValidationException` when:

- XML parsing fails.
- DTD validation reports an error.
- The document references an unknown DTD or external entity.
- A required bundled DTD file is missing.

`BackboneService` does not catch this exception; `PublishJobService` already catches publish execution exceptions and marks the publish job failed.

## Testing

Tests should prove:

- Empty writer-generated ICH `index.xml` passes DTD validation.
- Writer-generated US Regional XML with admin metadata passes DTD validation.
- A leaf missing a DTD-required attribute fails.
- Unknown DTD system ids are blocked.
- `BackboneService` validates both generated XML files before invoking the file writer.
- Full backend tests pass.
