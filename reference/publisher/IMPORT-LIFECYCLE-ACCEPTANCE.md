# QA-07 local import and lifecycle acceptance

Recorded 2026-11-07 against `071d74c` plus the source manifest below. Run with:

```powershell
pwsh -NoProfile -File ./scripts/local-check.ps1 -Task msbuild-import
```

The selected scope is ApplicationImportRegionalTests14, ApplicationImportDeleteTests16 and LifecycleReferenceTests12. Real MSBuild builds API, DatabaseMigrator and the complete Tests project, then original VSTest executes these42 cases. Guards and cached-restore limitations follow [MSBUILD-ACCEPTANCE.md](MSBUILD-ACCEPTANCE.md).

## Baseline and retention

The shared EctdWorkspaceFixture and delete-import tests still matched review baseline90f3d36. The regional-import test only had the preceding analyzer repair. Their old physical-delete setup/disposal paths were inspected but not executed in this subtask. Before running, four missing-file setup calls were changed to controlled moves and the shared fixture disposal was changed to whole-tree recycling.

Each fixture now owns an absolute `.artifacts/ectd-workspace-tests/ectd-workspace-<GUID>` directory. Recycling checks direct-parent ownership, prefix/GUID and source/ancestor links; tree traversal checks each entry before descending. Destinations are random direct children of the same-parent `_recycle`, without replacement. Missing-file setup asserts that the original path is absent and the retained bytes are identical. Disposal asserts that the original tree was moved. Source, recycle files and failed-run evidence stay retained; recycling does not reclaim disk space. The bounded source preflight also includes the selected tests and their explicit helpers, without claiming a complete native-operation sandbox.

## Failure evidence and test contract repairs

All runs are under `.artifacts/local-check/`. Production code was not changed and its fail-closed conditions were preserved.

| Run | Result | Finding and correction |
| --- | --- | --- |
| `20261107T211236-0e425ca3460b42f8a6d21ea15370456d` | 30 passed/12 failed/0 skipped | Two positive delete-import methods still assumed every diagnostic was NODE_SCHEMA_NOT_AVAILABLE. They now accept six explicit codes, all Warning, and require exactly one RegionalQualificationPending per imported sequence; lifecycle and payload assertions remain. |
| `20261107T211542-0b80292b6dff45029be694d656cae21c` | 40/2/0 | EU re-publication lacked the same regional store, archived backbone and snapshots used by import. The test now passes the same instances into the real package builder and asserts regional revision ID/digest. |
| `20261107T212016-ede6b199816f449a96d3708cdb28ef80` | 34/8/0 | The old mixed fixture used InMemoryWorkspaceRevisionStore with EF application Update, which deliberately does not write existing sequence revisions. Import now uses a coherent InMemory application/revision pair. The imported application is then stored through the real EF repository, tracking is cleared and every sequence revision is checked after rehydration. |
| `20261107T212422-a52e30bccfd04d8686577255e9b1d8af` | 38/4/0 | Typed EU output correctly rejects missing real cover placements. EU fixtures now include a historical cover and its delete-cover lifecycle, generated then imported with actual context. No synthetic typed defaults or writer relaxation were added. |
| `20261107T212748-0d33a952ff7749d793179aeacf693ac2` | **42/0/0** | API, DatabaseMigrator and Tests each build with0warnings/0errors. Runner exit0, test process tree stopped without forced termination. |

Final source-manifest SHA256: `5bd7a38c34779b772515a825263337ff8aa553f783caf42c55653f28634eb630`. Backups: `backups/qa-07-local-20261107/workspace-retention/`.

The delete-only publication still requires `PublishedFiles` to be empty. Every current delete placement binds the historical DocumentId/target; output leaves have no href and empty checksum. The intended original leaf remains selected by actual LeafId, including external historical leaf IDs. Original document and cover bytes are checked. The existing malformed/missing/checksum/outside-reference and failed-sequence rollback assertions remain in the selected execution.

## Boundaries

The persistence round trip uses EF InMemory provider for application/document/placement mapping, with InMemory import, node and regional stores. It does not validate PostgreSQL, relational constraints/transactions or PostgreSQL revision SQL. Generated fixture XML is synthetic; this is not independent official-regional qualification. Empty EU cover support remains pending. NodePublishingTests, broader EU/US PostgreSQL end-to-end tests, the full suite, fresh restore, native browser download completion, current joint Linux/PostgreSQL evidence and Formal/external qualification remain outside this run. Earlier156 and34 results were not rerun here and are not combined into a new all-suite total. The earlier A2 deviation documented in MSBUILD-ACCEPTANCE remains part of the record.

Economic readonly code and final evidence review: PASS. Final TRX class counts and the879-entry source manifest were independently checked.
