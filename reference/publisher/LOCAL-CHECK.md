# Local Windows QA checks

On2026-11-07 the user explicitly authorized local testing when remote storage is insufficient. This supersedes the remote-only execution preference for the current authorized continuation. It does not make Windows evidence equivalent to Linux qualification. Remote data remains retained without expansion, deletion or new jobs.

```powershell
pwsh -NoProfile -File ./scripts/local-check.ps1 -Task build
pwsh -NoProfile -File ./scripts/local-check.ps1 -Task pdf
```

Each invocation creates a new `.artifacts/local-check/<timestamp-guid>` directory. Current Domain/Application/Infrastructure/API source is compiled using the retained SDK8.0.423/runtime8.0.29 and current embedded resources. Direct PackageReference versions must match existing net8.0 restore assets; absent or conflicting dependencies fail. Test runtime dependencies have an independent output directory. Recommended .NET8 analyzers, source generators, repository editorconfig and warnings-as-errors are applied. No MSBuild clean/rebuild is invoked; test temporary evidence is retained or uses the reviewed Windows recycle mechanism.

This is a bounded direct-csc runner, not full MSBuild or xUnit-executor parity. `pdf` requires exactly47 passed,0 failed,0 skipped across five suites: concurrency7, engine19, navigation10, content resources5, content workflow6. It exercises actual installed qpdf12.4.1 and configured local Python/PyMuPDF content fixtures on Windows, without claiming arbitrary document or Linux capability.

Current-source Recommended compilation exposed CA1861 in the new LinuxPdfResourceScope introduced by9dda41b. The fixed controller list is now a static readonly field; no analyzer level or resource behavior changed. Red evidence:150346-65ff0ed1c1ae41ea9cf115c5c31b4d97 Infrastructure compile log. An earlier missing generated AssemblyVersion was a runner mistake. A test/API System.IO.Pipelines6.0.3/8.0.0 runtime conflict was also a runner failure, resolved through separate test output.

Final run **20261107T151402-cb86e36a42134ff39d3338fea57ab590**, base091d08f plus the recorded changes: four layers and selected tests compile; **47/0/0**, exit0. Source-manifest SHA256 **3d5ed66ab0cdba3a0b312316179414fc94d5e555ecbd739e5593ab63dee830be** records573 inputs; dependency-hashes records1634 compiler/reference/analyzer/generator hashes. Economic read-only code/evidence review PASS. Backups: `backups/qa-07-local-20261107/`.

Unrun in this checkpoint: full backend/frontend suites, standard MSBuild, DatabaseMigrator, PostgreSQL/browser current-head integration, latest Linux integration and external/Formal qualification. The lost remote job remains unaccepted. Continue the documented QA07 gaps and then the external-ZIP browser chain in the subsequent business plan.
