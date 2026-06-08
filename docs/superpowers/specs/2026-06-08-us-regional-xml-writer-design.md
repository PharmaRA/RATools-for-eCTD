# US Regional M1 v3.3 us-regional.xml Writer Design

## Goal

Add a standards-aware writer that converts `EctdSequencePackage.Module1Leaves` and FDA regional publishing metadata into deterministic US Regional Module 1 v3.3 `us-regional.xml` content.

This is the next implementation step after the ICH eCTD v3.2.2 `index.xml` writer. It moves the project toward the first real FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3 delivery baseline while keeping package file writing and publish-flow rewiring as later steps.

## Scope

In scope:

- Extend the package model with the minimum FDA regional metadata required to generate DTD-shaped `admin` content.
- Generate `us-regional.xml` content for US Regional Module 1 v3.3.
- Consume `EctdSequencePackage.Module1Leaves`.
- Ignore `EctdSequencePackage.IchBackboneLeaves`; Modules 2 through 5 belong to the ICH `index.xml` writer.
- Use a US Regional M1 v3.3 section map derived from `reference/dtd/us-regional-v3-3.dtd`, not the current simplified `FdaEctd322.Root` M1 tree.
- Emit deterministic XML ordering.
- Emit leaf `ID`, `operation`, `xlink:type`, `xlink:href`, `checksum`, `checksum-type`, and `modified-file` when lifecycle data exists.
- Emit DTD-supported M1 leaf children only.
- Surface writer-specific errors for missing metadata and unsupported section mappings.
- Register the writer in application dependency injection.
- Add unit tests for contract shape, admin metadata, M1 section mapping, leaf attributes, lifecycle attributes, deterministic order, M2-M5 exclusion, metadata failures, and DI registration.

Out of scope:

- Replacing `BackboneService`.
- Writing XML to disk.
- Copying documents, DTDs, stylesheets, or supportive files.
- Generating `index.xml`; that is handled by `IIchIndexXmlWriter`.
- Generating `index-md5.txt`.
- Creating zip packages.
- Full DTD validation or FDA validation-criteria enforcement.
- Frontend metadata editing changes. The first writer can use package-model test data and later integrate with API/UI metadata fields.

## Standards Boundary

The first supported baseline remains:

- FDA CDER/CBER eCTD v3.2.2 + US Regional Module 1 v3.3.
- US Regional DTD: `reference/dtd/us-regional-v3-3.dtd`.
- FDA standards page: `https://www.fda.gov/drugs/electronic-regulatory-submission-and-review/ectd-submission-standards-ectd-v322-and-regional-m1`.

The bundled DTD declares:

- root element: `fda-regional:fda-regional`;
- FDA regional namespace: `http://www.ich.org/fda`;
- xlink namespace: `http://www.w3c.org/1999/xlink`;
- root attribute `dtd-version="3.3"`;
- root content model: `(admin, m1-regional?)`;
- `admin` content model: `(applicant-info, application-set)`;
- `m1-regional` content model: ordered optional M1 section elements;
- `leaf` content model: `(title, link-text?)`.

Because the bundled DTD uses `http://www.w3c.org/1999/xlink`, this writer should match the bundled asset value, consistent with the ICH writer decision.

## New Module

Create a focused namespace under:

`RATools.Application.Publishing.UsRegional`

Primary service:

- `IUsRegionalXmlWriter`
- `UsRegionalXmlWriter`

Result:

- `UsRegionalXmlWriteResult`

Exceptions:

- `UsRegionalXmlWriterException`
- `UsRegionalXmlMetadataException`
- `UsRegionalXmlSectionMappingException`

Registration:

- Register `IUsRegionalXmlWriter` in `AddApplication()` as singleton unless the implementation grows scoped dependencies. The first implementation should be stateless and use static section metadata.

## Package Model Extension

Add regional metadata to `EctdSequencePackage`:

```csharp
public sealed record EctdSequencePackage(
    Guid ApplicationId,
    string ApplicationNumber,
    string SequenceNumber,
    string StandardsProfile,
    string IchEctdVersion,
    string UsRegionalModule1Version,
    EctdApplicationMetadata Application,
    EctdSequenceMetadata Sequence,
    EctdUsRegionalMetadata UsRegional,
    IReadOnlyCollection<EctdLeaf> Module1Leaves,
    IReadOnlyCollection<EctdLeaf> IchBackboneLeaves,
    IReadOnlyCollection<EctdPublishedFile> PublishedFiles);
```

Minimum metadata record:

```csharp
public sealed record EctdUsRegionalMetadata(
    string ApplicantId,
    string CompanyName,
    string SubmissionDescription,
    string ApplicantContactName,
    string ApplicantContactType,
    string Telephone,
    string TelephoneNumberType,
    string Email,
    string ApplicationType,
    string SubmissionType,
    string SubmissionSubtype,
    string? FormType);
```

Initial package builder defaults should be conservative and traceable:

