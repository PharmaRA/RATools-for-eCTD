# QA-07 Windows MSBuild and selected VSTest acceptance

Recorded 2026-11-07 against `65d5334` plus the source manifest below. This closes the actual MSBuild build gate for API, DatabaseMigrator and the complete Tests project, and the selected 156-case runtime gate. QA-07 remains open. Economic readonly code and final evidence review: PASS.

```powershell
pwsh -NoProfile -File ./scripts/local-check.ps1 -Task msbuild
pwsh -NoProfile -File ./scripts/local-check.ps1 -Task msbuild-tests
```

## Build and runtime scope

The runner uses retained SDK8.0.423/runtime8.0.29, actual `dotnet build --no-restore --configuration Release -warnaserror`, original repository properties/analyzers, independent output/intermediate directories and original xUnit/VSTest execution. Project references build Domain, Application and Infrastructure. Existing assets are copied after checking direct PackageReference ID/version matches. This is not a fresh restore or full restore-metadata equivalence check.

MSBuild Delete/RemoveDir tasks reject nonempty cleanup requests without deleting. File and nonempty-directory probes both fail with QA_NO_DELETE and retain their targets. The file probe also verifies rejection with TreatErrorsAsWarnings; the directory probe does not set that option. Output/TEMP inherits deny-delete ACL; a native DELETE-access-only probe returns access denied without requesting deletion. MSBUILDPRESERVETOOLTEMPFILES retains compiler response files. Children enter an owned Windows Job before resuming; process receipts confirm termination. The output ACL does not protect fixture workspaces elsewhere. A bounded source preflight checks the eleven selected classes and NodeImportWorkspace for the direct deletion forms encountered here; it is not a general sandbox.

The named RATOOLS_TEST_REPOSITORY_ROOT setting initializes the testhost working directory after marker/ancestor-link checks. Ordinary tests without this setting keep their normal working directory. The PDF joint tests use actual qpdf/PyMuPDF, publication, Registry registration, validation and finalization services, with InMemory repositories and retained input/capture adapters. They do not qualify the production input reader or PostgreSQL persistence.

## Failure evidence and repairs

Evidence directories below are relative to `.artifacts/local-check/`.

| Run | Observed result and action |
| --- | --- |
| `20261107T194407-08cdf000535b4a5d879b7b77dda15d43` | API/Migrator exit0 but compiler-response cleanup produced 4+1 MSB5018 warnings under deny-delete ACL. Not a zero-warning pass. |
| `20261107T195007-41e902c861584ac6a8a0c3ab8e6752b5` | Preserve-tool-temp setting enabled; both projects 0 warnings/0 errors. |
| `20261107T195920-7ac031534a944e88bca2ef7c0192e9a3` | Complete test compilation exposed 105 errors in 21 files: 7 C#; 81 CA1861; 9 CA1869; 3 CA1865; 2 CA1822; 2 CA1859; 1 xUnit2031. `test-compile-errors.json` preserves the inventory. Twenty files matched review baseline90f3d36; PostgresPublishCompletionReportTests had changed in QA02. |
| `20261107T201934-062df18183634ab2a6c257fdfedc31ae` | Editing utility incorrectly typed extracted array declarations as JSON options. This was an editing error, corrected before acceptance; not a product failure. |
| `20261107T202325-4233357af5c2417390c6d846418c490f` | All three projects compile with 0 warnings/0 errors after constant-array/options reuse, overload/static/concrete-type analyzer repairs and current stream/dependency contracts. No suppression or product-code change. |
| `20261107T202716-3e2992ce482d4096adba007cf0fba3fa` | 137 passed/19 failed/0 skipped. Testhost output cwd broke relative fixtures and Node fixture recycling. Explicit checked repository cwd fixes the runner setup. |
| `20261107T203029-2fbd3ffafa824ad88c7a810bb0187c59` | 152/4/0. Test rigs omitted authoritative assembly document/placement stores and regional metadata stores. Wired existing same-instance dependencies; EF branch remains compile-only. |
| `20261107T203345-876b010e24eb4123b0cd9135fe927ea6` | 154/2/0. Node assertions predated regional warnings; PDF rig manually registered an obsolete manifest without capture. Explicit warning allowlist retains severity and business assertions. Real Registry registration now supplies the complete contract; finalization checks remain unchanged. |
| `20261107T204021-976f87c13285423da4837a16a385db63` | Final three builds exit0, each 0 warnings/0 errors; VSTest **156 passed/0 failed/0 skipped**, runner exit0, process tree stopped without forced termination. |

Final 879-entry source-manifest SHA256: `93bc246f793b1cf71030b82a3cab76fec9c646b3d293da9b795b90a05b9ae8d0`. The manifest covers relevant sources, projects, explicit resources, selected fixtures and entry scripts; SDK fingerprints are separate. Web SDK implicit Content and the entire toolchain are not completely sealed by this manifest. Exact per-class TRX counts are enforced:

| Classes | Cases |
| --- | ---: |
| RegionalImportComplete / RegionalImportAcceptance / ApplicationImportNode | 24 / 3 / 16 |
| RegionalMetadataModel / RegionalMetadataEditing | 11 / 26 |
| ScopedEuRegionalWriter / ScopedUsRegionalWriter | 23 / 22 |
| ScopedEuPackageBinding / ScopedUsPackageBinding | 18 / 10 |
| PdfContentContract / PdfCombinedQualification | 1 / 2 |

## Execution deviation

Earlier selected runtime runs executed two pre-existing `File.Delete` missing-payload setup branches in ApplicationImportNodeTests. This violated the user's A2 no-physical-delete instruction. The files were task-generated fixture payloads; this scope does not excuse the violation. Earlier build-output ACL protection did not cover those fixture paths. No claim is made that earlier runs performed no deletion, and later green evidence does not reverse that event.

Both branches now move the original payload into the controlled fixture recycle directory after path/link checks, and assert the original path is absent and retained bytes are identical. Workspace disposal also recycles the owned tree. The same missing-reference/transactional-state assertions still run. Direct selected-fixture deletion preflight was added before the final run. Backups are under `backups/qa-07-local-20261107/{msbuild,test-build,test-runtime-v2,msbuild-records}/`; failed outputs and evidence remain retained. Temporary editing utilities are retained for audit, with no physical cleanup.

## Remaining acceptance

The unfiltered backend suite, fresh restore, PostgreSQL runtime branches in changed tests, wider regional end-to-end cases, native browser download-manager completion, current combined Linux/PostgreSQL/browser evidence and actual Linux qpdf/Python qualification remain unrun here. Official current scope, external validator and Formal delivery/receipt qualification remain open. Compilation of these files is not their runtime acceptance. Prior overlapping test totals are not added to 156.
