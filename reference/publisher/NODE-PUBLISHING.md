# Node-aware XML publishing (P1-07)

The package builder reads the sequence's nodes, placements, metadata and archived
backbone addresses while holding the application workspace lock. The resulting
package records its `WorkspaceRevision`. A concurrent edit waits until this read
finishes. Immutable document-byte snapshots and formal publication remain P4 work;
this read lock is not a promise that files cannot change after package construction.

ICH output follows actual node identities and the pinned definition schema. Two
manufacturers in the same section produce separate attributed groups. The writer
rejects missing attributes, unresolved or equal sibling identities, invalid parents,
cycles, unsupported schema versions, duplicate XML IDs and incompatible leaf
bindings. Unused empty UI groups are pruned. Extension titles and nested extensions
are retained, including namespace-correct `xml:lang` attributes.

Different standard sections follow the DTD's fixed content slots. Within a
repeatable slot, nodes use `SortOrder` and then stable node ID. Leaves use
`SortOrder` and placement ID; where the schema allows leaf/extension interleaving,
both share those sort positions. Existing leaf IDs never change during projection.
Legacy unbound leaves receive deterministic ephemeral nodes only through singleton
definitions without required attributes. Repeatable business groups require an
explicit binding; no persisted metadata is invented by publishing.

Lifecycle selection is constrained to the same business node and never selects a
delete leaf. `modified-file` uses the historical backbone address, including custom
regional filenames. Document delete operations do not deliver another document or
emit an href. Original imported provenance remains distinct from the current file
path: an imported URI spelling is retained only if it still resolves to the actual
delivery file. Other hrefs are calculated relative to the writing backbone.

Regional XML is generated first. Its exact UTF-8 bytes determine the checksum in
the ICH M1 reference. Archived reference IDs and supported M1 container attributes
are preserved, and custom regional directories receive the correct relative DTD
address. Multiple regional backbones and regional-backbone deletion require P6
profile support and are explicitly blocked. This does not implement the detailed
regional M1 node schema. Every generated XML still runs through its applicable DTD
validator.

Verification on Windows with real PostgreSQL: 758 backend tests passed without
skips, followed by 18 node-publishing tests after the final regional guard and mixed
ordering additions. The four independent G0 sequences were emitted as directories
and ZIPs, checked against the hand-authored historical-address oracle, and imported
again. Tests cover distinct manufacturers, extensions, encoded hrefs, custom
regional historical targets, unchanged PDF bytes, schema failures and a concurrent
edit waiting for the package read. EF model drift and publisher reference contract
checks pass. Linux execution remains NotEvaluated.