- `ApplicantId`: use application number until a separate applicant id field exists.
- `CompanyName`: use sequence applicant name, falling back to sponsor name.
- `SubmissionDescription`: use sequence description.
- `ApplicationType`: use existing metadata application type when present; otherwise derive from application number prefix when clear, and fail later when unclear.
- `SubmissionType`: use existing sequence publishing metadata or sequence submission type.
- `SubmissionSubtype`: use existing publishing metadata subtype. The writer should fail on blank values because the DTD requires the attribute.
- `FormType`: use existing `FormType` only when emitting the `admin/application-set/application/submission-information/form` element.

The writer must not invent applicant contact, telephone, or email values. Those values should be required in `EctdUsRegionalMetadata` for generation. Until the API/UI model is expanded, tests can construct package records directly.

## API Shape

Writer contract:

```csharp
public interface IUsRegionalXmlWriter
{
    UsRegionalXmlWriteResult Write(EctdSequencePackage package);
}
```

Result:

```csharp
public sealed record UsRegionalXmlWriteResult(
    string FileName,
    string RelativePath,
    XDocument Document,
    string XmlContent);
```

Rules:

- `FileName` is always `us-regional.xml`.
- `RelativePath` is always `m1/us/us-regional.xml`.
- `XmlContent` is serialized from `Document` using deterministic formatting.
- Null package input fails with `ArgumentNullException`.
- Missing required regional metadata fails with `UsRegionalXmlMetadataException`.
- An empty Module 1 leaf set still returns a valid root document with `admin` and no `m1-regional`.

## XML Shape

Root:

```xml
<!DOCTYPE fda-regional:fda-regional SYSTEM "../../util/dtd/us-regional-v3-3.dtd">
<fda-regional:fda-regional
  xmlns:fda-regional="http://www.ich.org/fda"
  xmlns:xlink="http://www.w3c.org/1999/xlink"
  dtd-version="3.3">
  <admin>...</admin>
  <m1-regional>...</m1-regional>
</fda-regional:fda-regional>
```

The DTD system id should be `../../util/dtd/us-regional-v3-3.dtd` because the file's target package path is `m1/us/us-regional.xml` and the future package writer should place DTDs under sequence-level `util/dtd`.

`admin` is always emitted:

```xml
<admin>
  <applicant-info>
    <id>...</id>
    <company-name>...</company-name>
    <submission-description>...</submission-description>
    <applicant-contacts>
      <applicant-contact>
        <applicant-contact-name applicant-contact-type="...">...</applicant-contact-name>
        <telephones>
          <telephone telephone-number-type="...">...</telephone>
        </telephones>
        <emails>
          <email>...</email>
        </emails>
      </applicant-contact>
    </applicant-contacts>
  </applicant-info>
  <application-set>
    <application application-containing-files="true">
      <application-information>
        <application-number application-type="...">...</application-number>
      </application-information>
      <submission-information>
        <submission-id submission-type="...">...</submission-id>
        <sequence-number submission-sub-type="...">...</sequence-number>
        <form form-type="...">...</form>
      </submission-information>
    </application>
  </application-set>
</admin>
```

The `form` element is optional in the DTD. The first writer should emit it only when `FormType` is present or when M1 form leaves exist under section `m1.1`.

`m1-regional` is emitted only when at least one M1 regional leaf exists:

```xml
<m1-regional>
  <m1-2-cover-letters>
    <leaf ...>
      <title>Cover Letter</title>
    </leaf>
  </m1-2-cover-letters>
</m1-regional>
```

Leaf:

```xml
<leaf
  ID="leaf-..."
  operation="new"
  checksum="..."
  checksum-type="sha256"
  xlink:type="simple"
  xlink:href="12-cover-letters/cover-letter.pdf">
  <title>...</title>
</leaf>
```

Lifecycle leaf:

```xml
<leaf
  ID="leaf-..."
  operation="replace"
  modified-file="../../../0000/m1/us/12-cover-letters/old-cover-letter.pdf"
  checksum="..."
  checksum-type="sha256"
  xlink:type="simple"
  xlink:href="12-cover-letters/new-cover-letter.pdf">
  <title>...</title>
</leaf>
```

`EctdLeaf.Href` is currently sequence-root relative because it is also used by the ICH writer. The US regional writer should convert Module 1 document hrefs to paths relative to `m1/us/us-regional.xml` by stripping the leading `m1/us/` segment when present. Lifecycle `modified-file` should use `EctdLifecycleReference.TargetSequenceNumber` plus `ModifiedFileHref` to build a relative path from the current sequence's `m1/us` directory to the historical target sequence, such as `../../../0000/m1/us/...`.

The writer must not emit prototype-only elements such as:

- `fileName`
- `mimeType`

## US Regional Section Mapping

Do not reuse the current `FdaEctd322.Root` M1 branch as the authoritative writer map. It currently contains top-level and selected M1 nodes, while the bundled US Regional DTD contains many deeper section elements such as:

- `m1-3-1-contact-sponsor-applicant-information`
- `m1-5-7-withdrawal-of-approval-of-an-application-or-revocation-of-license`
- `m1-12-17-orphan-drug-designation`
- `m1-14-2-3-final-labeling-text`
- `m1-15-2-1-1-clean-version`
- `m1-16-2-6-rems-modification-history`

Add a writer-owned `UsRegionalM1V33` section map that captures:

