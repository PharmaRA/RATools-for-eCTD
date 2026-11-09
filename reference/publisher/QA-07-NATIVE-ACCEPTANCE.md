# QA-07 native acceptance — 2026-11-09

Latest Windows checkpoint: resource/concurrency8, workflow7 and combined2 passed with final read-only review. PostgreSQL-content2 also passed in `pg2-11`, with database identity,31/0 migrations and clean remote shutdown; final read-only source/evidence review passed. Accepted coverage is **1,824/1,824 across separate runs**. This does not establish one unfiltered full-suite pass. Historical19-case boundaries below describe the earlier token/ACL checkpoint.

## PostgreSQL content2: final execution

Evidence: `.artifacts/local-check/pg2-11/`. The unchanged
`PostgresPdfContentTests.ContentEvidenceRoundTripsAndAdoptionCommitsOrRollsBackAtomically`
executed both `rejectRevision:false` and `true`, with2 passed,0 failed,0 skipped.
The original assertions cover retained content evidence, independent context
reads, committed/idempotent adoption, rejected-revision rollback, unchanged
source bytes and workspace revision. Discovery and TRX names match as multisets.

The Windows identity probe matched the unique database, server port, version and
postmaster start time before migration. The current independent Windows migrator
applied31 migrations, then0. All five stages exited0, and each child's connection
alias was present with the conflicting colon alias absent. All166 created process
instances matched guard and Job counts and exited; the outer receipt records
forced final cleanup. The remote manager/service/server exited0 after the exact
owned stop marker, with no remaining native processes or server process group.
Transport exited0 without timeout, truncation or OOM. The session took283,530ms.

| PG2 artifact | SHA-256 |
| --- | --- |
| Final TRX | `9a9eccab57acbb7b9d4bb94e0d802b17dc69566b1b2545d7807a07e1db5f87e7` |
| Remote source archive | `3b5a0dc697fc9c12e42e643e32ac2f694ae96b299d9578962fe4b12a487d9034` |
| Python relay | `c4264258666bfe2109b898e9891f6cd8d16c5761154784cbb908bbd8ad687c28` |
| SSH wrapper | `7a0ac1f57a78abcaf568b9787949c0f46ed3e2360b65224dfbbfbf3ffebdd497` |

Failed attempts `pg2-1` through `pg2-10` remain retained. The first exposed the
PowerShell `cli` alias shadowing a helper function; the helper is now
`Invoke-TaskCli`. Direct SSH later timed out. The task-only Python binary SOCKS5
relay through local7890 passed actual authentication and banner checks in
`tunnel-17`, with8 guarded/Job instances stopped. Earlier .NET relay attempts
did not qualify; their exact failure cause remains unproven. No guard class was
added to resolve networking. Raw SSH diagnostics and credentials were not retained.

`pg2-5` reached31/0 migrations but both content cases failed because VSTest changed
the working directory to its output directory, making the fixture's relative
qpdf path absent. Setting the existing `RATOOLS_TEST_REPOSITORY_ROOT` opt-in fixed
that harness error without product or fixture changes. A separate diagnostic
supported a missing migrator connection value. The retained synthetic probe
`pg-environment-alias-1/result.json` demonstrates that PowerShell7.6.5/.NET10.0.11
`SetEnvironmentVariable(name,$null)` leaves an empty child-environment key.
Each child now explicitly removes competing aliases and writes its required
connection variable. This proves the empty-key mechanism, not every historical
failure's cause. The first migration exceeded100s in `pg2-10`; `pg2-11` passed
with a200s stage limit and360s session budget, below the remote400s watchdog.
The independent deadlines are cleanup bounds, not a guarantee every failure
branch finishes within the600s outer limit.

The two PG cases used CLI initialization mode with business moves disabled.
No product code, test assertions or native refusal policy changed for PG2.
Final review independently checked all15 launch bindings,130 output bindings,
881 source inputs, the exact TRX multiset and166 process-instance lifecycles.
Official-source/rule mapping, a fixed external validator and Formal delivery/
receipt qualification remain open, as does an unfiltered full-suite run.

