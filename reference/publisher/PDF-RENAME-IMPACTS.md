# PDF rename and move impacts — P5-07

The workspace batch panel previews PDF filenames and business-node moves against
the final paths of the entire batch. It lists referring documents and requires
explicit confirmation before processing those documents. A historical or otherwise
non-editable referring source blocks the affected plan. Original captures and
previously finalized files are retained.

## Plan and execution

The server captures the application inventory, source versions and byte hashes,
placement identities, target business nodes, expected revision, engine version and
expiry. The plan digest binds this information and the confirmed source set. The
client submits the stored plan identity and digest, rather than a replacement graph.
Changing the inventory, including adding a referring document, invalidates the
preview for execution or adoption.

Incoming references to renamed targets and outgoing references from moved sources
are resolved against the final batch graph. PDF file specifications are literal
filenames; relative URI actions use URI encoding. Rewriting preserves the action
type and its page or named destination. Every changed or affected document becomes
a separate immutable result version. All results must pass inspection and final
graph validation before the batch can be adopted.

The durable worker uses a lease, heartbeat and fenced completion. Execution and
adoption are distinct: downloading checked results does not modify the draft.
Adoption prepares independent working copies and commits all document/placement
changes, the audit record and one workspace revision in a transaction. Placement
and leaf identities are retained. Replaying the same adoption returns its original
result; an incomplete output set cannot be adopted.

Existing direct filename and node-move paths also check PDF impacts. Operations
with affected references must use the reviewed batch workflow. Source versions,
results and failed attempt evidence remain available; this workflow introduces no
automatic permanent file cleanup.

## API and UI

Routes use the base
`/api/applications/{applicationId}/sequences/{sequenceNumber}/pdf-renames`.

| Method/path | Purpose |
| --- | --- |
| `POST plans` | Preview changes and explicit referring-source confirmations |
| `GET plans/{planId}` | Read the stored preview in its application and sequence |
| `GET plans/{planId}/job` | Recover an existing job after losing its enqueue response |
| `POST jobs` | Enqueue or replay a plan/digest/idempotency-key request |
| `GET jobs/{jobId}` | Read progress, outputs and adoption |
| `POST jobs/{jobId}/cancel` | Request cancellation |
| `POST jobs/{jobId}/adopt` | Adopt the whole batch at the reviewed revision |
| `GET jobs/{jobId}/placements/{placementId}/content` | Read a checked result with Range and ETag |

The UI saves the complete request before enqueueing. Recovery first queries the
plan's job; a missing job can be re-enqueued only while the workspace remains
editable. Request identity checks prevent an older tab from clearing or replacing
a newer task handle. Starting another batch clears the previous source confirmations.
Source labels include the sequence and full delivery href, including historical
sources with the same filename.

After a revision change, query and download remain available, while stale adoption
is disabled. Downloads use the authenticated API and compare SHA-256. Successful
adoption invalidates workspace validation and refreshes the document list.

## Acceptance evidence

Windows implementation and scoped local acceptance completed on 2026-11-05:

- 61 affected backend cases passed: 20 rename cases and 41 existing processing
  regressions. These include real qpdf, simultaneous moves into different business
  nodes, direct-entry guards, rollback, replay and actual HTTP background-worker,
  Range, scope, cancellation and timeout behavior.
- Five actual PostgreSQL cases passed, including independent-connection enqueue,
  claim/recovery/fencing, full adoption/rollback and corrupt candidate quarantine.
  Two migration checks passed: the reviewed migration applied, a repeat was a
  no-op, and the EF model had no pending changes.
- The two PDF panels' 27 tests passed, including 11 batch UI cases for response-loss
  recovery, concurrent-tab protection, new confirmations, read-only transitions,
  historical labels and adoption calls. Targeted TypeScript and ESLint passed.
- Three nullable-response schema regressions and actual-controller contract export
  passed. The generated contract adds eight rename routes, preserves all existing
  paths and keeps adoption and validation-coverage reference types nullable.
- Four layers compiled with recommended analyzers and warnings as errors.
  Independent read-only review findings were fixed and re-reviewed.

Backend evidence is under
`.artifacts/current-head-review-20261104/p507-backend-final8/` and
`p507-backend-stage6/`; the final schema/export compilation and checks are under
`p507-contract-final9/` in the same parent directory. Detailed source/test mappings
are in the local `p5-07-backend-report.md` task report.

The pinned Windows qpdf build cannot open some long paths. Its input arguments use
an existing Windows short-path alias when necessary; this does not create links
or change stored paths. On volumes without a usable alias, deep paths fail
explicitly; use a shorter workspace root. Normal processing first captures sources
under `_document-versions`, outside the business directory hierarchy.

These are scoped local checks. Browser, Linux, OCR, large-document and regulatory
qualification remain outside this result; P5-08/09 and P6 keep their own acceptance.
