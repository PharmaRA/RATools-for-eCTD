# PDF tool process budget

QA-04 / R-06, 2026-11-06. Implementation baseline: `0e7ec6a`.

The application shares a two-slot `PdfToolProcessBudget` across qpdf navigation inspections, navigation output generation, the content engine's qpdf version probes, and Python workers. Dependency injection and default engine constructors use the same process-wide instance. An explicit budget can be supplied by a test or isolated host. A slot covers one tool process tree through process exit, output-reader completion and stream disposal. Waiting is cancellable and does not start a tool. The content engine's existing one-operation slot remains separate and does not recursively acquire the shared budget.

On Windows, qpdf uses the content engine's existing suspended process launcher and Job Object. The process joins the job before its initial thread resumes. Default limits are 1 GiB committed memory per tree, three minutes of CPU time and four active processes per tree. Memory and CPU values are configured through `PdfProcessing:Engine:MaxMemoryBytes` and `MaxCpuTime`; the existing execution deadline and diagnostic/output size limits remain enforced. Thus two simultaneously active default trees have at most eight native processes and a combined 2 GiB configured job memory budget. Python retains its own configured tree limits. These are process-tree limits, not a claim of constant total application memory.

The Windows launch adapter reuses the existing native structures and handle ownership. The non-Windows path retains cancellable scheduling, process-tree termination, deadlines and bounded streams; equivalent kernel memory/CPU enforcement is not qualified there. Linux qualification remains in QA-07.

Failure cleanup retains stdout, stderr and wait tasks. After an error, cancellation or timeout, the engine cancels readers, terminates the owned tree, closes its job and waits for all tasks before disposing output and releasing the shared slot. Secondary reader exceptions are observed without replacing the original outcome. Closing the job also terminates descendants whose parent has already exited while they retain inherited output handles.

Evidence is in `.artifacts/qa-04-20261106/`:

- `r06-red`: real simultaneous inspections started three processes where the budget must allow two.
- `r06-red-memory`: 33 passed / 1 expected failure; the controlled allocation helper committed all 32 eight-MiB blocks despite the configured 64 MiB bound before native enforcement was connected.
- `r06-red-readers`: 46 passed / 1 expected failure; stderr exceeded its limit while stdout remained in an outstanding write, and the old run returned early.
- `r06-reader-green`: 47/47. Includes actual qpdf edits and reopen verification, bounded output, shared inspection/execution cancellation, start/callback failure release, real Python capability probes, Windows memory and CPU limits, child-tree cancellation/deadline handling, OCR/font/version workflows and the reader barrier regression.
- `qa04-joint-green`: 48/48 planner/durable/actual loopback HTTP cases using current Application, Infrastructure and API assemblies.
- Complete Infrastructure and API Recommended analyzer compilation passed with repository editorconfig and warnings as errors. Current Domain/Application had already passed during this ordered QA work.
- Economic readonly review initially blocked reader cleanup. The reproduction preceded the fix; its bounded re-review passed without running tools that write, build or test.

Two existing content-test number parses and one fixed expected array were minimally adjusted to satisfy the recommended analyzers before running their direct regressions. No full backend, browser, Linux, external validator or regulatory qualification is established. Test evidence directories are retained; physical cleanup is not invoked.
