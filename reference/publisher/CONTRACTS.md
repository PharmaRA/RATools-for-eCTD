# Shared publisher contracts (G0-03)

`contracts-v1.json` fixes ownership and invariants for P1–P6. It specifies the
implementation boundary; it does not advertise unimplemented routes. Concrete
DTOs must also update the existing OpenAPI snapshot and generated frontend types.

## Identity and ordering

A section definition belongs to a standards snapshot. A node instance belongs
to an application. Its appearance in a sequence belongs to that sequence, with
its own attributes, title, order, storage segment and metadata-completion state.
The application instance supplies stable business identity; the sequence record
supplies the exact historical appearance. Editing one sequence cannot mutate
the attributes displayed for another sequence.

The identity comparison version initially compares parsed XML attribute values
exactly. Attribute names are schema-controlled; unknown names are findings.
Identity attributes are individually declared by the schema. Titles, file names,
directories and internal database IDs never supply missing manufacturer or
substance evidence. Equal ambiguous siblings survive import with separate IDs
and require explicit mapping. Nonrepeatable siblings have database uniqueness,
including an explicit constraint for root instances with no parent.

An XML `ID` is case-sensitive and unique within its own document. `LeafAddress`
contains application, four-digit sequence, backbone-relative path and original
leaf ID. An XML node's optional `ID` is also distinct from `NodeInstanceId`.
The fixture's two `overview` leaves demonstrate why a leaf ID alone is inadequate.
Legacy generated leaf IDs and imported XML IDs survive migration, retries,
sorting, renaming and processing a file in the same draft. A replacement in a
new regulatory sequence creates its own leaf and targets the historical one.

Sequence nodes and placements sort by explicit `sortOrder`, then stable instance
or leaf ID using ordinal comparison. Defined child sections retain their DTD
order; only repeats of a permitted child and leaves within their permitted
content slot are reordered. This prevents a UI sort from creating invalid XML.

## Paths and manifests

Public addresses are logical, case-preserved slash-separated paths. They never
contain a host drive or physical staging path. Directory/ZIP collision checking
also applies the portable path policy, including case-folded conflicts; rejecting
a collision does not change the preserved href or XML ID.

URI syntax is parsed before decoding. Decode each path segment and the fragment
once, validate the decoded segment, then resolve relative to the source backbone.
Do not decode an entire URI before splitting it or repeatedly decode percent
escapes. `modified-file` may traverse to a previous logical sequence; the result
must stay in the same application and resolve through the chosen baseline.
File-content hrefs use the corresponding authorized package boundary. Unknown
resource hosts, system DTD paths, URI queries, duplicate IDs and ambiguous roots
produce explicit findings rather than fallback guesses.

For the fixture, the Alpha replacement uses:

```text
source: 0001/index.xml#alpha-spec-v2
modified-file: ../0000/index.xml#alpha-spec-v1
target: 0000/index.xml#alpha-spec-v1
file: 0000/m3/32-body-data/32s-drug-sub/drug-a-alpha/32s4-contr-drug-sub/32s41-spec/specification.pdf
```

The regional source `0001/m1/us/us-regional.xml` would need
`../../../0000/index.xml#alpha-spec-v1` to name that same target. Relative URIs
therefore cannot be reconstructed from a target file name or section number.
Cross-instance exceptions require a versioned regional rule, not an implicit
fallback in the importer or target selector.

`ratools-canonical-json-v1` uses UTF-8 without a BOM or trailing newline. Object
members sort by ordinal UTF-16 code-unit order. Arrays preserve their declared
semantic order; manifest file arrays first sort by logical path, baseline arrays
by sequence number, and unordered sets by their documented stable identity.
Strings retain their original Unicode value. Use JSON escapes for quotation mark,
backslash and controls (short escapes for backspace, tab, newline, form feed and
carriage return; other controls as lower-case `\u00xx`), and otherwise emit Unicode
directly. Reject unpaired surrogates, duplicate object keys, floats and integers
outside the safe JSON range. Booleans and null use JSON literals. No culture
formatting, whitespace, Unicode normalization or case folding enters a digest.