- DTD element name;
- CTD section path;
- title;
- children in DTD order;
- whether the element directly accepts leaves;
- required structural attributes, when any are known.

The first implementation may hand-maintain this map if it is reviewed against the bundled DTD. A later tooling task can generate it from the DTD, but the writer should not parse the DTD at runtime.

Mapping rules:

- Only `m1` sections are accepted.
- M2-M5 leaves are ignored by this writer.
- Unknown M1 sections fail with `UsRegionalXmlSectionMappingException`.
- The writer emits the full ancestor chain so XML follows DTD parent-child order.
- Leaves are emitted only under section elements whose DTD content model accepts `(leaf | node-extension)*` or equivalent form content.
- Section elements requiring unavailable attributes fail fast instead of fabricating values.

Special M1 cases:

- `m1.1` maps to `m1-1-forms`, whose content model is `form*`; leaves under forms should be emitted inside a `form form-type="..."` element.
- Promotional material sections under `m1.15` have required attributes in the DTD. The first writer should fail fast if leaves target those sections before the package model carries required promotional metadata.
- Other attribute-heavy sections should use the same fail-fast rule.

## Ordering

The writer must be deterministic:

- `admin` always appears before `m1-regional`;
- `m1-regional` children follow the DTD order;
- section descendants follow the DTD order;
- leaves within the same section preserve package model order, then sort by `LeafId` as a tie-breaker;
- XML serialization must be stable between repeated calls for the same package.

## Error Handling

Add writer-specific exceptions:

- `UsRegionalXmlWriterException` as the base writer exception.
- `UsRegionalXmlMetadataException` for missing or blank required regional metadata.
- `UsRegionalXmlSectionMappingException` for unknown, unsupported, or attribute-incomplete M1 section mappings.

Exceptions should include enough context for future compliance findings:

- application id;
- sequence number;
- placement id when applicable;
- CTD section when applicable;
- metadata field name when applicable;
- reason.

The writer should fail fast on unsupported M1 sections and missing required attributes. It should not silently place unknown sections into `node-extension` in this first version because that would hide profile gaps and create misleading package output.

## Integration Boundary

This feature should not change `BackboneService` yet.

Future tasks will:

- update metadata persistence and API/UI editing for required FDA regional fields;
- update `BackboneService` or a new package orchestrator to delegate to `IEctdPackageModelBuilder`, `IIchIndexXmlWriter`, and `IUsRegionalXmlWriter`;
- write `index.xml`, `m1/us/us-regional.xml`, documents, DTD assets, and checksum files to disk;
- add DTD validation and compliance report findings;
- update publish artifacts and frontend package review.

Keeping the writer unused by publish flow initially is intentional. It provides a tested XML boundary before publish service rewiring.

## Tests

Add tests under:

`tests/RATools.Tests/Publishing/UsRegional`

Required coverage:

- `UsRegionalXmlWriteResult` exposes `us-regional.xml`, `m1/us/us-regional.xml`, `XDocument`, and serialized XML.
- Generates a root document with required namespace, DTD system id, and `dtd-version="3.3"`.
- Emits required `admin` metadata.
- Emits no `m1-regional` when Module 1 leaves are empty.
- Maps common M1 leaves such as `m1.2`, `m1.14.2.3`, and `m1.16.2.1` to DTD element names.
- Emits M1 sections in DTD order.
- Emits leaf attributes: `ID`, `operation`, `checksum`, `checksum-type`, `xlink:type`, and `xlink:href`.
- Emits lifecycle `modified-file` when `EctdLeaf.Lifecycle` exists.
- Emits only `<title>` as the required leaf child when no link text exists.
- Does not emit `fileName` or `mimeType`.
- Ignores ICH M2-M5 leaves.
- Produces stable XML for repeated writes.
- Throws `UsRegionalXmlMetadataException` for missing required admin metadata.
- Throws `UsRegionalXmlSectionMappingException` for unknown M1 sections.
- Throws `UsRegionalXmlSectionMappingException` for attribute-heavy sections that lack required metadata, such as unsupported promotional material nodes.
- Registers `IUsRegionalXmlWriter` in application DI.

## Acceptance Criteria

The US Regional XML writer is complete when:

- `EctdSequencePackage` exposes the minimum `EctdUsRegionalMetadata` needed by the writer.
- `IUsRegionalXmlWriter` is registered in application DI.
- `UsRegionalXmlWriter.Write()` returns `us-regional.xml` content for `EctdSequencePackage`.
- Generated root XML uses the FDA regional namespace, bundled-DTD xlink namespace, and `dtd-version="3.3"`.
- The document includes a DTD system id appropriate for `m1/us/us-regional.xml`.
- Required `admin` content is generated from package model facts.
- M1 leaves are placed under DTD-derived US Regional element names.
- M2-M5 leaves are excluded.
- Leaf attributes and lifecycle `modified-file` are generated from package model facts.
- Prototype XML namespace and prototype-only child elements are absent.
- Missing metadata and unsupported M1 mappings fail with writer-specific exceptions.
- All writer tests pass.
- Existing backend and frontend test suites still pass.
