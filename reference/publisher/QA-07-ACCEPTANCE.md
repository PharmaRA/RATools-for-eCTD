# QA-07 bounded acceptance ledger

Recorded 2026-11-07. Backend baseline `a2a05c5`; browser completion-state repair `a07b3c2`. QA-07 remains **in progress**, not fully accepted.

## Verified checks

| Scope | Result | Evidence under `.artifacts/qa-07-20261107/` |
| --- | --- | --- |
| Assembly and actual loopback HTTP | 55 passed | `assembly/tests.log` |
| Publication report completion and atomicity | 37 passed | `report/tests.log` |
| Import summary/cache on Windows | 8 passed, 1 explicit Linux skip | `import/tests.log` |
| PDF tool concurrency, resource limits, content workflow on Windows | 47 passed | `pdf/tests.log` |
| In-memory finding/run contracts | 2 passed | `findings/tests.log` |
| Finalization input binding, capture, ZIP and response handles | 52 passed | `capture/tests.log` |
| Listener configuration | 24 passed | `listening/tests.log` |
| Actual Kestrel startup/listener probes | 4 passed | `host-probe/host.log` |
| Regional PostgreSQL references/concurrency | 13 passed; 3 additional in-memory cases | `pg-regional/tests.log` |
| PostgreSQL finding/projection integrity | 6 passed | `pg-findings/tests.log` |
| PostgreSQL assembly result binding | 2 passed | `pg-assembly/tests.log` |
| PostgreSQL complete publication reports | 2 passed | `pg-report2/tests.log` |
| PostgreSQL finalization bindings/idempotency | 5 passed | `pg-capture2/tests.log` |
| PostgreSQL assembly claims/recovery/rollback | 8 passed | `pg-recovery/tests.log` |
| Empty/previous-schema migration | 2 scenarios passed | `migrations/tests.log` |

The first eight rows total **229 passed, zero failed, one explicit skip**. Two additional legacy publication report methods containing physical cleanup were deliberately excluded by the runner; they are unrun, not included in the skip count. These selected runners are not the standard complete backend suite. The actual Linux cache case passed separately on Instabox (see `IMPORT-PATH-IDENTITY.md`); Linux release-handle response tests also passed 4/4 (see `RELEASE-FILE-READ-HANDLE.md`).

PostgreSQL totals **36 real database tests**, plus the three memory cases and two migration scenarios. The empty database applied 31 migrations. The upgrade scenario applied 30 migrations through `AddRegionalMetadata`, wrote an application, sequence and archived regional defaults, applied migration 31, reran migration, checked model/pending state and reloaded old evidence through a new connection. Both databases remain retained. This qualifies the tested previous-schema upgrade, not every historical schema or downgrade.

`baseline.json` records the current layer assembly SHA-256 values. Economic-model read-only review verified runner references, selection boundaries and counts and passed; it did not build, test, write or delete. Initial runner dependency-path mistakes ran no tests and were corrected before the results above.

## Browser evidence and open gates

Chromium 154.0.8037.57 on Instabox Debian 13 ran against the real current API with the InMemory provider and an isolated synthetic workspace. The API and browser inherited a seccomp filter refusing unlink/unlinkat/rmdir. API configuration used a task-only ephemeral Data Protection extension. No API key was stored in the bundle or evidence. Initial harness startup/resource/click timing errors were corrected; they are not product defects.

The browser created an EU application and sequence, saved/re-read a regional draft, and retained its unqualified regional status. It then exercised synthetic PDF upload, explicit node mapping, plan preview, subset confirmation, worker commit and result recovery. This exposed the completion disappearing on revision refresh, repaired in `a07b3c2`. The repaired UI passed 29 component/source tests, TypeScript, scoped lint, read-only review and the actual browser round trip including repeated result reading and creating the next plan. Evidence: `regional-browser-saved.json`, `assembly-browser-final.log`, `assembly-browser-state.json`, `assembly-ui-{red,green}.log`.

The frontend production budget gate subsequently passed at unchanged thresholds. Against `803276f`, a fresh build reproduced 472470 B total JavaScript and 19036 B for the workspace chunk. The error boundary used only Result's error state but included the unused 403/404/500 illustrations. Reusing Alert preserves the error message/retry/home controls. Lazy loading the validation summary and grouping React Query's shared modules removes the remaining chunk/compression overhead. The final production configuration produces **459939 B JavaScript / 460800 B**, **18092 B workspace / 18432 B**, and all other budgets pass. Evidence: `budget-current-red.json`, `budget-final-result.json`, `budget-final-build.log`; 24 workspace validation tests and one error-recovery test passed, as did TypeScript and scoped lint. Economic read-only review found no blocking code issue. These checks do not constitute a new browser or full frontend-suite run. Earlier unsuccessful bundler/minifier experiments remain diagnostic evidence; no budget or dependency version was raised.

Remaining gates, in plan order:

1. **Actual-backend browser closure is partial.** Real Chromium/current API now passes Development capture, persisted ZIP verification and draft reopening on an empty synthetic US sequence/InMemory; see [FINALIZATION-ACTUAL-BROWSER.md](FINALIZATION-ACTUAL-BROWSER.md). A later one-page PDF fixture and PostgreSQL-backed API/database restart also pass, with exact archived report bytes retained. Formal capture/delivery/receipts remain open. Earlier synthetic Formal fixtures do not qualify that gate.
2. **Linux qualification is partial.** Ten Linux concurrency/cancelled-waiter/listener/refusal checks pass, including two actual Kestrel loopback probes; see [REMOTE-CHECK.md](REMOTE-CHECK.md). Owned-process cancellation/timeout/output-limit now pass three selected checks below. Native memory/CPU process-tree enforcement remains open. Linux content-host refusal does not qualify content processing.
3. **External qualification is unrun.** Version-fixed external-validator evidence and official current regional business scope comparison remain outstanding. Regional profiles must remain unqualified until their evidence exists.

All task outputs are retained under the local evidence directories and remote `/data/home/ratools-qa-76555a3-20261107/` on `insta01`. The existing shared machine is not deleted or stopped. This ledger must not be used as a full-suite, PostgreSQL-backed browser, external-validator or regulatory approval claim.

After budget commit `9b6d0fe`, the user's updated execution rules require all subsequent build/test/install work through `scripts/remote-check.ps1` on Instabox. The new bounded frontend entry is implemented but **not remotely accepted**: its first test invocation stopped at authentication (CLI 3, wrapper 1), without upload or remote execution. See [REMOTE-CHECK.md](REMOTE-CHECK.md). Do not reuse earlier pasted credentials or treat prior local checks as validation of this new runner. Resume ordered work after CLI login or `INSTABOX_API_KEY` is available.

P4-08 dependency inspection also confirmed that `EctdReleaseCapturePlanner` deliberately adds `RegionalBusinessContextUnconfirmed` for new Formal capture, while `FinalizationService` forbids delivery registration for Development/non-ready snapshots. Implementing the UI must preserve these gates. No new P4-08 UI or Formal browser acceptance is claimed in this checkpoint.

Remote continuation on 2026-11-07: authentication is now available through the named user-level environment variable. The unified runner's first three installer attempts failed before product checks (archive root layout and identical duplicate entries); fixes preserve checksum/path/deletion boundaries. Final independent Linux runs passed **25 selected frontend tests** and **app/config TypeScript + Vite + all original gzip budgets**, exit 0. Evidence and exact source archive digests are in [REMOTE-CHECK.md](REMOTE-CHECK.md), test `20261107T104232-aabdeeb9eda54a348a77fe6dc18ae1b2`, build `20261107T104355-94f700772e2d4aaea4360fc99af07f72`. The remote execution prerequisite is satisfied for this frontend scope. P4-08 and the broader Linux/external gates remain open.


Linux continuation: current four production files compiled on Linux with SDK 8.0.423/runtime 8.0.29, without Windows build outputs. Final selected result **10/10, no skips**, exit 0, run `20261107T114816-1803b767f8884e38a0c82c1b452799e4`, archive `bbff997b0ff8754d74be58b94da9995d474b77bf47c7345f783fb1bda20b6672`. Scope-safe resume and build-failure classification corrected; economic read-only review PASS, no build/test/install/write/delete. Direct csc warnings-as-errors is not full backend/MSBuild/analyzer acceptance. The production Linux resource limitation already existed in the review baseline; no capability flag or Formal gate was relaxed.

Official-source acquisition remains unresolved: two attempted FDA URLs returned HTTP 404; the EMA module-one page returned HTTP 200 with a 244-byte **Request Rejected** body, not standards content. Evidence `.artifacts/qa-07-20261107/official-source-refresh/sources.json` and `eu-module1.html`. Those responses establish no newer FDA criteria version, and profile versions/qualification were not changed.

Actual-backend continuation: current-source Linux build/frontend budgets, runner failure recovery 2/2, capture recycling 3/3 and actual API/browser 6 checks all pass, exit0. Final run `20261107T124931-5368ecb0d79346fdaa1314bf9ef8bdb9`, archive `594af0baed295e46a1abc3b488d2d2c3ba5db5696b27dc109c651e90ecf93eae`; economic read-only review PASS. See [FINALIZATION-ACTUAL-BROWSER.md](FINALIZATION-ACTUAL-BROWSER.md) for actual commands, failures, direct-csc limitations and open gates. No full-suite/Windows/PG-restart/Formal qualification claim.

Linux process-tree continuation: actual Qpdf engine hosting a controlled Python parent/child passes cancellation, timeout and output-limit **3/3, no skips**, including stopped processes and recovered shared slot. No production fix was necessary. Run `20261107T125657-e608fc7dcfe344c082dab0141b084ac6`, base `e588bf5`, source archive `125235d53221bc2e739b9158385ac4e82f43d70f727b2c383996b5c8693c68c6`, whole job exit0; economic read-only review PASS. See [REMOTE-CHECK.md](REMOTE-CHECK.md). Hard CPU/memory and Python Linux processing remain open; the current task cgroup is not writable, which says nothing about all potential delegated locations.

