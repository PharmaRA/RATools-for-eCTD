# Multi-instance round-trip regression (P1-09)

The four hand-authored publisher sequences now pass through import, real backbone
generation and PDF copying, independent directory/ZIP capture, XML/DTD inspection,
historical reference replay, reimport and a second publication. This connects the
P1 node model and writer to the P2-01–04 minimum independent validation chain.

`MultiInstanceRoundTripTests` checks both actual writer-produced directories and
ZIPs against `Fixtures/Publisher/expected.json`. It verifies all four effective
document sets, exact replacement/append/delete targets, node ancestry and identity
attributes, extension titles, document-scoped IDs, and separate same-named PDFs for
Alpha and Beta. Directory and ZIP file inventories agree while their exact input
bindings remain distinct. The second publication preserves these facts, including
payload checksums. A modified Alpha-to-Beta reference fails independent context
validation and reimport leaves no partial sequence nodes or placements.

`PostgresNodePublishingTests` runs the same independent delivery assertions after
loading persisted nodes through a real PostgreSQL context. Existing persistence,
editing, path, backfill and compensation tests remain part of the full regression.

| P1 scenario | Executable evidence |
| --- | --- |
| N-01, N-03, N-05: distinct manufacturers, precise lifecycle targets, preserved import structure | `Publishing/MultiInstanceRoundTripTests.cs`, `Applications/ApplicationImportNodeTests.cs`, `Persistence/Postgres/PostgresNodePublishingTests.cs` |
| N-02, N-04, N-06: missing metadata, stable sorting, ambiguous identities | `Publishing/NodePublishingTests.cs`, `Applications/ApplicationImportNodeTests.cs`, `Domain/CtdNodeInstanceTests.cs` |
| N-07: sequence rollback on invalid history and write failures | `Publishing/MultiInstanceRoundTripTests.cs`, `Applications/ApplicationImportNodeTests.cs` and PostgreSQL import tests |
| N-08: repeatable legacy backfill without invented metadata | `Applications/LegacyNodeBackfillPlanTests.cs`, `Persistence/Postgres/PostgresCtdNodeTests.cs` |
| N-09–11: revision conflicts, filesystem boundaries, historical identity protection | `Workspaces/CtdNodeMoveTests.cs`, `Workspaces/CtdNodeEditingTests.cs`, corresponding API and PostgreSQL tests |

Paths in this table are relative to `tests/RATools.Tests`. The existing P1-08
Chromium exercise (`scripts/browser/verify_node_editor.mjs`) and frontend node,
workspace and lifecycle tests provide UI evidence; this task does not repeat the
browser exercise or claim new UI features.

## Qualification boundaries

The importer currently derives application number from the selected directory
name. The same-application round-trip therefore uses separate temporary parents
with the same explicit `000001` application directory. Inferring identity from
regional administrative XML and checking its compatibility remains regional work.

An import or successful publish job is not historical trust evidence. These
integration tests deliberately retain `Unverified` baseline entries, assert the
resulting incomplete `HISTORY-BASELINE` status, and separately check the observable
lifecycle facts. They do not manufacture validation or finalization record IDs.

The fixtures, standards profiles and all verdicts remain development scope.
Regional acceptance rules, advanced append behavior, external comparison evidence,
PDF/link verification and immutable finalization remain their roadmap tasks.
Windows/PostgreSQL receipts demonstrate the local implementation; Linux path and
runtime qualification still requires an actual Linux run.
