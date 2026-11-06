# Regional evidence retention on draft removal

QA-05 / R-11, 2026-11-07. Implementation baseline: `63e011b`.

Removing a sequence node or document placement referenced by any archived regional metadata revision returns HTTP 409 with code `REGIONAL_EVIDENCE_REFERENCED`. The object identity, workspace revision and archived evidence remain intact. Removing the context from the newest metadata revision does not remove older references.

This fix deliberately retains referenced draft objects. It introduces no tombstone, visibility flag or migration. A future requirement to hide such objects needs a separate design for editable visibility and retained history. Existing Restrict foreign keys and append-only evidence remain in force; no cascade removal is introduced.

## Enforcement

- EF checks all stored regional contexts before removal. Sequence node checks cover both node contexts and document contexts within the exact application and sequence.
- If evidence commits after the check, only PostgreSQL foreign-key violations naming the three regional reference constraints are mapped to the retention conflict. Other database errors retain their existing handling. The surrounding workspace transaction rolls back revision changes.
- In-memory repositories use the same metadata store via `IRegionalEvidenceReferences`, including committed revisions and revisions staged in the current transaction. Production DI aliases the interfaces to the same instance.
- The global exception middleware maps the dedicated exception to 409 and supplies a message explaining why historical evidence still requires retention.

## Validation

The original PostgreSQL tests created a context, saved a newer revision without contexts, and attempted to remove the previously referenced object. Before the fix, both tests returned 500 instead of 409 (0 passed, 2 failed). After the fix both passed.

The final bounded runner passed **16 tests: 13 real PostgreSQL and 3 in-memory**. This includes two deterministic races: an independent connection commits evidence after the negative reference query but before removal. A test interceptor refuses to execute SaveChanges unless that evidence has committed. Real FKs reject removal; a new connection confirms the node/placement, original workspace revision and archive digest remain intact. Other covered contracts include concurrent revision writes, rollback, append-only guards, current migration model, DI identity, scope isolation and staged reference rollback.

Application, Infrastructure and API current source compiled with .NET 8 Recommended analyzers, the repository editorconfig and warnings as errors. The existing Application CS9236 informational diagnostic remains. The implementation and additional tests received two bounded read-only economic-model reviews, both PASS; the reviewer did not build, run tests, write files or delete files.

Evidence and runners are retained under `.artifacts/qa-05-20261106/`:

- `r11-red4/tests.log`: valid pre-fix 0/2 failure evidence.
- `r11-green/tests.log`: original cases 2/2 after repair.
- `r11-extended2/tests.log`: final 16/16.
- `application-r11`, `infrastructure-r11`, `api-r11`: complete layer compilation evidence.
- `run-postgres-r11-all.ps1`: bounded runner using current rebuilt layers.

Backups are under `backups/qa-05-20261107/r11/` and `r11-docs/`. Test fixtures and compiler outputs are retained as controlled evidence. No physical file cleanup was performed. An initial missing-using compile failure in the added test was corrected; it is not counted as a product failure.

The 409 response was tested through the real middleware in process, not through a complete Kestrel route or browser. Standard whole-repository tests, Linux and external qualification remain unrun. QA-05 stays open for the real Linux case-sensitive filesystem gate; this local acceptance does not advance QA-06.
