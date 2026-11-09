# P6-09 declared regional qualification matrix

QA-07 source update, 2026-11-09: [current FDA/EMA originals](QA-07-OFFICIAL-SOURCES.md)
have now been acquired and frozen. FDA publishes criteria 4.7; the criteria 4.5
rows below retain the historical trial scope. Current source acquisition does not
upgrade these rows or bind a new production snapshot. Rule mapping, the six
pending rows and independent business-validator comparison remain open.

This matrix records the locally exercised development/trial scope. It is an
evidence index, not activation of a regulatory profile. A selected row can be
observed in a completed report while Formal finalization remains blocked by the
immutable snapshot gates, criteria inventory or external comparison evidence.

| Region / profile | Application and operation | Country / language | Sections | Local status | Formal status |
| --- | --- | --- | --- | --- | --- |
| US M1 3.3 / criteria 4.5 | NDA-like synthetic application; new documents with explicit metadata | US / en | m1.1 forms, m1.2 cover metadata | Selected trial; positive and negative bounded XML cases | Blocked: current 4.7 originals acquired; new snapshot binding, complete rule inventory and external qualification pending |
| US | Promotional m1.15 | US / en | m1.15 | Explicitly pending / NotEvaluated | Blocked |
| EU M1 3.1.1 / criteria 8.2 | MAA-like national application; new documents, initial/reformat/response reference rules | DE / de, FR / fr, BE / fr | envelope, PI contexts and related sequence | Selected national trial; repeated values and contexts exercised | Blocked: complete criteria and external comparison missing |
| EU | Other countries, procedures or language combinations | Outside DE/FR/BE national trial | Other M1 scopes | Unsupported or NotEvaluated with explicit findings | Blocked |
| EU | Empty cover without a real authoring placement | Any | Cover | Unsupported by current authoring schema | Blocked |

## Evidence chain

P6-05 retains the unmodified EMA M1 1.4 XML, archive hash and source URL for
parser/archive evidence. It is not current 3.1.1 output qualification. P6-07's
independent hand-written DE/FR/BE XML and the libxml2 comparison contain three
valid outputs and six rejected structural mutations; this is structural DTD
evidence, not an independent regional business validator. The FDA source check
recorded both registered URLs as HTTP 404 on 2026-11-06, so the FDA current
source and business criteria remain unverified.

P6-08's local matrix adds six ProjectPolicy business checks, exact selected
history/catalog binding, new engine identities, source locations and explicit
unknown/fail outcomes. Its evidence is recorded in
`REGIONAL-VALIDATION-GATES.md` and
`.artifacts/current-head-review-20261104/p608-backend-stage5/`.

P6-09 verification must cite separate counts for backend matrix cases, actual
HTTP, PostgreSQL, browser, DTD/tool comparison and static checks. Overlapping
counts must not be summed. A skipped external validator, Linux run, or browser
case stays pending. Manual status cannot change Missing or Unverified evidence
to Verified, and an Active snapshot cannot bypass these conditions.

The final integration fix wave covers three confirmed local issues from the
frozen P5/P6 review: defaults and sequence snapshots must remain separate,
legacy upgrade must return and display its newly captured snapshot, and in-memory
assembly completion must fence an independently accepted cancellation at commit.

## Roadmap scenarios

| Case | Concrete acceptance path | Boundary |
| --- | --- | --- |
| R-01 | FdaM1EndToEndTests actual HTTP and new PostgreSQL connection; independent form attributes, full output/reimport | Synthetic scoped FDA technical input; promotional branches pending |
| R-02 | EuM1EndToEndTests DE/FR envelopes, Belgian cover and nondefault PI contexts | National trial combinations; no inferred defaults |
| R-03 | Repeated tracking values, names, INNs, two same-file placements and source wrapper order | IDs/order and actual context checked separately |
| R-04 | PackageRegionalInspectionTests future, malformed, missing, foreign and wrong-address history | Old history catalogs do not acquire current rules; target association still checks actual identity/address |
| R-05 | Scoped writer/readiness and bounded regional negative tests | Missing fields retain field/node/source locations |
| R-06 | RegionalMetadataIntegrationTests legacy upgrade; browser upgrade into a newly captured snapshot | LegacyDefaultsUnconfirmed remains visible and blocking |
| R-07 | RegionalActivationAcceptanceTests actual Formal HTTP refusal and PostgreSQL active-only attempt | Active status and matching synthetic scope cannot bypass missing evidence |
| R-08 | StandardsSnapshotIntegrationTests literal historical digests, archived resources/catalog and release bytes | Historical reports stay readable; incompatible queued engine must be recreated |
| R-09 | Unmodified EMA 1.4 import evidence plus current synthetic writer/reimport chains | Explicit version mismatch/unknown diagnostics; no current official submission claim |
| R-10 | Shared PDF rule mappings and per-sequence captured catalog severity tests | Project parameters/severity source declared; no invented regional criteria |
| R-11 | Independent libxml2 positive/negative DTD comparison and external evidence gate tests | Complete regional business validator comparison remains pending and Formal blocked |

