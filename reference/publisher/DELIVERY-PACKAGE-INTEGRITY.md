# Manifest-bound delivery ZIP

QA-03 / R-03, 2026-11-06. Implementation baseline: `7f0282f`.

`LocalReleaseCaptureStore.SaveAsync` first copies and measures the planned release files, then creates `delivery-package.zip` from the measured `Payload` members. ZIP entry names strip only the `payload/` prefix. Entries are sorted using ordinal path order, stored without compression, and receive a fixed 1980 timestamp and zero external attributes. Validation reports and regional evidence remain beside the payload, outside the ZIP.

The ZIP is a `ReleaseFileKind.DeliveryPackage` member at the fixed release-relative path `delivery-package.zip`. Its measured length, SHA-256 and MD5 participate in the canonical manifest digest. The tool version is `ratools-finalization-v3`; the manifest schema and existing member values remain compatible with older captures.

Before writing `manifest.json`, `CompleteAsync` checks that the supplied manifest lists exactly the staged records, remeasures every file, and verifies that the ZIP entry set and each entry's length/SHA-256/MD5 reproduce the payload inventory. Duplicate, missing, additional and renamed ZIP entries are refused. A ZIP whose whole-file digest was updated to hide different entry bytes is still refused.

The existing finalized-file endpoint serves this manifest member with `application/zip`. `RegisterDeliveryAsync` retains the Formal/ready gate, opens the captured ZIP through the verified member reader, and accepts only that SHA-256. Missing legacy ZIP bindings cannot authorize new delivery records; existing legacy manifests and historical delivery/receipt facts remain readable. ZIP integrity failures map to `DELIVERY_EVIDENCE_INVALID` (409).

Cleanup uses the Windows recycle bin after containment and ancestor/member reparse checks. On other platforms, task-owned isolation directories are retained. The junction regression deliberately retains its link fixture rather than recursively cleaning it. These checks do not close QA-06 / R-15: the current path-based reader still verifies and subsequently reopens bytes, so the response-handle race requires its separate fix.

Evidence is under `.artifacts/qa-03-20261106/`:

- Original arbitrary-digest regression: 32 passed, 1 expected failure.
- `review-red`: 44 passed, 2 expected closure failures.
- `review-red-extra`: 44 passed, 4 expected closure/ancestor failures.
- `final-green`: 48 passed, 0 failed, with per-case instance disposal.
- Domain/Application complete source compilation plus Infrastructure/API stage 3 compilation: .NET 8 Recommended analyzers, repository editorconfig and warnings as errors.
- OpenAPI adds only the new enum member; two independent `clean:false` TypeScript generations produce identical file lists and bytes.

These are Windows targeted acceptance results. Full backend regression, a complete browser download/delivery/receipt flow, Linux and external qualification were not run. No new PostgreSQL schema or persistence behavior is introduced by this item. The original red runner accidentally invoked legacy physical cleanup; `EXECUTION-DEVIATION.md` records the violation, and all subsequent disk regressions use the recycle-bin implementation.
