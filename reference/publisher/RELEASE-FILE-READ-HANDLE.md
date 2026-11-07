# Verified release file handles

QA-06 / R-15, 2026-11-07. Baseline: `9a8825b` (the read path remained unchanged from the original `90f3d36` review baseline).

Release previews and ZIP downloads now use the same file handle for digest verification and response streaming. `LocalReleaseCaptureStore.OpenFileAsync` verifies manifest membership, opens the file once, measures its length and SHA-256 on that handle, resets the position to zero, and returns `ReleaseFileContent.Content`. The contract no longer exposes a source path to reopen.

`ReleaseFileContent` owns the seekable stream and implements synchronous/asynchronous disposal. The controller transfers the verified stream to MVC's `FileStreamResult`, preserving inline PDFs, other content as attachments, Range support, ETag and cache headers. MVC disposes the stream after response execution. Verification failures, response construction failures and delivery registration (both success and failure) also dispose it.

`FileShare.Read | FileShare.Delete` allows a path to be renamed while keeping the original file identity open. A replacement at the old path cannot change the response. This does not protect against an untrusted process writing the same inode in place on Linux: Unix sharing restrictions are not a mandatory isolation boundary. The existing local-only deployment policy requires a dedicated publishing root with minimum host permissions. `_releases` and its parent directories must be writable only by the service identity and trusted administration/backup operations; other processes or accounts must not have write access. The service does not automatically rewrite existing ACLs. Host compromise and writable shared/NFS deployment qualification are outside this fix.

## Evidence

- Deterministic pre-fix reproduction: after the real store verifies the file, a wrapper moves the original to a retained name and writes different bytes at its old path. PDF and ZIP responses both carried the recorded ETag but served a different SHA-256: **0 passed / 2 failed**.
- Windows current-source targeted capture/delivery/finalization regression: **28/28 passed**. The replacement test covers PDF/ZIP and complete/Range responses through the actual MVC result executor, checks body bytes and ETag, and verifies handle disposal. Digest refusal also proves the file can be opened exclusively afterwards.
- Instabox Debian 13 x86_64 / Linux 6.18.54-instabox: **4/4 replacement/response tests passed** on the native persistent filesystem. The test source was directly compiled there with SDK 8.0.423 / runtime 8.0.29 and Recommended analyzers against the freshly built portable layer assemblies. This is not a complete source build of the backend on Linux.
- Current Application, Infrastructure and API source compiled with .NET 8 Recommended analyzers, repository editorconfig and warnings as errors. The pre-existing Application CS9236 informational diagnostic remains. Economic-model bounded read-only implementation review PASS; no reviewer build/test/write/delete.

Backups: `backups/qa-06-20261107/`. Local evidence: `.artifacts/qa-06-20261107/{red,green,application-current,infrastructure-current,api-current,linux-tests.log,linux-compile.log,linux-payload/inputs.json}`. Remote inputs and outputs remain under `/data/home/ratools-qa-76555a3-20261107/qa06/` on Instabox `insta01`. Test cleanup uses the existing Windows recycle-bin path or retains Linux fixtures; no physical deletion is introduced.

QA-06's targeted acceptance is complete. Full backend regression, browser workflows, broader Linux resource/permission qualification, and external validation remain QA-07 work. No database migration or frontend contract change was needed.
