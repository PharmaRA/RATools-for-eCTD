# CTD persistence and legacy backfill (P1-03)

Migration `20260908131138_AddCtdNodeInstances` adds the node schema without
changing existing document bytes, storage paths or leaf IDs. It introduces:

| Table/column | Purpose |
| --- | --- |
| `ctd_definitions` | Immutable, versioned definitions and their closed JSONB schema |
| `ctd_node_instances` | Application identity, parent, definition and exact identity attributes |
| `sequence_nodes` | Per-sequence attributes, title, order, directory segment and metadata status |
| `sequences.WorkspaceRevision` | Bigint revision, default zero, bounded to the safe JSON integer range |
| `document_placements.NodeInstanceId/SortOrder` | Initially nullable node binding plus stable ordering |
| `node_backfill_checkpoints` | Versioned per-sequence input digest, counts and completed transaction |
| `node_backfill_diagnostics` | Placement-level unresolved mapping or metadata findings |

The migration and model snapshot were generated with the repository's EF 8.0.8
tool. PostgreSQL trigger functions in the embedded migration resource additionally
enforce immutable business identities and definitions, declared parent shape,
sequence attributes and identity preservation. This migration resource is part of
the historical migration: future changes require a new migration, not editing it.

Composite foreign keys enforce application ownership and ensure a placement's
application, sequence, node and compatibility section agree. Separate partial
unique indexes cover singleton children and singleton roots with NULL parents.
A definition-shape foreign key prevents bypassing singleton rules by changing
the instance's repeatability flag. A parent must exist before a child is inserted,
and parent identities cannot be updated, preventing cycles. Node deletion cannot
leave sequence nodes, child nodes or placements dangling; application/sequence
deletion preserves the existing scope-specific cascade behavior.

`EfCoreCtdNodeRepository` reads a consistent snapshot and writes complete sequence
node selections inside an explicit transaction with compare-and-increment of the
revision. Losing a revision race leaves no node or partial revision change.
Existing application updates do not overwrite a stored revision with a stale
domain object's value. All API mutation guards are implemented by P1-04; this
repository does not claim that the older file/metadata endpoints already enforce
the new revision protocol.

## Upgrade and repair

Take the existing application database and workspace backup, stop editing and
run the independent migrator with `ConnectionStrings__PostgreSql` set for that
database. API startup still only checks migration state.

```text
dotnet run --project src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj --configuration Release --no-build
dotnet run --project src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj --configuration Release --no-build -- --preview-ctd-backfill
dotnet run --project src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj --configuration Release --no-build -- --backfill-ctd
```

Preview requires the schema to be current and performs no migration or data
write. It reports the same deterministic mapping and diagnostics as execution.
Normal schema migration does not implicitly backfill existing workspaces. Each
executed sequence commits nodes, placement bindings, diagnostics, checkpoint and
its new revision together. Cancelling before commit rolls back the entire
sequence, using an independent bounded rollback token. Already committed earlier
sequences remain committed; the next invocation resumes by rechecking their
input digests. An unchanged repeated run creates no new nodes and no new revision.

For unambiguous nonrepeatable sections, the planner creates deterministic parent
chains shared in the application. It never infers a manufacturer, product,
substance or historical association from a folder, sponsor or file name. At every
repeated legacy branch, it creates a distinct unresolved context per placement.
This intentionally avoids collapsing unrelated documents merely because they
share `CtdSection`. The repair/import workflow can later explicitly map them to
verified business instances; their files are not moved by backfill.

Missing metadata propagates a visible unresolved state to the bound document's
node. Unknown regional sections, invalid sequence numbers and ambiguous definition
paths such as `m2.3` remain unbound with explicit diagnostics. The first schema
does not invent regional M1 node definitions; P6 provides those schemas. Existing
rows can still be read. Once a placement is bound, a section-only move cannot
rewrite its binding; the instance-aware move/UI follows in P1-05/P1-08. This is an
additive upgrade, not the later migration that makes every binding mandatory.

Before tightening the nullable placement relationship, repair all unresolved
rows and verify coverage. P1-06 adds source-XML reparsing; no background migration
silently reparses or relocates historical packages.

## Verification and downgrade

PostgreSQL tests run the actual independent migrator in new disposable databases,
both from empty and from the previous migration seeded with a real synthetic PDF
and imported leaf ID. They exercise preview, execution, repeat, original-byte
preservation, true foreign-key/unique/trigger failures, concurrent revisions,
cancellation after SaveChanges, and application-scoped deletion. The full Release
backend suite passed with PostgreSQL configured: 664 passed, zero skipped.

An empty node schema can be downgraded and upgraded again. Once nodes or repair
diagnostics exist, `Down` explicitly refuses to discard them. Export node/attribute
and placement mappings for audit and restore the complete pre-upgrade database
and workspace backup to return to the old program. A schema downgrade alone
cannot preserve data that the old version cannot express. Existing leaf IDs are
never rewritten by either schema migration or backfill.

Linux uses the same migrations and test entry points in CI; local execution
evidence in this task is Windows/PostgreSQL 16.14. Future migration and API tasks
must retain the old defect regressions and validate their own new boundaries.
