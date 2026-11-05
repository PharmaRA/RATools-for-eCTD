# Regional metadata revisions

P6-03 implementation and local Windows verification, 2026-11-06. This item
provides typed regional drafts, explicit defaults/context inheritance and
immutable persistence. Regional forms/API, import, XML writers and profile
qualification continue in P6-04 through P6-09. The P6-02 Formal admission gates
remain in force; this implementation does not activate a regulatory profile.

## Model and evidence

`RegionalMetadataContent` is a versioned US/EU tagged union. Unknown schemas,
regions, members, duplicate properties and case aliases are rejected. Typed
inputs have a global 2 MiB byte budget, bounded collections and XML-safe text.
Incomplete business drafts retain entered values with precise field diagnostics.

US contacts preserve stable IDs and ordered typed phones/emails. EU metadata
preserves ordered envelopes, each with its own applicant, tracking numbers,
invented names, INNs and related sequences. Equal business values remain distinct
when they have different entry identities. Nested lists are frozen without
sorting or deduplicating business values.

Application defaults use a separate reusable-field model. Explicit inheritance
copies an exact defaults revision and digest while preserving sequence fields.
Subsequent defaults edits do not change a sequence or historical revision.
Field provenance follows stable entry identities when arrays are reordered.
Metadata readiness requires complete diagnostics and explicit confirmation;
stored drafts do not acquire regulatory evidence by being non-null.

Node contexts belong to an exact application/sequence/node. Document contexts
belong to a placement, including its exact bound node, rather than to a shared
DocumentId. Copying node defaults requires an explicit source revision and
target placements. It preserves existing placement order and records the source
revision/digest for every copied value, including an explicit null that clears
a prior value. Later edits do not dynamically re-inherit node values.

The acquired EU M1 3.1.1 implementation guide, page 24, distinguishes related
sequences: initial/reformat explicitly select the current sequence; other units
select an earlier sequence. All references must exist in the same application.
No current sequence is silently filled in. Envelope/document country scopes and
other code lists use captured DTD resources. This is pinned-version validation,
not a claim about the latest regulatory standard.

Legacy upgrade retains the original shared metadata JSON and canonical digest.
EU dynamic writer defaults are not treated as stored user facts or materialized
as confirmed envelopes. Replaying an upgrade returns the exact original revision
without a second workspace increment. Shared legacy values remain readable.

## Persistence and concurrency

Migration `20261105200000_AddRegionalMetadata` adds revision, head, ordered node
context and ordered document context tables. Composite foreign keys enforce
application/sequence/node/placement scope and application defaults inheritance.
Inheritance checks explicitly handle SQL null semantics so a null source scope
cannot bypass the defaults foreign key.

Revision and context rows are append-only. Database triggers reject update,
delete and truncate; heads can only advance one revision within the same scope.
Document context inserts must match the actual placement's bound node. Downgrade
refuses evidence removal. Reads verify serialized bytes, canonical revision
identity, scalar columns and every ordered context projection.

Sequence writes use the workspace lock, expected revision, finalization guard
and a single transaction for archive, head, contexts and workspace revision.
Application defaults use an application lock and expected defaults revision.
Same-ID exact replay returns the original archive under independent connection
races; conflicting changes fail. Failures after append roll back all projections.

## Verification

Fresh evidence: `.artifacts/current-head-review-20261104/p603-regional-stage3/`.

| Evidence | Result |
| --- | --- |
| Four current C# layers with recommended analyzers | Passed |
| `p603-core-joint-model-final.log` | 32 passed: 18 model/service, 13 independent integration, 1 persistence model |
| `p603-migration-apply-repeat.log` | Apply, repeat and runtime model parity passed |
| `p603-postgres-final9b.log` | 9 real PostgreSQL tests passed |
| Independent read-only implementation and migration review | Passed after fixes |

PostgreSQL cases cover independent connection CAS and same-ID races, ordered
round trips, exact inheritance, two placements of one document, injected rollback,
composite foreign keys, append-only guards, null inheritance scope, head rewind,
placement/node mismatch and legacy replay. Strict JSON, stable-ID provenance and
explicit null-copy source regressions were observed failing before their fixes.

This is targeted Windows/domain/service/PostgreSQL verification. It is not a
whole repository regression run, browser acceptance, Linux qualification or an
external regional validator comparison. Test inputs and auxiliary migration
evidence are retained under `.artifacts`; tests do not delete evidence.
