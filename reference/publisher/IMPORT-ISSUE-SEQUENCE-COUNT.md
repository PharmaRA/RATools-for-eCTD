# Failed import sequence count

QA-05 / R-10, 2026-11-07; baseline `df5cc40`.

`FailedSequenceCount` measures distinct nonblank sequence identities with an Error issue, using ordinal sequence comparison and case-insensitive severity labels. Several errors on sequence `0001` count as one failed sequence. Application-level errors without a sequence remain in the issue collection and do not invent failed sequences. The existing skipped-index count contract is unchanged.

Evidence: `.artifacts/qa-05-20261106/r10-red` reproduced duplicate counts, lowercase/uppercase severity omissions and application-level errors counted as sequences (4 passed, 4 failed, 1 unrelated Linux skip). `r10-green` passes eight import cache/summary cases, with the one real Linux cache case still explicitly skipped on Windows. Complete Application Recommended analyzer compilation and bounded economic readonly review passed. This is not full import, browser, backend or Linux acceptance.
