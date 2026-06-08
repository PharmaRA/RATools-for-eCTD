# ICH eCTD v3.2.2 index.xml Writer Design

## Goal

Add an application-layer XML writer that converts `EctdSequencePackage.IchBackboneLeaves` into a deterministic ICH eCTD v3.2.2 `index.xml` document.

This is the fourth implementation step in the FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3 compliance roadmap. The writer creates a standards-aware XML generation boundary without replacing the existing publish flow yet.

## Scope

In scope:

- Generate `index.xml` content for ICH eCTD v3.2.2 Modules 2 through 5.
- Consume the already-built `EctdSequencePackage`.
- Use only `EctdSequencePackage.IchBackboneLeaves`.
- Ignore `EctdSequencePackage.Module1Leaves`; Module 1 belongs to the future US Regional writer.
- Use the existing FDA section dictionary profile to map CTD section paths to ICH DTD element names.
- Emit ICH namespace and DTD-compatible `xlink` namespace declarations.
- Emit deterministic XML ordering.
- Emit leaf `ID`, `operation`, `xlink:type`, `xlink:href`, `checksum`, `checksum-type`, and `modified-file` when lifecycle data exists.
- Emit only DTD-supported leaf children.
- Surface writer-specific errors for unsupported or ambiguous ICH section mappings.
- Add unit tests for XML shape, section mapping, leaf attributes, lifecycle attributes, deterministic order, and M1 exclusion.

Out of scope:

- Replacing `BackboneService`.
- Writing XML to disk.
- Generating `m1/us/us-regional.xml`.
- Generating `index-md5.txt`.
- Copying DTD, stylesheet, or document files.
- Creating zip packages.
- Full DTD validation or compliance validation. This writer should produce a DTD-aware shape, while full validation remains a later compliance verifier task.

## Standards Boundary

The first supported baseline remains:

- ICH eCTD Backbone File Specification for Modules 2 through 5: v3.2.2.
- Bundled DTD asset: `reference/dtd/ich-ectd-3-2.dtd`.
- Existing standards profile: `FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3`.

The local DTD declares:

- root element: `ectd:ectd`;
- ICH namespace: `http://www.ich.org/ectd`;
- xlink namespace: `http://www.w3c.org/1999/xlink`;
- root attribute `dtd-version="3.2"`;
- `leaf` content model: `(title, link-text?)`;
- `leaf` required attributes: `ID`, `operation`, `checksum`, `checksum-type`;
- `leaf` supported lifecycle attribute: `modified-file`.

Because the bundled DTD uses `http://www.w3c.org/1999/xlink`, the first writer should match that local DTD value instead of the more common `http://www.w3.org/1999/xlink` URI. A later standards asset review can decide whether to preserve or patch the bundled DTD.

## New Module

Create a focused namespace under:

`RATools.Application.Publishing.Ich`

Primary service:

- `IIchIndexXmlWriter`
- `IchIndexXmlWriter`

Result:

- `IchIndexXmlWriteResult`

Exceptions:

- `IchIndexXmlWriterException`
- `IchIndexXmlSectionMappingException`

Registration:

- Register `IIchIndexXmlWriter` in `AddApplication()` as singleton unless implementation grows scoped dependencies. The first implementation should be stateless and use static section profile data.

## API Shape

Writer contract:

```csharp
public interface IIchIndexXmlWriter
{
    IchIndexXmlWriteResult Write(EctdSequencePackage package);
}
```

Result:

```csharp
public sealed record IchIndexXmlWriteResult(
    string FileName,
    XDocument Document,
    string XmlContent);
```

Rules:

- `FileName` is always `index.xml`.
- `XmlContent` is serialized from `Document` using deterministic formatting.
- The writer accepts an empty ICH leaf set and still returns a root document with no module elements.
- Null package input fails with `ArgumentNullException`.

## XML Shape

Root:

```xml
<!DOCTYPE ectd:ectd SYSTEM "util/dtd/ich-ectd-3-2.dtd">
<ectd:ectd
  xmlns:ectd="http://www.ich.org/ectd"
  xmlns:xlink="http://www.w3c.org/1999/xlink"
  dtd-version="3.2">
  ...
</ectd:ectd>
```

The document must include an `XDocumentType` equivalent to:

```xml
<!DOCTYPE ectd:ectd SYSTEM "util/dtd/ich-ectd-3-2.dtd">
```

The root element is prefixed as `ectd:ectd`. Module, section, `leaf`, `title`, and future `node-extension` elements are unqualified because that is how the bundled ICH DTD declares them.

Top-level module elements are emitted only when at least one descendant leaf exists:

- `m2-common-technical-document-summaries`
- `m3-quality`
- `m4-nonclinical-study-reports`
- `m5-clinical-study-reports`

Module 1 is never emitted by this writer.

Leaf:

```xml
<leaf
  ID="leaf-..."
  operation="new"
  checksum="..."
  checksum-type="sha256"
  xlink:type="simple"
  xlink:href="m3/32-body-of-data/file.pdf">
  <title>...</title>
</leaf>
```