Final Windows evidence is `.artifacts/local-check/w1/` (workflow7, zero failures/skips, 435 guarded/Job process instances stopped) and `.artifacts/local-check/c1/` (combined2, zero failures/skips, 181 instances stopped). The workflow run permits one actual qpdf promotion and refuses two unrelated moves. The combined run permits four qpdf promotions and two single-file assembly-directory promotions, and refuses two unrelated moves. Final target file IDs, volumes, link counts and retained bytes were checked independently. Two fixture changes only allow a task-owned root through `RATOOLS_NATIVE_FIXTURE_ROOT`; business assertions are unchanged.

| Accepted artifact | SHA-256 |
| --- | --- |
| Workflow7 TRX | `7eae13979c6146e8529ab8b4390922fd2eb09b7764ce976f5c8892654fb961ef` |
| Combined2 TRX | `4ebd50c92257aaa8ff6f36eafc16369e57740f7fe5138331744b985ce2cd88d6` |
| Current 881-input build manifest | `4a1f184778443dfcce5f7c5d713d8b3211d311d1f254947caa669be8324cb44c` |
| Current test DLL | `4afee4ebd45b4d64bb56f9e0a184acfa461e7b5cdf6431b23a5fb8776399b205` |

The combined run includes the160-page, 31,573,516-byte fixture, cancellation/retry, archived evidence and original resource assertions. Cancellation was23ms; first/retry memory was332,193,792/332,402,688 bytes below1GiB, and CPU was9,578/9,343ms below180,000ms. This remains synthetic Development qualification; Formal delivery and external regulatory qualification remain open.

At the earlier Linux checkpoint, qpdf **54/54** and PostgreSQL/qpdf **7/7** passed, with zero
failed/skipped cases and final read-only code/evidence review PASS. These 61
cases add to the previously accepted 1,744 across separate executions, yielding
**1,805 of 1,824 covered;19 then remained unqualified**. This is not an unfiltered
full-suite run. Linux resource probes separately passed seven checks.

## Native tool provenance and execution

Official qpdf 12.4.1 Linux x86-64 release evidence is retained under
`.artifacts/local-check/qpdf-linux-tool-20261108T230136-7f655f86d4884a72a3002060330ae1e3/`.
The official GitHub release API asset digest, download receipt and actual
4,040,061-byte ZIP agree on SHA-256
`db9122e88ec00c76ac6a14e09ffb92406db1773d47b968911ff6e69f28c09bf9`.
The archive's sole symlink, `lib/libqpdf.so.30 -> libqpdf.so.30.4.1`, was reviewed
and materialized as a regular copy of the target bytes; no link was created.

Final evidence is
`.artifacts/local-check/linux-qpdf-final-20261108T234925-aa7169f80e2346d59e3f1529b9250433/`.
The worker verifies the 862-input source map, guard launch and tool archive,
runs in a unique delegated user-manager unit, rebuilds the complete test
assembly with actual MSBuild, and selects eight complete classes (50 cases)
plus four exact portable `QpdfConcurrencyTests` methods. Its separate
PostgreSQL driver selects only `PostgresPdfRenameTests` and
`PostgresProcessingAdoptionTests` (seven cases), requiring the explicit qpdf
executable and delegated cgroup root.

Discovery/selection/TRX name multisets agree for both runs. Four builds completed
with zero warnings/errors; the independent migrator applied 31 migrations and
zero on repetition. Four worker stages exited zero with no surviving process
groups. All 393 recorded resource groups and their root were empty. The final
manager reported MainPID zero and absent unit cgroup; both inner and outer
PostgreSQL survivor lists were empty. Guarded PostgreSQL cleanup retained data
and recycled native cleanup objects. No physical deletion was enabled.

