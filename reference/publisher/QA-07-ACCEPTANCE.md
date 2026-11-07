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

1. **Finalization/delivery browser closure is unrun.** Existing workspace, publishing history and package-validation views do not provide the planned P4-08 finalization/export/receipt flow. Complete that dependency and its browser acceptance within the ordered business plan; do not count API tests as a substitute or describe P4 as wholly absent (its domain/API/storage exist).
2. **Broader Linux qualification is unrun.** Native resource enforcement, cancellation, listener combinations and other QA-07 Linux cases remain, beyond the completed case-sensitive cache and release-handle checks.
3. **External qualification is unrun.** Version-fixed external-validator evidence and official current regional business scope comparison remain outstanding. Regional profiles must remain unqualified until their evidence exists.

All task outputs are retained under the local evidence directories and remote `/data/home/ratools-qa-76555a3-20261107/` on `insta01`. The existing shared machine is not deleted or stopped. This ledger must not be used as a full-suite, PostgreSQL-backed browser, external-validator or regulatory approval claim.
