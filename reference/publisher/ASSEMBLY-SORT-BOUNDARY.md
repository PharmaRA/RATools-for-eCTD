# Assembly sort bounds

QA-04 / R-07, 2026-11-06; baseline `ddd5912`.

The planner accepts explicit `sortOrder` values from zero through 2147483646. `int.MaxValue` is reserved as the exhausted automatic allocation boundary. Internal next-order calculation uses `long`, including the initial maximum of existing placements, so an existing maximum cannot wrap to a negative integer and an explicit maximum cannot throw OverflowException.

Invalid explicit values and exhausted automatic allocation produce a row diagnostic `ASSEMBLY_SORT_INVALID`. Invalid rows do not advance the next-order value. The preview contract continues returning its diagnostic result; enqueue refuses the invalid preview with the existing 409 conflict. No global preview HTTP status or schema change is introduced.

Evidence: `.artifacts/qa-04-20261106/r07-red2` has 39 passes and three expected failures (explicit maximum, existing maximum, batch exhaustion). Complete Application Recommended analyzer compilation passed. `r07-http-green` passes 46 planner/durable/actual loopback Kestrel cases, including the maximum-order diagnostic and refused enqueue, upload/source paging, subset worker execution, replay, cancel and timeout. Test fixtures remain isolated as evidence; no physical cleanup is invoked.

These checks do not establish a complete browser journey, full backend regression, Linux or external qualification. This fix has no database schema change.
