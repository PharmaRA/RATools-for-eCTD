# Scoped FDA M1 output

P6-06 implementation and local Windows evidence, 2026-11-06. The selected first
FDA scope covers explicit US administrative metadata, m1.1 forms with independent
placement attributes, and existing m1.2 cover letters. The bundled US M1 3.3 DTD
is the pinned technical input. FDA current source/version/business criteria
remain unverified in `standards-scope.json`; this work does not activate Formal
publishing or establish regulatory qualification.

## Explicit authoring and output

Typed US metadata now has separate application, applicant and submission
identifiers. `applicationIdentifier` writes application-number,
`applicantIdentifier` writes applicant-info/id, and `submissionIdentifier` writes
submission-id. The latter two optional model fields are required for complete
typed output; old drafts without them remain readable and require completion.
Omitted optional fields remain omitted in historical canonical evidence, so
existing revision digests continue to verify.

The API/schema-driven regional panel exposes these fields and ordered contacts,
typed telephones and multiple emails. The writer uses all explicit values and
preserves order. It does not replace missing typed values with an internal UUID,
application folder, sponsor or sequence number. A regional head that is incomplete
fails with a specific field path; it cannot select the legacy shared projection.
Shared-only historical trial fixtures retain their documented legacy writer path.

Each form resolves an exact placement context, then its exact node context, then
an explicit sequence form value. A present context with a cleared form value
requires correction and cannot inherit a hidden fallback. Two placements of one
document can have different form types. The pinned DTD permits repeated form
wrappers under `m1-regional/m1-1-forms`; typed output uses that structure and emits
each leaf once. A generic imported language is emitted on the actual leaf's
`xml:lang`, which the US DTD permits. EU country/PI values and form attributes on
unrelated sections are refused with placement/node/field diagnostics.

The DTD defines form-type as CDATA. No invented regulatory vocabulary is exposed
as verified. Existing `RequiresUnsupportedAttributes` guards remain active for
m1.15 promotional correspondence/material branches. Their audience/document/
material attributes and independent qualification stay pending in the matrix.

## Snapshot and release binding

Package construction captures the persisted regional revision under the existing
application/workspace lock. It retains the immutable revision ID, digest and exact
standards snapshot. A selected archived profile must match that snapshot's full
profile content and assets; a current provider cannot reinterpret the revision.
The ordinary backbone service also validates and supplies the archived profile.
Writers consume the captured package and do not reread mutable metadata stores.

Release capture checks application/sequence/standards/workspace scope and archives
the exact verified revision as `regional-metadata.json`. The existing release
manifest binds its byte length and hashes using the new RegionalMetadata file
kind. It remains outside the regulatory payload and delivery ZIP. Manifest
serialization and reload verify this file's digest; old manifests without such
an attachment remain readable.

## Capability matrix

| Scope | Draft editing/import | Trial XML | Formal | Evidence |
| --- | --- | --- | --- | --- |
| US explicit administrative fields and ordered contacts | Supported | Supported against bundled 3.3 DTD | Blocked by P6-02 gates | Synthetic independent inputs, parser and UI evidence |
| m1.1 independent form-type, including same-file placements | Supported | Selected scope | Blocked | Positive/negative DTD, exact context, API/PG and reimport cases |
| m1.2 cover letters and generic leaf language | Supported | Existing scope retained | Blocked | Legacy and typed writer regression |
| m1.15 promotional correspondence/material branches | Pending | Explicitly refused | Blocked | Attribute inventory retained; business vocabulary/samples pending |
| Current FDA source/version and business criteria | Pending verification | No current regulatory claim | Blocked | Existing version labels and bundled asset provenance remain unverified |

## Local verification

First analyzed layer: `.artifacts/current-head-review-20261104/p606-backend-stage1/`.
New writer/binding/parser/historical cases passed 22 tests after an observed
3-pass/19-fail baseline. Existing direct US writer, section table, regional model/
service and import regressions passed 65 tests. Parent capture/profile cases
passed four tests after three observed failures; actual PG import/new-connection/
DTD/full reimport also passed. Initial HTTP same-file forms exposed tied source
ordering resolved by random placement IDs; its deterministic fix and final
acceptance passed on stage2: four analyzed layers, 24 scoped cases and nine
independent joint cases. Imported US M1 leaves with tied section/SortOrder use
their persisted placement-context order before the GUID fallback; explicit
SortOrder edits retain priority. A fixed reverse-GUID case failed before the fix.

Final review found a case mismatch: legal `M1.1` could enter the package through
existing case-insensitive section contracts but miss the new typed form loop.
Five comparisons in typed output, context validation and imported ordering now
use the same ordinal case-insensitive policy. Seven failures were observed before
the fix; stage3 four-layer analyzed compilation, 34 scoped cases and all nine
independent joint cases passed afterward. Uppercase form leaves with placement,
node or sequence context each appear exactly once; cleared/incorrect contexts
still fail. The scoped review verifies this repair before the task commit.

Actual Chromium evidence is retained in `.artifacts/p606-browser/evidence.json`
and `completed.png`. The actual panel used actual controllers/services with
retained in-memory repositories, edited explicit identifiers, added a contact,
preflighted without mutation, saved different form values, and reloaded them.
Original revision values and both languages remained intact; page errors were
zero. PostgreSQL and XML writer evidence are separate.

OpenAPI semantic comparison changes only `UsRegionalMetadata` and
`ReleaseFileKind`, with no path/schema addition or removal. Regional frontend
runtime tests passed 19 cases; direct TypeScript and ESLint passed. Counts overlap.
No ordinary cleanup-owning fixture, full repository suite or Linux/external FDA
validator qualification is claimed. Final task review is required before commit.