SHA-256 covers these bytes. Exclude only the explicitly declared self-digest
field; do not silently omit unknown fields. Package input manifests include all
delivered files, their lengths and SHA-256/MD5 values. Validation binding adds
history, standards, rules, engine, mode and resource-limit digests. A snapshot
manifest includes the fixed node/metadata/content selection and validation
reference. ZIP hashes live in an outer artifact or export record to avoid a
manifest/ZIP hash cycle. Directory manifests and ZIP-byte hashes are different
facts; equivalent directory content does not imply identical compressed bytes.

`contract-examples.json` provides canonicalization vectors and real fixture
addresses and content digests. Implementations must agree with those bytes
before using this encoding for cache keys, idempotency or immutable records.

## Revision, history and validation

`WorkspaceRevision` starts at zero, uses a database bigint and is bounded by the
safe JSON integer range. It increases once per atomic logical mutation. It is
not the eCTD sequence number. All delivery-affecting writes in the JSON inventory
use a database compare-and-increment with `expectedRevision`. Missing revisions
return 428 after the coordinated client/API cutover; stale revisions return 409
with a stable code and current revision. There is no legacy write bypass.

The file system is not a database transaction participant. Prepare task-owned
files, conditionally commit all database changes and the revision, then perform
cleanup. Failed preparation and complete compensation leave the revision alone.
If reconciliation leaves a changed consistent state, advance the revision and
invalidate validation. Compensation uses an independent bounded token and stores
unresolved cleanup work. File digests detect external edits even when the database
revision has not changed. A successful response must describe durable state.

A history baseline is an immutable selection of exactly one source version per
included sequence in an application. It includes source, content digest, standard
and trust status. Missing required historical dependencies are visible; sequence
number order alone is not proof of completeness or submission. A later trust or
source change creates a new baseline digest. The P2 reader never consults business
placements to decide what the input package contains. P3 projects validated facts
and leaves invalid/unresolved events visible without applying them as successes.

Execution status, check status, severity and blocking are separate. A completed
validation run may contain failed checks. A necessary `NotEvaluated` check blocks
readiness; `NotApplicable` needs a recorded applicability reason. Manual evidence
can close a check only if its rule explicitly allows this. A report is reusable
only for the complete identical binding listed in `validationBinding`.

## Finalization, delivery and processing

Finalization captures one draft revision and blocks further writes to that draft
while its durable job owns the capture. Capture does not consume a new regulatory
sequence number. A failed/cancelled job returns an editable draft with recorded
recovery work. Committing creates an immutable snapshot and seals that draft.
Editing again explicitly creates a new draft identity and the next workspace
revision; the source snapshot remains unchanged. An observed input change prevents
commit even if the database revision appears unchanged.

The fixed content protocol is Requested → Captured → Staged → Validated →
Materialized → Committed. Each phase records task ownership and digests. Recovery
after a directory rename checks those facts before committing or quarantining.
Cancellation after commit returns the committed snapshot. Cleanup failure never
turns a committed snapshot into a failed job or authorizes deleting its files.

Document versions and snapshot payloads use independent copies under a controlled
root, not links to mutable source files. Snapshot storage is internal:

```text
<application-output>/_releases/<sequence>/<snapshot-id>/
  payload/<sequence>/...
  manifest.json
  validation-report.json
  delivery-package.zip
```

External packages contain only the selected profile's regulatory layout.
Public APIs accept authorized artifact/workspace/version IDs; the server maps
them to these paths. Export verifies stored content. It does not reconstruct a
damaged snapshot from a current draft. Backups preserve snapshots, version files,
reports, standards and every referenced historical source.

Delivery is an append-only event stream. Exported bytes, manual submission
registration and typed transport/technical/business receipts are distinct facts.
Corrections refer to previous events. No publish, validation or snapshot success
automatically records submission or acceptance.

P5 previews bind a complete atomic selected batch, revision, content versions,
history baseline and expiry to a plan digest. Selecting a subset creates a new
plan. Processing always creates a new version; adopting it is a guarded draft
mutation. Historical versions and references stay fixed. P6 regional fields and
standards snapshots use versioned, closed schemas; unconfirmed legacy defaults
remain unresolved and cannot activate a qualified regional capability.