The browser additionally exercises separate sequence/defaults snapshots and the
legacy upgrade response with actual controllers and services. PostgreSQL is
exercised separately; an in-memory browser host does not prove database behavior.

## Final local execution evidence

Base commit: `e36748e` (P6-08). Production analyzed layer:
`.artifacts/current-head-review-20261104/p609-fixwave-compile/`.
The final four-layer analyzer check is retained in
`.artifacts/current-head-review-20261104/p609-final-analyzed-compile.log`.

| Run | Result | What it proves |
| --- | --- | --- |
| `p609-matrix-final.log` | 96 passed / 0 failed | Five supported rows drive typed metadata/writer, DTD, import parser and exact-snapshot independent inspection; six pending rows stay explicit |
| `p609-service-binding-final2.log` | 146 passed / 0 failed | Regional model/service/binding and writer regressions; 45 writer cases overlap other runs |
| `p609-parent-e2e.log` | 9 passed / 0 failed | Actual US/EU HTTP saves, new PostgreSQL connection output, DTD/full reimport/capture, precise EU rejection and independently sourced EMA parser evidence |
| `p609-parent-assembly-pg.log` | 22 passed / 0 failed | 15 memory/workflow and 7 actual PostgreSQL assembly cases, including accepted outside cancellation rollback and refusal of completion outside the workspace transaction |
| `p609-parent-snapshots.log` | 21 passed / 0 failed | Exact standards HTTP/PG evidence, Formal refusal reasons, active-only negative case, old resources/requests/manifests and historical replay |
| `p609-browser-final-host.log` | 19 passed / 0 failed | Retained regional integration/HTTP fixture and actual browser service host |
| `.artifacts/p609-browser/final.json` | 2 browser scenarios passed, 0 page errors | Defaults B and sequence A save/preflight/render/reload identities; legacy upgrade captures C, refreshes revision and preserves defaults B |
| `.artifacts/p607-independent/libxml2-comparison.json` | 3 outputs valid, 6 mutations rejected | Independent libxml2 DTD structure check on current actual HTTP/PG outputs; no regional business qualification claim |
| Regional panel/fields/import action frontend tests | 25 passed / 0 failed | Historical schema handling, strict ordinary-save identity and nullable import fieldPath DTO fixture compatibility |
| Full `tsconfig.app.json` TypeScript and changed-file ESLint | Passed | All application TypeScript roots compile; changed frontend files meet lint rules |

Versioned matrix input is
`tests/RATools.Tests/Fixtures/RegionalQualification/p609-declared-matrix.json`
(11 rows: five selected, six pending). Its byte SHA-256 is
`e574ec370ee005bf89c22458f837bc58596bae666be12ee016ce5ef807fe7da9`.
The committed evidence is under
`reference/publisher/regional-qualification/windows-20261106/`: the matrix
receipt, five actual XML outputs and independent libxml2 comparison. The receipt
binds exact snapshot IDs, rule/engine versions, matrix/test assembly/test source
hashes, input hashes, regional revision digests and actual Formal refusal
reasons. Complete captured snapshot bytes remain in the append-only local
receipt directory named in the implementation report.

The browser first reproduced both snapshot defects. Unit tests also observed
the two UI failures before repair. The memory assembly race was reproduced by
pausing revision advance and cancelling from a separate execution context. A
completion commit now holds the store's gate while checking committed job state
and applying the whole transaction; a rejected fence leaves documents,
placements, result and workspace revision uncommitted. PostgreSQL already uses
the workspace transaction and row update fence.

The selected backend runs use the retained source compiler and explicit test
runner instead of cleanup-owning full-suite fixtures. The relevant commands are:

```powershell
.artifacts/p507-backend/Run-Tests.ps1 -LayerLabel p609-fixwave-compile -Label p609-parent-e2e -Sources @(
  'tests/RATools.Tests/Applications/RegionalImportAcceptanceTests.cs',
  'tests/RATools.Tests/Publishing/UsRegional/FdaM1EndToEndTests.cs',
  'tests/RATools.Tests/Publishing/EuRegional/EuM1EndToEndTests.cs',
  'tests/RATools.Tests/Publishing/EuRegional/EuPublishingErrorsHttpTests.cs')
.artifacts/p507-backend/Run-Tests.ps1 -LayerLabel p609-fixwave-compile -Label p609-parent-assembly-pg -Sources @(
  'tests/RATools.Tests/Assembly/DurableAssemblyTests.cs',
  'tests/RATools.Tests/Assembly/PostgresAssemblyTests.cs')
python .artifacts/p607-independent/compare-dtd.py
node .artifacts/p609-browser/build.mjs
python .artifacts/p609-browser/verify.py final
```

The PostgreSQL runs use the dedicated `RATOOLS_TEST_POSTGRES` environment
variable. The browser verifier runs against its separately started test host.
Labels and retained directories identify this execution; repeat runs use fresh
labels/inputs and preserve previous evidence. Matrix receipts separately bind
their exact input bytes, snapshot IDs, catalog digest, engine identity, runtime
and test assembly.

No full repository suite, Linux run, current FDA official qualification, full
regional criteria or independent regional business validator was executed as
part of P6-09. Those dependencies stay visible and Formal remains blocked.
