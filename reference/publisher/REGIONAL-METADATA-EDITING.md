# Regional metadata editing

P6-04 implementation and local Windows verification, 2026-11-06. Operators can
edit ordered regional metadata and application defaults, preflight unsaved inputs,
review exact inheritance differences and save at an expected workspace revision.
The P6-02 qualification and Formal gates remain active. Regional import, FDA/EU
writers, regional rules and independent profile qualification continue in P6-05
through P6-09.

## Editing and contracts

The sequence workspace contains an on-demand regional metadata panel. Its schema
comes from the selected immutable standards snapshot, with recursive fields,
ordered collections, captured controlled values and resource/hash references.
There is no separate frontend country, agency, program or phone vocabulary.
Fields without verified controlled business vocabularies remain explicit text
with qualification diagnostics. Condition effects distinguish guidance, visibility
and requiredness. A response's related-sequence input remains editable; node/leaf
conditions use the actual selected CTD section without adding it to the payload.

Changing country, procedure or applicability preserves existing inputs. Ordered
envelopes/contacts/names use stable entry IDs; document contexts select actual
placements and bind their real node. Defaults and sequence inputs are separate.
Fields show saved provenance or unsaved manual changes. Diagnostics can focus the
exact input, and preflight displays a summary of values intended for XML output.
The existing writer is not upgraded by this summary.

Existing publishing-metadata GET/PUT preserve shared fields and add an optional
`regional` object. A shared-only PUT preserves the regional head. A mixed PUT
commits shared values, immutable regional evidence and one workspace increment
in one transaction. Its response uses the exact committed shared values and
regional revision, avoiding a later writer's head. JSON requests reject unknown
members, duplicate properties, case aliases and client-supplied evidence. XML
forbidden characters return 400 without changing metadata or workspace revision.

Additional application routes under `/api/applications/{id}/regional-metadata`
provide schema, defaults GET/PUT/preflight and scope-checked revision reads.
Sequence routes under `.../publishing-metadata/regional` provide preflight,
inheritance preview/apply, node-context-copy preview/apply and legacy upgrade.
Mutation requests require their expected revision. Missing defaults return 404.
API/controller OpenAPI and generated frontend types are synchronized.

## Explicit inheritance and review

Inheritance and context copying display exact source revision/digest and concrete
before/after field differences before confirmation. Preview does not commit.
Changing a source node clears old placement targets. Unsaved sequence edits must
be saved or explicitly discarded before copying persisted sources. Later defaults
do not automatically update existing sequences.

An existing sequence head selects its own standards snapshot. Before the first
regional save, existing application defaults select their exact historical
snapshot, so first inheritance can use that source without silently switching
schema or reporting a committed mutation as failed. Scoped historical revision
reads expose the original values, contexts, provenance and snapshot identity.

Workspace revision changes preserve unsaved values and disable stale writes.
Conflicts and failed saves keep the draft visible. Successful mutations refresh
workspace validation/revision. No confirmation control can claim regulatory
verification or activate a profile.

## Verification

Fresh backend layer and evidence:
`.artifacts/current-head-review-20261104/p604-backend-stage4/`.

| Evidence | Result |
| --- | --- |
| Four current C# layers with recommended analyzers | Passed |
| `p604-review-round1-green.log` | 57 passed: 26 P6-04 and 31 P6-03 model/service/integration |
| `p604-http-postgres-final.log` | 20 passed: 5 actual HTTP, 2 real PostgreSQL and 13 direct integration |
| Frontend direct workspace/form regressions | 53 passed before the final historical-snapshot case |
| Final panel/field/value tests | 19 passed, including the historical-snapshot case |
| Final generated-contract consumers | Targeted TypeScript and ESLint passed |
| Independent task specification and quality review | Passed after fixes |

Counts overlap and should not be added. Actual PostgreSQL independently verifies
mixed save/read/CAS and injected failure rolling back shared values, regional head
and workspace revision. Actual HTTP verifies strict binding, revision errors,
preflight, precise source preview and forbidden XML input mapping.

Chromium evidence is retained in `.artifacts/p604-browser/evidence.json` and
`completed.png`. The actual panel talks to actual controllers/services backed by
retained in-memory repositories. It exercises nondefault country/procedure,
response-related sequence, ordered names, two independent placement contexts,
preflight without mutation, mixed save, defaults without implicit sequence edits,
explicit difference preview/inheritance, reload and original revision immutability.
The browser reported no page errors. PostgreSQL transaction evidence is separate.

Strict JSON, XML error mapping, deterministic later-writer response, condition
effects, target reset and historical-snapshot inheritance regressions were
observed failing before their fixes. This evidence does not establish a whole
repository regression run, Linux, new XML writer output, an external regional
validator comparison or regulatory activation.
