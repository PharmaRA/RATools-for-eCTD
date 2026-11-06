# Import path identity

QA-05 / R-09, 2026-11-07. Baseline `4aaffd1`.

`ImportFileHashCache` keys entries by the exact normalized full path using ordinal comparison. Two sources addressed as `a.pdf` and `A.pdf` cannot silently share the first source's hashes. On Windows, two case aliases may be read twice; repeated requests for the exact same path still reuse one measurement.

The import service's per-sequence and cross-sequence document maps use the platform's path comparison: ordinal-ignore-case on Windows, ordinal elsewhere. The same policy governs regional-backbone path identity and conflicts with an already imported working directory. Digest strings and file extensions retain their existing case rules.

Evidence in `.artifacts/qa-05-20261106/`: `r09-red` reproduced the wrong second MD5 (1 passed, 1 failed, 1 skipped); `r09-green` passed both platform-independent cache cases and explicitly skipped the real Linux double-file case on Windows. Complete Application Recommended analyzer compilation and economic bounded readonly review passed.

The Linux test writes separate files with different bytes, verifies both files exist, and checks their measured digests. It has not run: WSL did not expose a runnable distribution and no docker/podman command was found. This commit is a verified local code repair, while QA-05's required real Linux acceptance remains open. No full import/browser/backend or external qualification is claimed. No test fixture is physically deleted.

## Linux acceptance, 2026-11-07

The previously open R-09 Linux gate passed on Instabox `insta01`: Debian 13 x86_64, Linux 6.18.54-instabox, persistent `/data/home` filesystem reported as ext2/ext3. CLI 0.8.196+92be4dfb5 was installed in the repository tool directory from the official npm package with its SHA-512 checked. A task-local .NET SDK 8.0.423 / runtime 8.0.29 was downloaded from Microsoft and checked against its published SHA-512. No credentials are included in the task bundle or evidence.

Only the cache source, its three original tests and direct compiler dependencies were uploaded. The Linux compiler used Recommended analyzers and warnings as errors, with the existing xUnit CS1701 compatibility suppression. The `4aaffd1` cache baseline reproduced **1 passed / 2 failed / 0 skipped**, including wrong hashes for real `a.pdf` and `A.pdf`. The `76555a3` source passed **3 / 0 / 0**. Uploaded inputs were SHA-256 verified. Two bounded economic-model read-only runner reviews passed. The first runner compilation lacked an assembly version and xUnit compatibility option; it ran no tests and is not product failure evidence.

Evidence: `.artifacts/qa-05-linux-20261107/evidence/{linux-acceptance2.log,sdk-info.log,inputs.json}`. Runners remain in the parent directory. Remote evidence and input files are retained in `/data/home/ratools-qa-76555a3-20261107`; no physical cleanup or machine deletion was requested. New-machine creation hit the existing 5/5 quota, so the authorized test used a separate directory on an existing machine. No host-wide installation was performed.

Together with the recorded R-10/R-11/R-12/R-13 targeted and PostgreSQL evidence, this closes the defined QA-05 gate and permits QA-06. It does not establish a complete Linux backend build, browser acceptance, resource qualification or external qualification; those remain in QA-07.
