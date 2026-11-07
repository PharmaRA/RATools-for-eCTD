# Instabox checks

The user-directed entry points are:

```powershell
pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test
pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task build
```

The initial implementation targets the current **frontend** QA-07 gate. `test` selects `ErrorBoundary.test.tsx` (1 case) and `SequenceWorkspacePage.validation.test.tsx` (24 cases); `-TestFiles` accepts explicit frontend source test paths. The worker refuses success when requested files are absent, cases are skipped/failed, or the two default case counts change. It is not a full frontend/backend or Windows qualification runner. `build` runs both TypeScript no-emit configurations, Vite production build and the original gzip budget checker. Backend/Linux resource qualification still needs its own bounded runner through this entry.

Authentication uses the CLI's existing login or `INSTABOX_API_KEY`, inherited locally. No credential value is read by project scripts, passed as a command argument, uploaded or written to evidence. The default machine is the existing `insta01`; `-CliPath` overrides the installed CLI location. A failed authentication/connectivity probe stops before source packaging; it never falls back to local tests/build/install.

On Windows, when an already-running parent application has not inherited a newly persisted variable, the entry loads only the named user-level `INSTABOX_API_KEY` into its process environment. Its value is never displayed, serialized or passed in command arguments. A transient `insta01` name lookup failure was avoided with `-Machine` set to the existing machine ID; no new machine was created.

The explicit synchronization rules are in `scripts/remote-check.ps1`: frontend source extensions, named frontend configs/lock/budget checker, and these remote runner files. This includes uncommitted and newly added matching source files. It excludes environment files, credentials, business data, backups and platform outputs; source links are refused. Each run creates a new `/data/home/ratools-remote/<timestamp-guid>` directory. `source-manifest.json` hashes the exact uploaded file bytes; `run.json` records HEAD, the complete archive SHA-256, remote path and background job ID. HEAD alone is not the working-tree identity.

The remote worker requires Linux x86_64/glibc and a Node version satisfying the current package engine declaration. It reads the committed npm v3 lock, checks its root dependency declarations against package.json, downloads public npm tarballs, verifies each integrity value and extracts regular files/directories only. It does not run npm lifecycle scripts or copy Windows node_modules. This is an explicit archive installer, **not a claim of npm-ci parity**; a dependency requiring extra lifecycle setup must be handled and validated separately. Archives remain retained. All worker descendants inherit a seccomp filter refusing unlink/unlinkat/rmdir; Node also refuses filesystem deletion APIs.

Long runs use Instabox background jobs, poll at 20-second intervals and collect the final job status. Test/build/installation failures are recorded separately in `results/summary.json`, with exact commands, tool version, exit codes and log paths. The downloaded summary must match the saved task/source archive and the completed job exit code. Results are retained under `.artifacts/remote-check/<run>/results/`; production assets remain in the corresponding remote `frontend/dist`.

If the client wait expires, exit 124 means **still running**, not passed/failed. Resume without resynchronizing or relaunching:

```powershell
pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Resume .artifacts/remote-check/<run>/run.json
```

No script deletes files or machines, configures an expiry or pauses the shared machine. Outputs are retained as verification evidence; future cleanup must use the established recycle rules.

## Current validation boundary

2026-11-07: the test entry was invoked and reached the Instabox probe. CLI returned **3 (not logged in)**; the PowerShell entry returned **1** with the specific authentication message. `INSTABOX_API_KEY` was absent (only existence checked). No source was uploaded and no remote installation, build or test started. The build entry, dependency installer, background-job protocol and result collection have **not** been exercised on Linux. Read-only review does not substitute for those checks. These scripts remain pending remote acceptance and are not counted as a closed QA-07 gate.

Later on 2026-11-07, the user made the named variable available at user scope and remote acceptance completed. The first three installer versions failed before tests/build: DefinitelyTyped uses roots such as `chai/` and `node v24.12/`, and three dependency archives repeat a regular file. The final installer requires a single safe root, verifies tarball integrity and accepts repeated files only when their bytes are identical; no existing file is overwritten or deleted. Failures remain in `.artifacts/remote-check/20261107T103447-*`, `103647-*`, `103648-*`, `104012-*` and `104013-*`.

Successful independent fresh runs, on Linux x86_64/glibc 2.41 and Node **22.23.3**, with the inherited no-delete guard:

