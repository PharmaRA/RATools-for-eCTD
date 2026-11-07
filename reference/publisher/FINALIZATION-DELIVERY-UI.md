# Finalization and delivery UI

2026-11-07: P4-08 frontend implementation is available from **定稿与交付记录** in the sequence workspace. This closes the missing frontend dependency; it does not establish complete P4-08 or QA-07 qualification.

The operator selects a completed run from application history (paged, filtered to the current sequence) or supplies its run ID. The UI loads the archived report and exact standards snapshot. It checks application/sequence, completion, the entire report binding, standards identity and Formal readiness, and derives the request's standards versions/profile/history digest from that evidence. Legacy runs without exact standards must be revalidated. An explicit checkbox confirms the selected revision. Development captures retain their blocking reasons and are labelled as development; they cannot register deliveries.

Finalization request keys are retained by application/sequence/revision/run in sessionStorage with an in-memory fallback. Closing/reopening the panel or selecting the same run again preserves the identity after an uncertain response. Revision changes discard the candidate evidence and confirmation. The server remains responsible for rechecking current workspace bytes, eligibility and idempotency.

Snapshot details expose manifest identity, standards/history/run, captured files, downloads, and file/node/leaf/lifecycle/standards differences against the previous recorded revision. The workspace hides editing controls when finalized. Only the latest locked snapshot offers a new draft; draft request identity is retained by snapshot/revision. A committed draft followed by a failed workspace read can retry the read independently without submitting the mutation again. `useWorkspaceData` opts into throwing refetch errors only for this finalization callback.

Delivery forms bind the exact `delivery-package.zip` SHA-256 from the selected manifest after verifying its snapshot identity. Only ready Formal snapshots expose the form. Delivery and receipt records are append-only; receipts keep transmission, technical validation and business outcome categories separate, including their original text/digest. Recorder names are manual statements. The UI asks the operator to refresh records after uncertain append responses; those APIs do not offer mutation idempotency.

Adding the UI initially raised the total gzip budget to 455.4 KiB. The audit page's date filter only needs two time bounds, so its full DatePicker dependency was replaced with native local date-time inputs. Seconds, UTC conversion, independent bounds, clear/reset and reversed-range rejection are preserved and tested. The final production total is **415.9/450.0 KiB**, with all original chunk/CSS budgets passing; no threshold or dependency version was increased.

## Verification evidence

All checks below ran through `scripts/remote-check.ps1` on the existing Instabox Linux machine. Fresh run directories under `.artifacts/remote-check/` include `run.json`, file hashes, source archive SHA-256, actual commands, tool versions, job exit status and logs. Base HEAD was `765be7a`; archives include the uncommitted feature files. No Windows node_modules/bin/obj were reused. Jobs and Chromium inherited deletion refusal; evidence/profile files are retained and task-owned processes stopped.

| Scope | Evidence run | Result |
| --- | --- | --- |
| Request binding missing implementation | `20261107T104524-e88728a1f63e45aa811dcee7bd5d349a` | 28 expected failures |
| Request binding implementation | `20261107T105041-8ef6153431e047df9ea97fedd01807f0` | 28 passed |
| Missing UI | `20261107T105238-4beaa5134a574ff4b7a294217b0c1d04` | 5 expected failures |
| Initial UI workflow | `20261107T105712-d7cde6d1b8b94f93927b426eba92313c` | 5 passed |
| Review recovery gaps + native-date contract | `20261107T111040-3b2e07cb910c4aeabe1c72bd25cfecb6` | 10 passed / 4 expected failures |
| Date input initial-value defect | `20261107T111246-dec7de8b3b0b473a9e24047b322c796b` | 64 passed / 2 failed; uncontrolled-to-controlled warning fixed with empty initial values |
| Actions/UI/audit/workspace after correction | `20261107T111527-7c92613cf9be4333a964b4fd21c13629` | 66 passed, zero failed/pending |
| Added history selection/paging | `20261107T112017-40d5d6649c5847f5a40bb891c92691eb` | panel 8 passed |
| Final hook/panel/workspace integration | `20261107T112353-6305983a0d884f44b3cc4ef7ad5863e1` | 42 passed (10 hook + 8 panel + 24 workspace) |
| Final production build + Chromium contracts | `20261107T112649-f9c41dc8848d41d6b92dfeb25be4e3cf` | TypeScript app/config, Vite, original budgets and 5 browser workflow checks passed, exit 0 |

These test groups overlap and must not be added as independent totals. Scoped ESLint passed in `20261107T111247-7a59bc44175a4bd3b3693b8c84c9ad23` for actions/tests/audit/workspace, and `20261107T112351-0179cacd10fa440986f7c5136a78ba72` for final hook/panel/workspace. Browser attempts `112016-*` and `112351-*` failed because the synthetic server mistakenly matched the HTML workspace route as an API route; the corrected `/api/` prefix passed. Those were harness failures, not product red tests.

The final Chromium probe exercises the production frontend with **synthetic API contracts**, including a deliberately labelled Formal fixture. It verifies development confirmation, same-key retry after panel reopen, development delivery refusal, immutable ZIP endpoint selection, draft/read-error recovery, ZIP-hash-bound delivery and transmission receipt categorization, plus native audit dates. It does not persist/assert downloaded ZIP bytes, run the actual backend or qualify Formal/regional eligibility. Browser report `scope` records that boundary. Economic-model read-only review identified the two recovery issues before correction and found no remaining blocking implementation issue after the fixes; it never executed build/test/install/write/delete.

## Remaining acceptance

The actual backend Chromium Development capture/export/draft subset now passes on an empty synthetic US sequence with InMemory persistence; see [FINALIZATION-ACTUAL-BROWSER.md](FINALIZATION-ACTUAL-BROWSER.md). The complete Formal delivery/receipt chain, nonempty PDF browser fixture and PostgreSQL-backed browser restart remain unrun. Current `EctdReleaseCapturePlanner` explicitly rejects new Formal capture with `RegionalBusinessContextUnconfirmed`, and `FinalizationService` rejects Development/non-ready delivery facts. Those safeguards were not changed or bypassed. Exact external-validator evidence and current regional business qualification remain prerequisites for claiming real Formal delivery closure. Full frontend/backend suites, broader Linux resource/cancel/listener qualification and regulatory approval are not claimed.
