# Node workspace paths and moves (P1-05)

`ich-3.2.2-node-paths.json` pins the base directories of all 158 ICH M2-M5
definitions. The source is the ICH 3.2.2 PDF identified by SHA-256 in
`standards-scope.json`, Appendix 3 Tables 3-2 through 3-5 and Appendix 4.
The older workspace section resolver contains a partial, differently named
tree; it remains the legacy resolver. Node allocation uses this separate
versioned profile. Existing files are not migrated to the new layout.

Several XML sections deliberately share one directory (for example 2.3.S and
2.3.P use `m2/23-qos`). XML hierarchy must not be inferred from directory depth.
Instance segments are added at repeatable definitions or extension nodes, not
at every XML level. A persisted segment does not change when a title or sort
order changes. The development fixture's nine ICH leaves independently check
the exact resulting hrefs, including both manufacturers and M4/M5 study groups.
Regional acceptance of extensions remains subject to the qualifications in
`standards-scope.json`; this path profile does not establish regulatory readiness.

For example, Alpha's specification is allocated to:

```text
m3/32-body-data/32s-drug-sub/drug-a-alpha/32s4-contr-drug-sub/32s41-spec/specification.pdf
```

Segments use lowercase ASCII letters, digits and separated hyphens, at most
64 characters. New file names follow the existing file-naming rule, including
the 64-character file-name and 230-character sequence-relative path limits.
Reserved device names, links/reparse points, case variants, existing files,
directories used as files, files used as directories and database reservations
are checked before a move. Collisions never trigger automatic overwriting or
an unreviewed suffix. These are conservative development limits shared with
the current validation rule; they are not newly verified regional criteria.

## API and compatibility

`PUT /api/document-placements/{id}/section` now accepts `nodeInstanceId` and
optional `sortOrder` with `expectedRevision`. The supplied `ctdSection` must
match the selected node. Legacy unbound moves retain the existing route.
`POST /api/document-placements/{id}/section/preview` accepts the same payload
and returns source/target paths and revision without changing files or records.
Execution repeats preflight under the workspace lock; a preview cannot reserve
a revision or bypass a later conflict. DTOs return node identity and sort order;
the OpenAPI snapshot and generated TypeScript contracts are updated together.

A same-instance reorder preserves its existing href, including imported paths.
Delete leaves can be reordered without a physical file. Cross-instance moves
of lifecycle operations, referenced historical leaves, or shared physical
documents are rejected. A successful move updates document path, node binding,
section, order and revision together, retaining leaf ID and file bytes. The
response contains the placement captured under the mutation lock.

## Failure and restart recovery

Before moving a file, a write-through prepare record is atomically published
under `<application>/.ratools/node-moves/<placement-id>.json`, outside sequence
payloads. It binds workspace/revision, old/new node state, paths, length and
SHA-256. A database failure attempts physical restoration using an independent
30-second cancellation token. If restoration fails, reconciliation preserves
the actual new path and node binding and advances the revision. A storage call
throwing after its physical rename is handled in both directions.

Incomplete compensation retains the journal. Mutations, node writes, snapshots
and package preparation reject workspaces with pending records. With durable
PostgreSQL storage, a hosted recovery service runs before publish workers and
HTTP acceptance: it verifies file content and keeps the path dictated by the
committed database state. Missing/duplicate/changed files or inconsistent
records stop recovery without overwriting data. Retrying recovery is safe.
InMemory storage does not preserve application records across process restarts;
startup recovery is consequently limited to durable storage.

Node creation/editing UI and node-aware import/writers remain P1-06 through
P1-08 work. This change supplies their path/move consumer and does not claim
the complete two-manufacturer publishing milestone.

## Verification

The dedicated tests are `CtdNodePathTests`, `CtdNodeMoveTests`,
`PostgresNodeMoveTests` and `NodePlacementMoveApiTests`. They exercise physical
files, transactional memory repositories, real PostgreSQL and HTTP routes.
The path/file cases carry the `PathSecurity` category consumed by the existing
Windows/Ubuntu CI matrix. Local receipts are retained under
`.artifacts/publisher-g0/test-results/p1-05-*`.

Windows execution is verified locally. A Linux run has not been observed in
this session and remains **NotEvaluated** until actual CI evidence is retained.