| Task | Local evidence under `.artifacts/remote-check/` | Result |
| --- | --- | --- |
| `-Task test -Machine <existing-machine-id>` | `20261107T104232-aabdeeb9eda54a348a77fe6dc18ae1b2/results/` | **25 passed, 0 failed, 0 pending**, exit 0 |
| `-Task build -Machine <existing-machine-id>` | `20261107T104355-94f700772e2d4aaea4360fc99af07f72/results/` | dependencies, app/config TypeScript, Vite and all original gzip budgets passed, exit 0 |

The test archive SHA-256 is `54edac1a68f97f51b7725e1e00c41fd5efec628ff1b004d7fd5d97b2ce17293e`; the build archive SHA-256 is `06e32ee3b250605dd391656ef04691e327db09fb48cf7b23a4c19491eb6cf644`. Both record base HEAD `9b6d0fe` and include the then-uncommitted runner changes in their hashed source archives. The build budget log reports total JavaScript **449.2/450.0 KiB**, workspace **17.7/18.0 KiB**. Remote directories mirror these run names under `/data/home/ratools-remote/` and retain build output.

This establishes the bounded frontend remote workflow, not full QA-07, complete frontend/backend testing, Windows qualification, P4-08 browser closure or regulatory qualification. Later feature edits require their own uploaded source digest and checks.

P4-08 adds optional `-LintFiles` (explicit synchronized TypeScript paths) and `-BrowserProbe` (build only). The browser probe runs this upload's production output in Chromium against a loopback synthetic API service and retains `results/browser-contract.json`. It does not start the actual backend, assert downloaded ZIP persistence or qualify Formal profiles. It stops only its own browser/service and retains its isolated profile. The current accepted run and limits are described in [FINALIZATION-DELIVERY-UI.md](FINALIZATION-DELIVERY-UI.md).

## Bounded Linux platform scope

`-Scope linux-platform` synchronizes the current shared PDF concurrency budget, content process host, deployment validator/options, their project/SDK version declarations and the selected runner. `-Task test` compiles those sources on Linux and runs ten checks; `-Task build` compiles only. It uses the retained Linux SDK 8.0.423/runtime 8.0.29, with provider 8.0.8 and its Npgsql 8.0.4 dependency downloaded from NuGet and recorded with URL/SHA-256. No Windows assemblies or build directories are uploaded. Compilation is direct C# 12 with nullable/warnings-as-errors, **not full MSBuild or Recommended analyzer qualification**.

Final run `20261107T114816-1803b767f8884e38a0c82c1b452799e4`, base HEAD `a8d3f73`, archive SHA-256 `bbff997b0ff8754d74be58b94da9995d474b77bf47c7345f783fb1bda20b6672`: **10 passed, zero failed/skipped, exit 0**. Reports are `results/linux-tests.json`, `linux-toolchain.json`, `linux-compile.log`, `linux-tests.log` and `summary.json`. Eight listener checks include two actual Kestrel loopback HTTP probes; the container exception is configuration validation only. Other checks verify the two-slot budget/cancelled waiter recovery and the intentional Linux content-host refusal. The latter is a safety refusal, not positive Python resource capability.

Linux failures distinguish toolchain, dependency, build, test and test-timeout phases. A deliberate `#error` in the test driver reproduced the previous generic classification in `20261107T114640-9d5716c5294e4d0f9470bfeb5001f34a`; the same fault correctly reported `build-failed`, exit 1, in `20261107T114743-bf33da0fb8bd47c6bb19cff5773491e9`. The fault was restored before the successful final run. Both fault archives remain evidence. `linux-phase.json` names the phase being attempted; only the final summary/exit code/test report establish success.

Resume requires matching task, machine and scope. Legacy missing scope/`frontend only` explicitly means frontend; it cannot restore a Linux run. Downloaded summaries also require matching scope and archive identity. Repeated retrieval uses a fresh `retrieval-<guid>` directory. Economic read-only review passed. Linux qpdf/Python hard memory/CPU process-tree qualification and owned-process cancellation remain open; this runner does not claim them.


`-Scope backend-browser` now builds the current production frontend plus the four backend source layers. `-Task test` additionally runs process failure/deadline regressions, the capture recycling probe and actual API/Chromium Development capture/download/draft checks. The backend uses a bounded direct-csc/dependency resolver, not MSBuild/NuGet restore parity. Accepted source/archive, 6 browser checks, failure history and remaining Formal/PG/PDF limitations: [FINALIZATION-ACTUAL-BROWSER.md](FINALIZATION-ACTUAL-BROWSER.md).

## Linux process termination acceptance

`-Task test -Scope backend-browser` additionally compiles and runs `ProcessTreeProbe.cs` through the current public Qpdf engine. A controlled Python parent starts a child; cancellation, timeout and stdout-limit cases require both processes stopped and the shared slot reusable. Zombie/exited states count as stopped, not reaped; host init owns reaping. Helpers and waits are bounded and evidence retained.

