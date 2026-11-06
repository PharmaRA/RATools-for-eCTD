# Assembly completion result binding

QA-05 / R-12, 2026-11-07. Baseline `d4c0926`.

Assembly completion is bound to the sealed preview and the authoritative workspace transaction. The store first validates job, plan, digest, application, sequence, revision, selected line set and unique placement identities. It then validates each result row's sealed source version, destination href, historical target, leaf identity and operation shape. It rejects source document reuse for a new payload row and rejects invented versions or destinations on delete rows.

Within the completion transaction, the store reloads each actual placement and document. It checks application and sequence scope, node and sort binding, title, operation, document identity, leaf and lifecycle target. New payload rows must reproduce source filename, media type, length, SHA-256, MD5 and the platform-derived published href. Delete rows must retain the historical document identity and target sequence. This validation runs before the terminal job update, so a rejected row leaves no completed result or newly committed placement.

Completed EF and in-memory jobs are revalidated on reads against the sealed plan and static result contract. The existing row digest continues to cover the complete serialized job/result; no schema migration is needed. EF completion retains its optimistic digest update and workspace-transaction guard, while in-memory completion retains its commit fence.

Evidence: `.artifacts/qa-05-20261106/r12-*`. The 55-case Windows assembly runner passes, including seven identity mutations (document, version, href, leaf, target, placement and source reuse), existing transaction/lease/rollback/delete/HTTP cases. Real PostgreSQL 16.14 passes 2/2 mutation/storage-read cases, then 2/2 existing successful commit and database rollback cases. An interrupted first compatibility attempt left two Pending task rows; they were cancelled through `EfCoreAssemblyStore.CancelAsync` on the task-owned database and retained, then the compatibility runner passed. No direct SQL deletion or cleanup was used.

Full backend, browser, Linux, external qualification and R-11/R-13 remain separate QA-05 gates. No regulatory or cross-platform completion claim follows from this item.
