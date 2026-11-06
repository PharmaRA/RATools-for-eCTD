# Package findings and projection read integrity

QA-05 / R-13, 2026-11-07; baseline `e76b391`.

Finding pages are now anchored to the complete report's existing SHA-256 and run binding. The EF reader verifies that report, derives the expected filtered count and ordinal-addressed page, and compares database page rows against the report's content and filter columns. It rejects mismatched counts, ordinal positions, finding fields, rule/severity/status and logical/node paths. The returned findings come from the verified report. The in-memory implementation uses the same verified-report source and filter predicate.

This uses the already committed report hash instead of adding a separately mutable per-row hash and backfill migration. Rows outside the requested page are not all scanned: the current query's count and page are verified. A later query verifies its own page. Each page request currently loads and parses the complete report, which adds CPU and memory cost for large reports. No large-report performance qualification is claimed here.

Finding comparison uses defined deserialized fields rather than JSON byte equality. Historical optional null properties may be absent, and whitespace/property formatting may differ. Malformed JSON or changed evidence fields are refused. The report remains the returned evidence source.

Projection archival checks application, sequence, profile and resource-limit identity in addition to input/baseline binding. Reading verifies the existing projection SHA-256, schema, completed run status, application, sequence, input, baseline, profile and limits. A correctly recomputed projection hash cannot authorize an association with a different run.

Evidence: `.artifacts/qa-05-20261106/r13-*`. The initial PostgreSQL run had one pass and four expected failures. After the integrity fix, five tests passed; a new historical-JSON compatibility test then reproduced one failure and drove field-normalized comparison. The original in-memory store contract passed 2/2. Complete Infrastructure Recommended analyzer compilation passed. Final PostgreSQL and readonly review outcomes are recorded in the quality plan.

No schema migration, physical cleanup or broad backend/browser/Linux qualification is included. R-09's real Linux gate and R-11 remain open under QA-05.