Final run `20261107T125657-e608fc7dcfe344c082dab0141b084ac6`: **3/3, zero failures/skips, overall exit 0**. Source base `e588bf5`, archive SHA256 `125235d53221bc2e739b9158385ac4e82f43d70f727b2c383996b5c8693c68c6`. Evidence: `results/process-tree/result.json`, `process-tree.log`, `summary.json`, `linux-cgroup.json`. Earlier browser/recycling/build checks reran successfully; overlapping totals are not added. Economic read-only review PASS; no production Qpdf change was needed.

This closes the selected owned-process cancellation/timeout/output-limit gate. It does not qualify actual qpdf output, Linux Python content execution or hard CPU/memory limits. Read-only current-task cgroup inspection found no directory/procs/subtree write permission; no other delegated paths were searched and no host permissions changed.

A later real run exposed a probe-only procfs exit race: `131230-eb0db8fcaeea455aa1643b7dc6b1763e` passed cancel/output-limit but timeout failed with IOException "No such process" after stat had been opened. The probe now accepts IOException only when that PID directory is no longer visible. In `131500-13f67262b488429aa5a4b37f3bc4b31b`, process-tree returned 0 and all three cases passed (source archive `667b16e9049ff85c682ce4d8cb5919b9050f89878b8b16341f47a2db60ec1d0e`). Economic read-only static/evidence review PASS. The overall job returned 1 in a later, separate PDF ZIP verification step; it is not recorded as an overall pass.

`-RemoteRoot /tmp/ratools-remote` selects a fresh job on the separate temporary filesystem when `/data` is capacity-constrained. Only that root and `/data/home/ratools-remote` are accepted, case-sensitively; resume validates its recorded root. Temporary-volume source/build/workspace files may not survive a machine restart. Results are downloaded locally, and the browser ZIP/profile remain under `/data`. No existing evidence is deleted or expired. The nonempty PDF result and exact final source/ZIP digests are in [FINALIZATION-ACTUAL-BROWSER.md](FINALIZATION-ACTUAL-BROWSER.md).

## PostgreSQL preparation scope

`-Task test -Scope postgres-platform -RemoteRoot /tmp/ratools-remote` prepares an isolated PostgreSQL16 environment through the same entry. `postgres-packages.json` pins official PGDG Debian13 amd64 server/client16.15 and libpq18.6 URLs/SHA256 values. Only regular archive files are extracted; no maintainer scripts/system installation run. The needed libpq SONAME is copied from the verified regular implementation and its actual resolution is checked with ldd.

The task-only C preload adapter moves requested cleanup targets to the task root's recycle directory with `renameat2(RENAME_NOREPLACE)`. It refuses links, multiply linked regular files, out-of-root targets, nonempty directories and failed directory enumeration. The inherited seccomp filter still refuses physical deletion syscalls, including a direct raw-syscall smoke assertion. This is an isolated harness with controlled directory ownership, not a hostile concurrent-writer sandbox or production filesystem implementation. The cluster uses loopback task-local trust auth, no user credentials, `initdb --no-clean` and mmap dynamic shared memory. Process groups plus the unique native executable path/PID-start identity bound failure cleanup; surviving native descendants prevent success.

Final environment run `20261107T134344-653e80e38c574d9cb327d2bb73b99213`, base7c07a80, archive `ab1b02439d02c95799df74139bf8a7ca1ebe55e7727436fd8cad58d8018354f9`, overall exit0: compiler/smoke/dependency loading/initdb pass; SQL committed value survives a clean database stop/restart. Two shutdown process lists and final native list are empty; 22 recycled entries remain preserved. Logs/results were downloaded locally. The cluster/tool files remain on `/tmp`, whose durability is not guaranteed across machine restart.

Earlier run134110-6b7478f60d52449cae285fd992003eeb completed the SQL checks but POSIX shared-memory cleanup logged Permission denied under the no-delete guard. Those objects remain retained; no physical cleanup was attempted. The mmap rerun has no such cleanup failure. This scope establishes a usable isolated database environment; **it does not run the RATools migrations, PostgreSQL API/browser, application recovery or Formal acceptance**.

