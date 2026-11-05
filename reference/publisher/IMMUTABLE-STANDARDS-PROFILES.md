# Immutable regional standards profiles

P6-02 implementation and local Windows verification, 2026-11-05. This provides
immutable evidence and admission gates. It does not activate FDA/EU regulatory
qualification, introduce confirmed regional business metadata, or replace the
remaining P6-03 through P6-09 work.

## Identity and retained resources

`StandardsProfileSnapshot` is content addressed as `sha256:<64 lowercase hex>`.
Its identity covers the regional-neutral version descriptor, lifecycle, explicit
business scope and evidence, provenance and every resource's path, role, media
type, byte length and SHA-256. Stored resources include the provider descriptor,
DTD/XSL bytes, XML asset inventory, node schema/paths and rule catalog. Captured
vocabulary source constraints remain explicitly incomplete business evidence.
Acquisition time is retained on the first insert and does not change content
identity on repeated capture.

The service bounds resource count, each stream and the aggregate capture, rejects
nonportable or conflicting paths and link traversal, and verifies stored bytes
on read. Verified source/evidence declarations require valid captured digests and
consistent acquisition/verification dates; a status string alone is insufficient.
Provider and XML inspection assets must agree in category, resource path, delivery
address and SHA-256 before they can authorize inspection or capture. An inconsistent
new bundle can be retained for review, but cannot silently supply two different
standards to P2 and P4.

PostgreSQL uses an additive `standards_profile_snapshots` table with content and
projection verification, exact insert replay and guards on update, delete and
truncate. The migration refuses evidence-removing downgrade. In-memory storage
has matching immutable read/replay behavior. No test deletes evidence.

## P2 and P4 binding

The existing `ProfileSnapshotId` is retained as the rule catalog alias. The
separate optional `StandardsSnapshotId`, `SelectionDate` and
`StandardsBusinessContext` bind the actual standards content through queued
request, validation binding, report and release manifest. Each historical selection
can carry its own exact content identity. Missing fields in legacy JSON are omitted
from new canonical projections, preserving recorded identities.

New production P2 runs resolve a standards snapshot once. Same-key response-loss
replay returns that exact prior selection before consulting today's provider.
Checks receive the captured rule catalog, DTD resolver assets and node schema;
historical sequences use their own contexts. Legacy queued history retains its
archived baseline and revalidates its original evidence rather than silently
changing trust during an upgrade.

P4 constructs the profile from archived bytes and captures resources at the
verified delivery addresses. It preserves controlled imported regional backbone
paths. If the installed writer cannot reproduce an older node schema, a new
capture is explicitly rejected; existing committed release viewing/export and
idempotent finalization replay continue to use archived content.

New Formal requests check activation, dates, actual application template, business
scope and verified evidence at admission and capture. New runs cannot backdate
their selection to bypass retirement. A report claiming ready does not replace
these checks. Persisted regional business confirmation remains pending P6-03.
Bundled FDA and EU profiles remain trial evidence, with concrete missing evidence
and activation reasons. No production Active state is created by synthetic tests.

## API

| Endpoint | Behavior |
| --- | --- |
| `POST /api/ectd-templates/{templateKey}/standards-snapshot` | Capture or replay the current template bundle; returns metadata without resource bodies |
| `GET /api/standards-snapshots/{snapshotId}` | Read and verify the stored immutable descriptor and resource inventory |
| `GET /api/standards-snapshots/{snapshotId}/resources/{resourceKey}/content` | Read independently verified bytes, with download disposition, strong ETag and Range support |

Clients cannot activate a profile or submit evidence through these endpoints.
Finalization returns `409 / FINALIZATION_EVIDENCE_INVALID` for inconsistent stored
evidence. OpenAPI and generated TypeScript include the optional binding fields;
the validation detail view displays structured business context as field values.

## Verification

Current compiler and logs:
`.artifacts/current-head-review-20261104/p602-snapshot-stage8/`.

| Evidence | Result |
| --- | --- |
| Four current C# layers with recommended analyzers and warnings as errors | Passed |
| `p602-core-final.log` | 14 passed |
| `p602-integration-final13.log` | 13 passed |
| `p602-final-acceptance.log` | 54 passed: 4 PostgreSQL, 1 actual HTTP, 1 controller error mapping, 44 existing domain/report cases, 4 schema/export checks |
| Migration apply, repeat and EF runtime model parity | Passed |
| Generated-contract consumers | Targeted TypeScript and ESLint passed |

The independent integration tests retain actual bytes and use production P2
inspection/report archival plus P4 planner/capture/finalization services. Reader
and release storage adapters retain evidence; some tests use a fixed package
builder. The imported regional address case uses the actual package builder.
PostgreSQL and HTTP evidence are separate. This is not a whole repository test
run, Linux qualification, browser acceptance of regional forms, production release
store qualification or external regulatory comparison.

Fixed legacy vectors were recorded against `d9dbd4e` before the optional fields
existed: `.artifacts/p602-legacy-baseline/evidence.json`. Tests compare literal JSON
and digests. Upgrade, old-history, contradictory asset and path cases failed before
their respective fixes, then passed on the fresh final layer. Actual PostgreSQL
tests use two connections for identity races and verify rejected updates; forbidden
delete/truncate paths are verified through trigger definitions without execution.

P2/P4 integration artifacts:
`.artifacts/p602-integration/5356248a21d54616878456fd4f1ddbbc/`.
Legacy queued-history artifacts:
`.artifacts/p602-integration/408eb680b2344129be8c9663208377c5/`.
Per-sequence history resources:
`.artifacts/p602-integration/b7ef3ca0abc146d0a033a98c0291c370/`.
All implementation findings were fixed and independently re-reviewed read-only.
