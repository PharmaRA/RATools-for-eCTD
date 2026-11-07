# Publish completion and report durability

QA-02 changes the completion boundary: generation alone does not persist `Completed`. The execution service builds a candidate report with artifact/readiness/audit facts and integrity evidence, writes the complete report, reads it back and verifies its serialized content, then persists the completed job with the candidate completion timestamp. All execution entry points, including compatibility Create and claimed worker execution, use this order.

Report integrity evidence measures delivery artifacts before writing the report; it excludes the report's own serialized bytes, which cannot include their own final size/hash. Report readability is proved separately by PublishReportStore before the completed state becomes queryable. Failure/cancellation during generation or report preparation keeps the repository out of Completed; claimed failures retain the fencing boundary for worker retry/failure handling.

The local artifact store writes a unique sibling staging file with CreateNew, flushes it, compares its complete text, and publishes through a move or atomic replacement. Replacement retains the previous file through the filesystem backup parameter before recycling it. Staging/previous files are sent to Windows Recycle Bin, never physically deleted; on other platforms they are retained pending a qualified recycling boundary. Report paths reject reparse/link traversal. Linux atomicity and cleanup qualification remain pending.

Durable attempts write distinct `-attempt-N` report filenames. A stale worker cannot overwrite another attempt's report. Read/download retain fallback to the prior unversioned report filename for historical completed jobs. Completion replay after a lost database response requires matching attempt, timestamp at PostgreSQL microsecond precision, validation-run reference, status, output/package paths and failure reason. A later attempt's completion cannot satisfy an earlier attempt's replay.

Windows local acceptance, 2026-11-06, baseline c05ba46:

- Original terminal sequence: 11 pass/4 expected fail (generator cancellation, first write failure, cancellation after first write, completed-before-readable).
- Added stale-attempt replay case: 26 pass/1 expected fail before the attempt/timestamp binding fix. Added simulated PostgreSQL timestamp round trip: 27 pass/1 expected fail before microsecond comparison.
- Current complete Domain/Application/Infrastructure compile with .NET 8 Recommended analyzers, original editorconfig and warnings-as-errors. Targeted report/terminal/worker/naming/artifact tests: 37 pass.
- Actual file tests cover cancellation preserving the previous report and successful complete replacement with recycled previous bytes. The old implementation did not consistently observe cancellation in the new atomic test, so this is not reported as proof of old-file truncation.
- Actual PostgreSQL 16.14: completed attempt/report readable after a new context, and terminal commit response loss correctly recognized after timestamp round trip: 2 pass. Dedicated cluster has 31 migrations; no schema change in this repair.

Backups `backups/qa-02-20261106/`; evidence/runners `.artifacts/qa-02-20261106/`. Two historical disk/retention tests contain physical deletion and were not executed; standard whole test project has existing compile blockers recorded in QA-00. Browser/Linux/full-suite and regulatory qualification are unrun. Economic-model bounded read-only code review PASS, with no build/test/file-write operations by the reviewer; those broader gates remain unproven.

## Audit snapshot acceptance (2026-11-07)

The report's `AuditSummary` is a frozen observation taken before the terminal state and Completed audit event are persisted. In the real local publication integration fixture, the report contains two job events and latest action Started; the live audit contains Created, Started and Completed. Reopening the stored report preserves its original AuditSummary. This is the QA-02 ordering above, not a missing terminal audit event. The old expectation of three events inside the already-written report was reproduced and corrected with assertions for both the frozen report and the live terminal audit. See the publishing70 group in `QA-07-ACCEPTANCE.md`; this supplements the historical acceptance section without replacing its original evidence.