The 862-input source map differs from the accepted PG107 map only in
`QpdfConcurrencyTests.cs`. The original PG driver remains bound to the original
source archive; the new `qpdfOnly` driver is separately bound to this run's
archive. The test DLL SHA-256 is
`0e152971bc55ae1491891175f65436342d3dc52dc8906e0db9b6dafceb1d8089`.

| Artifact | SHA-256 |
| --- | --- |
| Source archive | `d763999483b4e166c51ee60a5cb7009e44b320c450e9c31d8526fec5db60f2a1` |
| qpdf TRX | `e32a4a58e95b3e451dea59b8fe65fc0ca1feb50e50e6ea89ea6ed396c3bf21d8` |
| PostgreSQL/qpdf TRX | `bdcd6145d2bacd0f2acc408fdc6f01d3b961cd5f88fd17c247fca12f160017d6` |

## Reproduction and repair

The earlier 29-case and 50+7-case runs are retained, not added again. Red run
`linux-qpdf-concurrency-red-20261108T234234-68bab67fe434492cba539670e2a26236`
executed 54 cases: 50 passed, four failed because the portable concurrency tests
still hardcoded Windows tool paths. The repair reads `RATOOLS_TEST_PYTHON` and
`RATOOLS_QPDF`, retaining the original Windows paths as defaults. Assertions,
barriers, cleanup and Windows-only guards are unchanged. The final run above
passed all 54. `/usr/bin/python3` only ran controlled standard-library helpers;
this does not qualify PyMuPDF/OCR content processing.

## Historical coverage accounting at the 1,805 checkpoint

The Windows discovery baseline retains the old migration method name
`PostgresCtdMigrationTests.IndependentMigratorUpgradesAnEmptyDatabaseTwiceAndEmptySchemaCanBeRolledBack`.
Its reviewed replacement in PG107 is
`IndependentMigratorUpgradesAnEmptyDatabaseTwiceAndCurrentSchemaRefusesDowngrade`.
The 1,805/1,824 accounting explicitly maps those names. Without the mapping,
raw name comparison incorrectly reports one extra and one missing case.
Windows/Linux truncated parameter display names also differ; preserve the
baseline multiset and repeated-case counts instead of raw string-set unions.
The 61 cases in this record have no overlap with the previous 1,744.

| Then-remaining class / methods | Cases |
| --- | ---: |
| `PdfCombinedQualificationTests` | 2 |
| `PdfContentHttpTests` | 1 |
| `PdfContentResourceTests` | 5 |
| `PdfContentWorkflowTests` | 6 |
| `PostgresPdfContentTests` | 2 |
| Windows-only `QpdfConcurrencyTests`: `QpdfCpuLimitEndsTheOwnedToolAndReleasesItsBudgetSlot`, `QpdfHostEnforcesTheConfiguredJobMemoryLimitBeforeExecutingToolCode`, `PythonCapabilitiesShareTheProcessBudgetAndRecoverAfterCancelledWaiting` | 3 |

Production `PdfContentProcess` and `PythonPdfContentEngine` deliberately refuse
Linux content execution. Windows Job limits do not prove native filesystem
cleanup protection. At that checkpoint these19 cases required a qualified Windows native no-delete
execution path before running; Linux early returns are not accepted coverage.
Full unfiltered backend execution, current official-source/rule mapping,
version-fixed external validation and Formal delivery/receipt qualification
remain open. Browser Development acceptance is recorded separately in
[FINALIZATION-ACTUAL-BROWSER.md](FINALIZATION-ACTUAL-BROWSER.md).

## Current Linux resource gate

Run `.artifacts/remote-check/20261109T000339-152b9f5adc114109be2c2d540197c4c2/`
passed three resource checks, three process-tree checks and one injected manager
polling failure. It used the current foreground entry, a 480-second inner
deadline and 600-second CLI deadline. All four stages exited zero, the transport
reported no timeout/truncation/OOM, and archive/manifest/current files matched
in final read-only review. Source archive SHA-256:
`5ce232d10b68995a2e97fb5dd5fcd89d2ec411e5ac57c61097a4d7c2235fec72`.

