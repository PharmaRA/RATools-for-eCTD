# CTD instance model (P1-02)

`CtdNodeInstance` is an immutable application-scoped identity. `CtdNodeGraph`
validates parent ownership, required parent definitions, extension locations,
cycles and sibling identity/cardinality, including singleton roots. Rehydration
checks a stored identity digest when supplied; it never replaces an imported ID
with a new GUID. A graph uses one explicit definition/comparison version.

Identity keys use SHA-256 of `ratools-canonical-json-v1` applied to this array:

```text
[
  applicationId in UUID D format,
  parentInstanceId in UUID D format or null,
  definitionKey,
  identityComparisonVersion,
  [[attributeName, originalValueOrNull], ...] sorted ordinally by attribute name,
  extensionInstanceId in UUID D format, or null for a standard node
]
```

Every schema-declared identity attribute has an entry, including a null for a
missing value. A present empty value remains distinct from an absent value.
Titles, sort order, file names and technical XML IDs do not enter this digest.
Extension instances use their own IDs because equal titles do not imply equal
business identity. Canonical encoding is implemented with the standard JSON
parser and a deterministic restricted serializer. It agrees with G0's independent
Unicode/control-character vectors and real file/history manifest digests.

Normal creation requires complete valid attributes. Import/backfill rehydration
can explicitly retain `MissingMetadata` or `Ambiguous` identities. Unknown
attributes and invalid XML values remain errors. Equal imported sibling contexts
are permitted only when all conflicting identities are marked ambiguous; their
IDs remain distinct. This does not allow duplicates of a nonrepeatable section.
The importer must detect the whole ambiguity group before restoring it.

`SequenceNode` freezes the exact attributes, display title, order, directory
segment and definition version for one sequence. Creating another sequence's
record does not edit the first record. Identity attributes must match the stable
instance, including which attributes are absent. Changing identity requires an
explicit new instance or reviewed mapping in the later repair workflow.

Completion status includes unresolved ancestors: a perfectly formed leaf section
under an ambiguous manufacturer is still `LegacyUnresolved`. Missing required
metadata or an extension title is `NeedsMetadataCompletion`. These records may
be displayed and bound while repairing a draft; they are not permission to
publish incomplete material.

`DocumentPlacement.BindToNode` checks application and sequence, derives the
compatibility section, records node ID and order, and preserves `LeafId`.
A section-only move cannot alter a bound placement to another section; it must
select a target instance. Existing unbound placements retain the legacy behavior.
`LeafAddress` separately models application, sequence, exact backbone path and
original XML ID, with portable logical-path validation and relative references
computed from both source and target XML directories. Cross-application and
future-sequence references are rejected. History-baseline membership, target
effectiveness and region-specific operation rules remain P2/P3 responsibilities.

This is the domain task boundary. P1-03 adds EF records, composite relationships,
revision columns and idempotent backfill before any API can create persistent
bound nodes. Until then the existing EF mapping continues to serve unbound
placements only. P1-04 onward adds revision guards and workflows; P1-06/07/08
connect import, publishing and the UI. The full multi-instance workflow is not
yet complete.
