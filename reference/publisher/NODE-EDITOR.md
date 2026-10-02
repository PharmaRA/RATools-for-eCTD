# CTD node editor (P1-08)

The workspace now presents actual sequence instances alongside available section
definitions. Manufacturer/substance, product/dosage and indication attributes come
from the pinned schema. A definition placeholder creates an instance beneath its
specific parent; files attach to the instance ID. The schema decides whether a
node accepts leaves, including ICH containers which explicitly allow leaves before
their defined children. M1 continues to use its existing regional UI until P6.

The node panel provides schema-controlled attributes, required fields and choices,
stable directory segments, titles, sorting, empty-node removal and structure-only
copying. Copying first previews the new directory tree and checks business/path
conflicts, then creates new identities without copying files. Existing identity
attributes cannot be edited in place. An earlier sequence's structure can instead
be included with its original identities and independent sequence metadata.

Document selection shows business context and offers an explicit node binding and
sort position. Pointer and keyboard moves target node keys. Unbound legacy files
remain visible for explicit mapping; incomplete nodes and import diagnostics remain
visible. Existing historical-reference and file-move safeguards still apply, so an
ambiguous history cannot be silently reclassified. Missing directory metadata must
be completed before allocating new files. This UI does not perform an automatic
bulk repair of ambiguous historical identities.

Node-aware uploads keep the validated filename in the allocated instance directory.
Existing files, case variants, links and persisted path collisions are rejected.
Alpha and Beta can each receive `specification.pdf` without overwriting each other.
Batch upload uses each successful response's revision for the next request.
Lifecycle candidates and explicit server-side targets stay within the same business
node; delete leaves and current/future sequences cannot be selected as targets.

## API and revision semantics

`GET /api/applications/{id}/sequences/{seq}/nodes` returns the schema, sequence
nodes, metadata issues, diagnostics and revision. The existing workspace snapshot
includes the same node projection under its shared read lock, so displayed nodes
and placements belong to one revision.

Under that node route:

| Method / suffix | Behavior |
| --- | --- |
| `POST /` | Create an instance or reinclude an exact existing resolved identity absent from this sequence |
| `PUT /{nodeId}` | Edit sequence attributes, title or order; preserve allocated directory and business identity |
| `DELETE /{nodeId}` | Remove only an empty sequence node; retain its application identity and history |
| `POST /{nodeId}/clone/preview` | Check a structure copy and return proposed nodes/directories without writing |
| `POST /{nodeId}/clone` | Create the copied business subtree at the checked revision |
| `POST /inherit` | Include missing nodes from an explicitly selected earlier sequence |

Writes require `expectedRevision`; missing revisions produce 428 and stale
revisions 409. Preparation runs under the application lock, and repository saving
compares the same revision in its own transaction. An edit between preparation and
saving therefore invalidates the operation. Node failures include stable codes and
the affected node ID. OpenAPI and generated TypeScript describe these contracts.

Formal immutable snapshots and their reference constraints remain P4. The node
editor does not claim regulatory validation or implement P3's complete cumulative
lifecycle projection.

## Verification

Windows verification: 764 backend tests passed without skips, including real
PostgreSQL creation/copy/inheritance/history isolation and HTTP upload/binding/
cross-node target rejection. All 445 frontend tests passed with four workers;
build, bundle budget, lint, API contract, EF model drift and publisher source
contract checks passed. The first unrestricted frontend run hit an unrelated lazy
route's three-second loading deadline under concurrent backend load; the complete
four-worker run passed. Linux execution remains NotEvaluated.

`scripts/browser/verify_node_editor.mjs` uses a real Chromium browser against local
isolated API/frontend servers. Only application/sequence setup uses direct API
writes. Node creation, attribute entry, clone preview/commit, both PDF uploads,
historical inclusion and Alpha-only replacement use the operator interface. It
reads the resulting workspace to assert identity, target IDs and original PDF
bytes, and records screenshots plus JSON evidence.

Run from the repository root with Playwright available. Set
`RATOOLS_PLAYWRIGHT_MODULE` to its module name or installed module file URL if it is
outside normal module resolution. Configure `RATOOLS_BROWSER_WORKSPACE_ROOT` as an
allowed disposable workspace parent on the isolated backend, and
`RATOOLS_BROWSER_API_KEY` if that backend requires a key. The default origins are
`http://127.0.0.1:3000` and `http://127.0.0.1:5000`; override them using
`RATOOLS_BROWSER_ORIGIN` and `RATOOLS_BROWSER_API`.

```text
node scripts/browser/verify_node_editor.mjs
```

The script creates one synthetic application and two sequences, keeps its test
files for inspection, and writes evidence beneath `.artifacts/node-editor-browser`
(override with `RATOOLS_BROWSER_OUTPUT`). Current run evidence is
`.artifacts/publisher-g0/p1-08-browser`; backend results are
`.artifacts/publisher-g0/test-results/p1-08-final.trx`.
