# Assembly and PDF workflow qualification

Verified on Windows on 2026-11-05. This is local development qualification for
P5-09. Linux, external regulatory validators, regional activation and formal
submission readiness remain outside this evidence.

## Durable assembly

Assembly now accepts bounded UTF-8 CSV and data-only XLSX, including explicit
source tokens, business node IDs, filenames, lifecycle targets and ordering.
Uploads register unmounted sources without advancing the workspace revision.
Source retrieval and stored preview rows have bounded pagination. The 1000-row
limit applies to each manifest, not the sequence's cumulative source inventory.

Previews retain input bytes and their SHA-256, all row diagnostics, source and
historical target identities and byte hashes, future paths, revision, expiry and
digest. Confirming an explicit valid subset creates a new immutable plan identity
and digest. Expired plans cannot be revived through selection. XLSX formulas,
external relationships, macros, malformed identities and expanded-size limits are
handled without executing spreadsheet code.

Execution uses durable jobs with idempotent recovery, cancellation, timeout,
leases, heartbeat and fencing. Prepared copies precede one transaction for new
documents, placements, audit and one revision. Source versions, input uploads and
frozen releases remain intact. The retired synchronous executor fails closed.

The UI supports source upload and explicit node mapping, CSV/XLSX input, template
and safe diagnostic export, valid-row selection, progress, cancellation and saved
request recovery. Finalized workspaces reject mutations. Generated OpenAPI and
TypeScript contracts describe the actual controllers.

## Evidence

The retained compiler and runner output is beneath
`.artifacts/current-head-review-20261104/p509-assembly-stage7/`:

| Evidence | Result |
| --- | --- |
| Four current C# layers, recommended analyzers and warnings as errors | Passed |
| `p509-assembly-stage7-regression.log` | 40 passed |
| Subsequent safe Assembly/HTTP/planner regressions | 22 passed, overlapping the earlier run |
| `p509-postgres-first.log` | 7 PostgreSQL cases plus 12 overlapping durable cases passed |
| Migration apply/repeat and EF runtime model comparison | No pending migration or model drift |
| `p509-contract-stage7.log` | 5 passed: actual controller export, nullable schemas and real directory/ZIP index MD5 |
| `p509-joint-stage7-final.log` | 2 passed |
| Assembly frontend components | 26 passed; targeted TypeScript and ESLint passed |
| qpdf base64 regression and affected engine cases | 19 passed |

Counts overlap and are not a full repository test total. Old tests with physical
deletion cleanup were not run. Affected planner tests were moved to the retained
fixture and durable service; unrelated cleanup was not changed.

The 1000-row sample used two manufacturer nodes, 500 rows each, and committed
exactly one revision. One recorded run took 18,329 ms with 56,281,856 managed bytes
at measurement. Failure on placement 999 rolled back documents, placements, audit
and revision, and the same execution then succeeded on retry. PostgreSQL tests
use independent connections for replay/conflicting identities, claim/fencing,
cancellation, atomic commit and rollback, corrupt-input quarantine and intake.

## Combined PDF workflow

`PdfCombinedQualificationTests` uses the actual assembly, PDF navigation/content,
rename, publication, P2 inspection/report archival and P4 capture/finalization
services. It verifies directory/ZIP byte parity, frozen payload and report binding,
then uploads the frozen bytes into a later sequence and creates a replacement
with a distinct leaf and the exact historical target. Leaf MD5 and index MD5
match the new bytes; the earlier frozen payload remains unchanged. Chinese
bookmarks and remote link destinations are independently reopened with PyMuPDF.

The run revealed and fixed two integration defects: JSON escaping of base64 `+`
in qpdf update input, and filename suffixes in generated `index-md5.txt`. Dedicated
regressions observed failure before each fix. Both publication and release capture
now emit the exact index checksum followed by a newline, matching P2's parser.

P2 capture and P4 disk storage use retained test adapters to avoid physical cleanup.
Their production inspection/planning/archival services run, but this combined test
does not qualify the production capture reader or release disk provider. It uses
in-memory repositories; PostgreSQL is verified separately. Development reports
retain partial/NotEvaluated coverage and do not become formally ready.

Full-chain artifacts:
`.artifacts/p509-joint/f46085fce71b4c46a17b6a23bb1a22a6/`.
Large/concurrent artifacts:
`.artifacts/p509-joint/09e3c66752c2433cb48f0c742c7b1296/`.

The large sample contains 160 pages and 31,573,516 bytes. Publication completed in
2,789 ms while PDF processing remained active until 8,236 ms. A queued inspection
was cancelled, the actual content worker cancelled in 15 ms, source/revision/
placement identities remained unchanged, and retry succeeded. Reported peak
memory was 332,693,504 bytes and CPU time 6,218 ms under the configured limits.
These are measurements for this Windows sample, not general performance promises.

## Actual browser acceptance

Headless Chromium ran the actual `AssemblyPanel` and source panel against the
actual controller and background worker with an isolated in-memory fixture.
Two same-name PDFs were uploaded and mapped through the UI to Alpha and Beta.
An invalid third manifest row remained visible while an explicit valid subset got
its own plan. The real enqueue request completed with HTTP 202, its response was
deliberately lost, and a page reload recovered the original job. The result had
two distinct documents, two node-specific placements, one revision increment,
one completion callback and unchanged original source bytes. No page errors were
recorded. Evidence and screenshot: `.artifacts/p509-browser/evidence.json` and
`.artifacts/p509-browser/completed.png`.

This qualifies the isolated assembly panel/controller workflow. It does not claim
a PostgreSQL-backed full application browser run or general PDF viewer acceptance.
All implementation findings were fixed and independently re-reviewed read-only.