Official-source correction also applies to the later proxy attempt: `.artifacts/qa-07-20261107/official-source-refresh/proxy-attempt/sources.json` records FDA404 and EMA244-byte rejection bodies (including the attempted timeline URL). No official search result or standard PDF was obtained; no version/timeline assertion is supported by those requests.

Nonempty PDF browser continuation: `20261107T131935-2d9e0298028a490193a99405e1c6c141` (base14445b0, archive411f4c57dbd3e58b344b3af9a9e6c86b557e2ede39c980fed8ee5b336e0b1563) passes the current API/InMemory/production-UI Development chain: 7 browser checks and 3 independent ZIP/PDF/backbone checks, overall exit0. Review PASS. Exact fixture/download hashes, failed runner attempts, local entry case validation and `/tmp` capacity boundary are in [FINALIZATION-ACTUAL-BROWSER.md](FINALIZATION-ACTUAL-BROWSER.md). No production PDF defect was demonstrated. PostgreSQL restart/Formal/complex PDF scopes remain open.

A further source attempt under `official-source-refresh/current-attempt/sources.json` again returned two FDA404 responses and EMA244-byte Request Rejected. No successful official-search or standards-body evidence was obtained. Statements claiming discovery of a newer FDA criteria version or official timeline are withdrawn; qualification/profile data remains unchanged.

PostgreSQL remote setup prerequisite now passes independently: run134344-653e80e38c574d9cb327d2bb73b99213, archiveab1b02439d02c95799df74139bf8a7ca1ebe55e7727436fd8cad58d8018354f9, exit0. Pinned PostgreSQL16.15 tools, guarded recycle adapter, real cluster initialization, SQL write/stop/restart/read and zero retained native processes are verified. Details and first-run POSIX shared-memory cleanup limitation are in [REMOTE-CHECK.md](REMOTE-CHECK.md). Product migrations and PostgreSQL-backed browser restart are still unrun; environmental readiness is not that acceptance.

PostgreSQL setup's final accepted source is superseded by run134752-f344d6258fc24e5fad21fae62bfeae27, archive2eaf0c0366fcc42940dfdca62c0b566f7c8dcb0db96e6b5b65740fda25410aa9, exit0. The prior134344 still had initdb cleanup warnings; explicit mmap now covers initdb as well as server execution, logs checked,28 entries retained, no task-native processes. Product PostgreSQL browser gate still pending.

PostgreSQL product browser continuation: final135532-f8772164aa6c40dda54fad62e4b27f0d (base9b21dfe, archive8a7c7ed8a8d99401102f0f2e4ca932f0c5fd20fd5885a3c7d706c5524ad895da) exits0. Current DatabaseMigrator applies31 migrations, repeat applies0. Actual API/PostgreSQL stop/restart retains full workspace/manifest/versions and exact26584B report bytes; browser8/errors[], independentZIP3/3. Four database shutdown lists/final native list empty. Details: [FINALIZATION-ACTUAL-BROWSER.md](FINALIZATION-ACTUAL-BROWSER.md). This closes the selected PostgreSQL Development process-restart gate, superseding earlier statements that it was unrun. Formal success/receipts, complex PDF, machine-reboot/crash recovery, full suites/MSBuild/analyzer/Windows parity, Linux hard CPU/memory/Python execution and official/external qualification remain unrun or unqualified.

Linux qpdf resource continuation: actual resource-host defect reproduced0/2; repaired through explicit delegated cgroup-v2 enrollment. Run142441-ae3a1129cba8480bbab3a5b11b76c835, archive5efcca789fff21feeb9a6dfd8fa3fb3de6d0e54e3990bb733da4025e48918eb5, wholeexit0: memory/CPU/accounting-failure3/3, cancellation/timeout/output-limit3/3, real launcher poll-failure cleanup pass; all groups empty, reviewPASS. Kernel aggregate memory/swap/pids/rate enforcement plus sampled cumulative CPU budget (200ms configured,219.726ms observed). See [LINUX-PDF-RESOURCE-LIMITS.md](LINUX-PDF-RESOURCE-LIMITS.md). Actual qpdf output and Python content qualification remain open; do not call the sampled CPU deadline exact hard cumulative enforcement.

Current head checkpoint after resource9dda41b and socket8b7fd5b: the latest combined run143949-e9dad679af514bfb8a296decd5a8ad29 is **unaccepted/capacity-blocked**, not passed. Its208-input frontend reuse and changed-input rejection module passed independently and reviewedPASS, but/data100%/0B caused missing job status and0B terminal receipts. Unit/resources/nativePG were stopped. Earlier accepted PostgreSQL135532 and resource142441 evidence stays tied to its own archives; latest combined acceptance must be repeated after capacity recovery. See [REMOTE-CHECK.md](REMOTE-CHECK.md). Formal success/receipts, actual pinned qpdf/Python content qualification, full suites/MSBuild/analyzers/Windows and official/external business qualification remain open.
