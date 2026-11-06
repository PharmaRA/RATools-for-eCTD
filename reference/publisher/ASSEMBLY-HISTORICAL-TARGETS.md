# Assembly historical target effectiveness

QA-04 / R-04, 2026-11-06; baseline `2694d14`.

The assembly planner previously checked target identity, application/sequence scope, node identity and the target's own delete operation. It did not account for subsequent historical operations replacing or deleting that target.

`AssemblyPlanner.ReplayHistory` now evaluates the application's placements before the target sequence in ordinal sequence order. Each sequence resolves operations against the previous sequence state, so repository enumeration order cannot choose a winner. A qualified replacement/deletion makes its exact target ineffective. The replacement itself becomes current. Delete instructions are never content targets.

Append groups require a cumulative lifecycle policy. Their members remain unqualified, rather than inferring cascading deletion or treating one branch as an ordinary current leaf. Same-sequence operations, conflicting operations on one target, cross-node references and unresolved historical targets also preserve uncertainty. An operation with no resolvable target makes then-current earlier leaves uncertain; a later independent new leaf can still be current. This follows the existing package lifecycle inspector's distinction between confirmed invalidity and unknown effectiveness.

Preview rows return `ASSEMBLY_TARGET_NOT_EFFECTIVE` for confirmed historical invalidation, and `ASSEMBLY_TARGET_EFFECTIVENESS_UNKNOWN` for unqualified history. These are invalid rows requiring an operator to resolve the history/policy. Existing target-not-found, same-sequence and node diagnostics remain in place.

The durable worker's `VerifyCurrentAsync` uses the same replay before committing selected rows, in addition to its existing inventory digest and byte checks. A history change after preview/enqueue prevents committing placements, revision or result. The application mutation boundary and repository transaction still govern concurrency; this change introduces no database schema or new API field.

Evidence: `.artifacts/qa-04-20261106/`. The initial valid red run had 21 passes and four expected failures for replace/delete and append targets. Further red runs proved same-sequence, cross-node and missing-target uncertainty gaps. `r04-review-green` passes 32 planner/durable cases. Full Application compilation uses .NET 8 Recommended analyzers, repository editorconfig and warnings as errors; the compiler emits an existing CS9236 informational message in PackageLifecycleInspector. Test isolation directories are retained as evidence; no physical cleanup is invoked.

This is Windows targeted evidence. Full backend/API/browser, Linux, PostgreSQL concurrency and external qualification have not been run for this item. Other QA-04 fixes remain separate work and separate commits.