Final initialization correction: review found that run134344 still left POSIX cleanup warnings during initdb, although server restarts were clean. The initdb subprocess now also receives `-c dynamic_shared_memory_type=mmap`, and its log is explicitly checked for shared-memory cleanup failure. Final run **20261107T134752-f344d6258fc24e5fad21fae62bfeae27**, archive **2eaf0c0366fcc42940dfdca62c0b566f7c8dcb0db96e6b5b65740fda25410aa9**, overall exit0: bootstrap/post-bootstrap and two service starts/stops have no cleanup failure; 28 recycled entries; both shutdown lists and remaining native processes empty. The initdb informational auto-selection line still says posix; the explicit override is applied to initialization and resulting configuration. Earlier runs remain partial evidence with retained shared-memory artifacts, not clean environment acceptance.

`-Scope backend-pg-browser` compiles the same product frontend/backend plus current DatabaseMigrator source, prepares the pinned isolated PostgreSQL environment, and runs the real Development browser workflow across API/database restart. Final command, migration31/0 assertions, byte-for-byte archived report comparison, ZIP digests and limits: [FINALIZATION-ACTUAL-BROWSER.md](FINALIZATION-ACTUAL-BROWSER.md). This is process restart on the same machine and retained filesystem, not machine-reboot/crash-recovery qualification.

The Linux qpdf host now requires explicit delegated cgroup-v2 execution. `-Scope linux-resources` compiles current backend source and runs resource/cancellation/launcher-fault probes without frontend dependency installation. For backend-browser/backend-pg-browser/linux-resources the entry uses a retained user-manager unit and polls its actual main exit status; both inner/outer workers apply no-delete guards. New run.json records the actual launcher command and local entry SHA256. Details, red/green evidence, CPU overshoot, required environment and limitations: [LINUX-PDF-RESOURCE-LIMITS.md](LINUX-PDF-RESOURCE-LIMITS.md).

## Same-input frontend reuse and capacity stop

For a new `backend-pg-browser test`, optional `-VerifiedFrontendRun <retained-run-id>` allows only prior Linux frontend assets whose complete synchronized frontend inventory/bytes equal the new upload. The old archive SHA and summary binding, and exactly one successful dependencies/types-app/types-config/Vite/budget step, are required. Source links and special assets are refused; regular assets copy into a new independent workspace with recorded hashes. No node_modules or Windows outputs are reused. The current backend/migrator still compile from the new upload. This assumes the explicitly named retained build output remains trusted and unchanged; the asset hashes are recorded at reuse time, not retrospectively presented as original build-time sealing.

Run143949-e9dad679af514bfb8a296decd5a8ad29 (base9dda41b, archive1539d531243ee7d696374bf7a5b246b4ab7ec689d5bd2c0db256101c7f056e41) verified208 identical inputs against142751-164a6e5f442b4326845ca6c2c49d6dbe/source3d954dcdaab84f9a1ce35c7aed6968d0e32e198f5f7f611354508771353da97f. The actual index.html mutation test was refused before assets copied, and the unchanged-input copy completed. Economic read-only module/evidence review PASS. Frontend dependency installation/types/Vite/budget were explicitly **not rerun** in this job. Its source run had passed those steps but failed later at the PostgreSQL socket prerequisite.

**The complete143949 job is not accepted.** `/data` reached100% with0B free (filesystem total20,957,446,144B), while `/tmp` had only149,553,152B free. The CLI reported the job lost without an exit code. summary.json, postgres-platform.json and manager-launch.json were0B. The unit wasMainPID0/ExecMainStatus1/failed; the task resource root waspopulated0 and no task-native PostgreSQL process remained. The status-collection command's exit0 is not the job result. Local `frontend-partial-evidence.json` and `lost-job-state.json` preserve the usable partial evidence. No cleanup deletion, machine expiry, pause or resize was performed.

Latest accepted product PostgreSQL browser evidence remains135532 at its exact source archive; latest accepted resource-host evidence remains142441. The corrected socket environment independently passed143310. A fresh combined run on the latest code is still required once capacity is restored; a lost/truncated run cannot be resumed into a success claim.

The CLI's `resize --help` states online disk growth needs no restart and paid disk capacity costsUSD0.08 per allocatedGB-month. Growing the existing approximately20GB allocation to40GB would add approximatelyUSD1.60/month on a paid plan. This persistent resource/billing change awaits explicit user confirmation. Do not remove retained evidence to make room or silently switch to local testing.

2026-11-07 user-directed execution change: remote storage exhaustion may be handled by local tests. The current Windows entry and first accepted47-test/Recommended-analyzer result are documented in [LOCAL-CHECK.md](LOCAL-CHECK.md). Remote-only instructions above are historical for the earlier continuation; no disk expansion is required to continue authorized local work. Linux-only and external qualification remain separate.