The memory workload recorded `oom_kill=4`, `oom_group_kill=1`; the CPU workload
recorded 201,974 microseconds against a 200,000-microsecond sampled budget.
The actual accounting permission fault recovered its slot. All five recorded
resource groups were empty. Cancellation, timeout and output-limit checks
passed. Injected polling exit 5 stopped both the worker and migrated child;
the outer manager ended with MainPID zero and exit zero.

This scope compiles four current backend layers via direct csc, not MSBuild.
These seven probes are outside the 1,824-case baseline and do not replace the
three Windows-specific tests. Deployment requirements and sampled-CPU limits
remain as documented in [LINUX-PDF-RESOURCE-LIMITS.md](LINUX-PDF-RESOURCE-LIMITS.md).

## Windows execution precondition: proposed token/ACL guard rejected

Three actual permission probes are retained under
`.artifacts/local-check/windows-native-permissions-20261109T002053-5fd752af838d496ab8adf854c6205f53/`.
Each has its own `probe.ps1` and `result.json`; the second and third are in
`caller-deny/` and `full-restricted/`. Original task-directory ACLs were backed
up under `backups/qa-07-local-20261109/windows-native-permissions/` before changes.
The probes used current-thread restricted-token impersonation, not native tool
execution or a qualified restricted process launch.

The proposed write-restricted token granted only the task's unique SID write
access, with DELETE/DELETE_CHILD/WRITE_DAC/WRITE_OWNER denied for that SID and
an Owner Rights rule limiting implicit owner access. Outside writes were denied,
but existing-file DELETE and parent DELETE_CHILD requests still succeeded.
Two stronger variants then added the current caller's DELETE/DELETE_CHILD deny;
the last also removed WRITE_RESTRICTED to require full restricted-token checks.

| Permission request | Write-restricted + caller deny | Full restricted + caller deny |
| --- | --- | --- |
| Task file write | Allowed | Allowed |
| Existing task file DELETE | Denied (5) | Denied (5) |
| Task directory DELETE_CHILD | Denied (5) | Denied (5) |
| Outside file write | Denied (5) | Denied (5) |
| Outside file DELETE | Allowed | Denied (5) |
| CREATE_NEW with GENERIC_WRITE and DELETE | **Allowed** | **Allowed** |

The creation-time request was exactly `0x40010000`. Each successful request
only closed the returned handle: no DELETE_ON_CLOSE, disposition/deletion API,
or move was invoked. All test files remain retained; the outside sentinel hash
was unchanged. Explicit protected-DACL creation failed with access denied;
subsequent error 2 means that file was absent, not a successful permission check.
Final read-only review of source differences and all three receipts passed.

Microsoft's [DeleteFileW documentation](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-deletefilew)
explicitly describes obtaining requested DELETE access on a newly created
file's returned handle even when inherited ACLs prevent reopening it for
deletion. The official page and HTTP200/SHA-256/UTC receipt are retained beside
these probes. The separately retained CreateRestrictedToken/CreateProcessAsUserW
documentation informed the candidate; documentation alone was not counted as
runtime protection.

These probes **reject this token + ACL design as the native no-delete boundary**.
Restricted-launching the entire VSTest tree does not resolve the demonstrated
creation-time handle behavior. No qpdf/PyMuPDF/OCR native process or remaining
19-case test was launched under this rejected design. An additional process
wrapper would also change the existing four-process Job limit and inherited
pipe tests; an assembly resolver proxy was not adopted. No production Windows
code, system policy or tool installation was changed.

At that checkpoint, the next prerequisite was a Windows execution boundary that rejects destructive
native file operations, including creation-time delete access and inherited
handles, while preserving the tested Job/process-tree behavior. This probe
does not establish that all Windows isolation methods are impossible. The later
interception harness and8-case acceptance advance that boundary; the rejected
token/ACL design remains rejected. QA-07 remains open.