Lifecycle leaf:

```xml
<leaf
  ID="leaf-..."
  operation="replace"
  modified-file="m3/32-body-of-data/old-file.pdf"
  checksum="..."
  checksum-type="sha256"
  xlink:type="simple"
  xlink:href="m3/32-body-of-data/new-file.pdf">
  <title>...</title>
</leaf>
```

The writer must not emit prototype-only elements such as:

- `fileName`
- `mimeType`

Those are not permitted by the ICH DTD leaf content model.

## Section Mapping

Use the existing FDA section dictionary profile:

- `FdaEctd322.Root`
- `SectionDictionaryProfiles.ResolveByName(...)`
- `SectionDictionary`

Mapping rules:

- Each ICH leaf's `CtdSection` must map to exactly one profile entry.
- Only M2-M5 sections are accepted.
- M1 leaves are ignored before section mapping.
- Non-standard or unknown sections fail with `IchIndexXmlSectionMappingException`.
- Ambiguous section mappings fail with `IchIndexXmlSectionMappingException`.
- The writer emits the full ancestor chain from the profile tree so the XML follows DTD parent-child order.

Examples:

- `m2` maps to `m2-common-technical-document-summaries`.
- `m3.2` maps to `m3-2-body-of-data` under `m3-quality`.
- `m5.3.5.1` maps under `m5-clinical-study-reports` -> `m5-3-clinical-study-reports` -> `m5-3-5-reports-of-efficacy-and-safety-studies` -> `m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication`.

Known DTD-required attributes on repeatable parent nodes, such as `substance`, `manufacturer`, and `indication`, are not available in the package model yet. The first writer should avoid inventing these values. If a leaf targets a section that requires unavailable parent attributes, the writer should still emit the structural node without fabricated attributes and leave strict DTD validation to the later compliance validator. This keeps generated facts honest instead of silently manufacturing regulatory metadata.

## Ordering

The writer must be deterministic:

- module order follows the DTD order: M2, M3, M4, M5;
- section child order follows the `FdaEctd322.Root` profile order;
- leaves within the same section preserve the package model order, then sort by `LeafId` as a tie-breaker if needed;
- XML serialization must be stable between repeated calls for the same package.

## Error Handling

Add writer-specific exceptions:

- `IchIndexXmlWriterException` as the base writer exception.
- `IchIndexXmlSectionMappingException` for unsupported, non-standard, ambiguous, or M1-only section mappings that reach the ICH writer path.

Exceptions should include enough context for future compliance findings:

- application id;
- sequence number;
- placement id when applicable;
- CTD section when applicable;
- reason.

The writer should fail fast on unsupported ICH section mappings. It should not silently place unknown sections into `node-extension` in this first version, because doing so could hide profile gaps and create misleading package output.

## Integration Boundary

This feature should not change `BackboneService` yet.

Future tasks will:

- implement `us-regional.xml` writer for Module 1;
- update `BackboneService` to delegate to `IEctdPackageModelBuilder`, `IIchIndexXmlWriter`, and the future US regional writer;
- add package file writing for `index.xml`, `index-md5.txt`, regional XML, documents, and standards assets;
- add compliance validation around generated package model and output files.

Keeping the writer unused by publish flow initially is intentional. It provides a tested XML contract before the publish service is rewired.

## Tests

Add tests under:

`tests/RATools.Tests/Publishing/Ich`

Required coverage:

- Generates an empty `index.xml` root for a package with no ICH leaves.
- Uses ICH root namespace, local DTD xlink namespace, and `dtd-version="3.2"`.
- Emits M2, M3, M4, and M5 sections in DTD order.
- Maps known CTD section paths to the existing DTD element names.
- Emits leaf attributes: `ID`, `operation`, `checksum`, `checksum-type`, `xlink:type`, and `xlink:href`.
- Emits lifecycle `modified-file` when `EctdLeaf.Lifecycle` exists.
- Emits only `<title>` as the required leaf child when no link text exists.
- Does not emit `fileName` or `mimeType`.
- Ignores Module 1 leaves.
- Produces stable XML for repeated writes.
- Throws `IchIndexXmlSectionMappingException` for unknown M2-M5 section paths.

## Acceptance Criteria

The ICH index XML writer is complete when:

- `IIchIndexXmlWriter` is registered in application DI.
- `IchIndexXmlWriter.Write()` returns `index.xml` content for `EctdSequencePackage`.
- Generated root XML uses the ICH namespace, bundled-DTD xlink namespace, and `dtd-version="3.2"`.
- M2-M5 leaves are placed under profile-derived DTD element names.
- M1 leaves are excluded.
- Leaf attributes and lifecycle `modified-file` are generated from package model facts.
- Prototype XML namespace and prototype-only child elements are absent.
- Unsupported ICH section mappings fail with writer-specific exceptions.
- All writer tests pass.
- Existing backend and frontend test suites still pass.
