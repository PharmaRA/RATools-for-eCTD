# Package capture recycling

QA-07 actual Linux API acceptance exposed a registered ZIP returning HTTP 403 during capture disposal. `PackageInputReader` still physically deleted its owned flat files, unchanged from review baseline `90f3d36`; the inherited no-delete guard refused that cleanup. A successful registration could therefore be followed by a failed response.

The reader now retains successful and failed captures by moving the complete owned directory into `<capture-parent>/_recycle/<input-guid>-<guid>`. It checks the resolved direct parent against the original reader root, validates the input GUID name, checks all direct entries and ancestors for links and refuses unexpected directories. The destination is unique, within the same root and filesystem, and is checked for links before moving. It does not delete source files or overwrite an earlier capture. Repeated successful disposal remains idempotent. Failed safety checks retain the active directory for inspection.

Recycled evidence consumes disk space; no automatic expiry or physical cleanup is added. Workspace/capture storage and ancestors must remain restricted to the service account. Path checks do not establish protection against a hostile concurrent filesystem writer. Upload-failure cleanup outside this reader remains a separate unqualified path; it was not exercised or changed here.

## Evidence

- Red: `.artifacts/remote-check/20261107T124743-d727b4c349e04b5191b65b5b3d3fe29c/results/capture-recycle.log` records `FileSystem.DeleteFile` failing under seccomp during `DisposeAsync`.
- Green: `20261107T124931-5368ecb0d79346fdaa1314bf9ef8bdb9/results/capture-recycle/` records **3 passed, zero failed**: successful disposal/repeated disposal retains exact bytes; unsuccessful selection retains its capture and original input error; a linked recycle destination is rejected with the active capture and source preserved.
- The same run completes real API registration/independent validation and the Development browser capture/download/draft flow, **6 checks**, exit 0. Base HEAD `0f02078`, uploaded current-worktree archive SHA-256 `594af0baed295e46a1abc3b488d2d2c3ba5db5696b27dc109c651e90ecf93eae`.
- Production layers were compiled on Linux from synchronized source by direct csc with SDK generators, compiler warnings-as-errors and CS1701 compatibility suppression. This is not full MSBuild/Recommended analyzer or Windows qualification. No standard complete backend suite was run.
- Backup: `backups/qa-07-20261107/backend-browser/PackageInputReader.cs`. Test: `scripts/remote-check/CaptureRecycleProbe.cs`; entry `scripts/remote-check.ps1 -Task test -Scope backend-browser`.

Economic-model final read-only review verified source boundaries, exact result counts/archive identity, downloaded ZIP and revision transition: PASS. It did not execute build/test/install/write/delete or read authentication.
