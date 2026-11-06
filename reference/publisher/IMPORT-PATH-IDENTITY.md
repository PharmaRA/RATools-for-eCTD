# Import path identity

QA-05 / R-09, 2026-11-07. Baseline `4aaffd1`.

`ImportFileHashCache` keys entries by the exact normalized full path using ordinal comparison. Two sources addressed as `a.pdf` and `A.pdf` cannot silently share the first source's hashes. On Windows, two case aliases may be read twice; repeated requests for the exact same path still reuse one measurement.

The import service's per-sequence and cross-sequence document maps use the platform's path comparison: ordinal-ignore-case on Windows, ordinal elsewhere. The same policy governs regional-backbone path identity and conflicts with an already imported working directory. Digest strings and file extensions retain their existing case rules.

Evidence in `.artifacts/qa-05-20261106/`: `r09-red` reproduced the wrong second MD5 (1 passed, 1 failed, 1 skipped); `r09-green` passed both platform-independent cache cases and explicitly skipped the real Linux double-file case on Windows. Complete Application Recommended analyzer compilation and economic bounded readonly review passed.

The Linux test writes separate files with different bytes, verifies both files exist, and checks their measured digests. It has not run: WSL did not expose a runnable distribution and no docker/podman command was found. This commit is a verified local code repair, while QA-05's required real Linux acceptance remains open. No full import/browser/backend or external qualification is claimed. No test fixture is physically deleted.
