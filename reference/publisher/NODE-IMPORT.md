# CTD node import (P1-06)

The importer stages a complete sequence before adding it to the application or
historical leaf index. It walks ICH nodes before attaching leaves, using the
pinned `ich-3.2.2-nodes-v1` schema. Node attributes retain their exact XML values;
the parent chain, sibling order, extension title, original leaf ID and leaf order
are preserved. Unknown attributes and invalid parent/cardinality relationships
fail the sequence. Missing required metadata remains explicitly incomplete.

## Identity and history

- A standard node reuses a historical identity only under the same parent and
  with a unique, matching set of schema-declared identity attributes. Repeated
  nodes with incomplete identity metadata are not guessed from directories,
  sponsor fields or filenames.
- Equal-attribute siblings receive separate IDs and an ambiguity diagnostic.
  When a later sequence reveals ambiguity, all affected identities in the staged
  application become ambiguous, including earlier sequence metadata. A failed
  sequence cannot change that earlier state.
- Extensions are not matched by title or XML ID alone. A unique historical
  `modified-file` XML address can establish their ancestor chain; all referenced
  leaves must agree on that chain. Otherwise a new extension identity is retained.
- Explicit references resolve in the source XML's directory and full historical
  XML/sequence context before node context is checked. Unresolved references never
  fall back from an explicit parent path or fragment to a similarly named file.
- Legacy bare hrefs match only a unique earlier non-delete placement in the same
  node. Unbound regional/legacy placements additionally require the same backbone
  path and exact ancestor XML context. Ambiguous modeled identities cannot use
  bare-href matching. Invalid modeled lifecycle targets fail the sequence; legacy
  unbound replace/append behavior retains its warning and null target.

## Source preservation and regional scope

`DocumentPlacement.ImportedSource` records the original backbone relative path,
href and modified-file spelling. URI escapes are resolved when reading files,
without renaming the physical file. Node moves retain this **original provenance**;
it must not be confused with the current document storage path.

`imported_backbones` stores the parsed source XML for each accepted sequence.
This preserves XML values, including regional attributes and empty groups; it is
not a byte-identical backup (XML formatting/declaration may change). Input files
are never rewritten. A single regional XML referenced by the ICH M1 container
takes precedence over the profile's conventional path. Multiple regional
backbones require a later regional import profile and currently fail explicitly.

Regional M1 detail schemas remain P6 work. Such leaves retain their existing
`m1.x` section, source context and a persisted `NODE_SCHEMA_NOT_AVAILABLE`
diagnostic. They are **not** bound to the ICH M1 container. Abbreviated legacy
section names have the same explicit unbound compatibility behavior. Archived
attributes are input evidence only, never an arbitrary-attribute output bypass.

## Persistence and recovery

`IApplicationImportStore` atomically saves the new application, sequences, final
node graph, sequence nodes, documents, placements, diagnostics and backbones.
PostgreSQL node writes participate in the outer transaction; the memory provider
stages its writes in the existing transaction mechanism. Each imported sequence
receives one workspace revision. Parse failures publish no partial sequence.

PostgreSQL rollback uses the persistence layer's independent 30-second token,
even after request cancellation. If rollback cannot be confirmed,
`IMPORT_ROLLBACK_INCOMPLETE` identifies the application and affected document and
placement IDs. Inspect all related records before retrying; the importer never
deletes source files as compensation. Normal cancellation propagates after a
successful rollback.

Migration `20261002170848_PreserveImportedBackbones` adds the source columns and
backbone table. Downgrade refuses to discard populated provenance; export and
restore a pre-upgrade backup instead. EF commits migrations individually, so test
and operate each downgrade boundary explicitly.

## Evidence

`ApplicationImportNodeTests` compares the frozen G0 four-sequence fixture against
its independently authored business groups and lifecycle addresses. It also
covers equal-attribute siblings, missing manufacturer metadata, nested and
same-title extensions, encoded hrefs, custom regional sources, failed sequence
isolation, and memory cancellation/failure.

`PostgresApplicationImportTests` exercises the same real PDF/XML fixture, node
and source persistence, revision advancement, ambiguity, failure/cancellation
rollback, an injected rollback failure with independent timeout, and downgrade
protection. `PostgresCtdMigrationTests` verifies empty and legacy databases through
the independent migrator. Windows execution is recorded in the local progress
log; Linux execution remains NotEvaluated until the CI matrix runs.
