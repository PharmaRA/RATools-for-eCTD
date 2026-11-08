# QA-07 native acceptance — 2026-11-09

Current Linux qpdf **54/54** and PostgreSQL/qpdf **7/7** passed, with zero
failed/skipped cases and final read-only code/evidence review PASS. These 61
cases add to the previously accepted 1,744 across separate executions, yielding
**1,805 of 1,824 covered; 19 remain unqualified**. This is not an unfiltered
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

## Coverage accounting and remaining cases

The Windows discovery baseline retains the old migration method name
`PostgresCtdMigrationTests.IndependentMigratorUpgradesAnEmptyDatabaseTwiceAndEmptySchemaCanBeRolledBack`.
Its reviewed replacement in PG107 is
`IndependentMigratorUpgradesAnEmptyDatabaseTwiceAndCurrentSchemaRefusesDowngrade`.
The 1,805/1,824 accounting explicitly maps those names. Without the mapping,
raw name comparison incorrectly reports one extra and one missing case.
Windows/Linux truncated parameter display names also differ; preserve the
baseline multiset and repeated-case counts instead of raw string-set unions.
The 61 cases in this record have no overlap with the previous 1,744.

| Remaining class / methods | Cases |
| --- | ---: |
| `PdfCombinedQualificationTests` | 2 |
| `PdfContentHttpTests` | 1 |
| `PdfContentResourceTests` | 5 |
| `PdfContentWorkflowTests` | 6 |
| `PostgresPdfContentTests` | 2 |
| Windows-only `QpdfConcurrencyTests`: `QpdfCpuLimitEndsTheOwnedToolAndReleasesItsBudgetSlot`, `QpdfHostEnforcesTheConfiguredJobMemoryLimitBeforeExecutingToolCode`, `PythonCapabilitiesShareTheProcessBudgetAndRecoverAfterCancelledWaiting` | 3 |

Production `PdfContentProcess` and `PythonPdfContentEngine` deliberately refuse
Linux content execution. Windows Job limits do not prove native filesystem
cleanup protection. These 19 cases require a qualified Windows native no-delete
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
