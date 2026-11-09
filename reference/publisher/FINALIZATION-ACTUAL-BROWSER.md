# Actual backend finalization browser acceptance

Current status (2026-11-09): the November8 current-MSBuild/browser section below
accepts nonempty Development native downloads and API/PostgreSQL clean process
restart. Formal capture/delivery/receipts remain open, including the regional
context implementation gate; see03-F in the [QA-07 closure checklist](QA-07-CLOSURE-CHECKLIST.md).
The November7 counts and open-gate descriptions below are historical.

2026-11-07 QA-07 now has real Chromium evidence against the current source API and production frontend for a **synthetic empty US Development sequence with InMemory persistence**. This complements the earlier synthetic API contract tests. It does not qualify Formal delivery, receipts, PostgreSQL restart or nonempty PDF packages.

The unified Instabox entry is:

```powershell
pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Scope backend-browser -Machine <existing-machine-id>
```

`-Task build` compiles the same frontend/backend scope without the business browser flow. The explicit sync rules include four backend `.cs`/project files and project-declared embedded resources, plus the current frontend/lock and runner. They exclude appsettings, credentials, real business data, backups, bin/obj and Windows assemblies. Source manifest/ZIP hashes bind uncommitted changes. Every run has its own workspace, runtime configuration, browser profile and retained evidence. The API uses a fresh task-only credential kept in process memory, an ephemeral Data Protection startup extension and loopback dynamic port; only task-owned API/browser processes are stopped.

Backend compilation uses Linux SDK 8.0.423/runtime 8.0.29, current project dependency versions, SDK/logging generators, C# 12, nullable and compiler warnings-as-errors. The bounded downloader resolves supported net8/earlier compatible/netstandard package assets with recorded URLs and SHA-256; it omits Design-only assets. **It is not NuGet restore/MSBuild/Recommended-analyzer parity.** Direct csc suppresses CS1701 for the existing Prometheus net6/net7 references, after the diagnostic was observed. The frontend uses its committed lock, app/config TypeScript, Vite and unchanged gzip budgets.

## Final retained run

`.artifacts/remote-check/20261107T124931-5368ecb0d79346fdaa1314bf9ef8bdb9/results/`, base HEAD `0f02078`, source archive SHA-256 `594af0baed295e46a1abc3b488d2d2c3ba5db5696b27dc109c651e90ecf93eae`, final **exit 0**. Source identity includes the then-uncommitted capture fix and runner. Remote directory mirrors the run name under `/data/home/ratools-remote/`.

`backend-browser.json` has **6 passed checks, errors []**:

1. Missing US metadata returns HTTP 400 with `US_REGIONAL_METADATA_MISSING` and the exact field; metadata is then supplied through real HTTP.
2. The actual writer's ZIP is registered, captured and independently validated through the real worker.
3. The operator selects the real completed run in Chromium, explicitly confirms Development capture and creates one bound immutable snapshot; the workspace locks at revision 1.
4. The UI hides delivery registration and the actual API rejects Development delivery with HTTP 409.
5. Chromium downloads the real immutable ZIP. Its persisted **68,835 bytes** and SHA-256 `ccc99dcdefa90280421d326513e894e92358032989b8d6105ac91c753632ed86` equal the manifest. Retained path: `/data/home/qa07-real-03cdb4ac/downloads/delivery-package.zip`.
6. The UI opens a draft at revision 2, permits editing and retains the identical old manifest across a browser reload. This is browser reload, **not an API/database restart**.

Two runner regressions pass: a missing executable rejects the awaited start, and an unresponsive HTTP service is aborted at its deadline. Three capture-recycling checks also pass; see [PACKAGE-CAPTURE-RECYCLE.md](PACKAGE-CAPTURE-RECYCLE.md). These are separate checks, not a complete backend test total.

Earlier failures are retained: SDK-policy matching and CS1701 were build-harness problems; an incorrect auth header caused 401 before business tests. Economic read-only review identified missing spawn-error handling and unbounded fetch; both were reproduced first (`122037-*`, `122439-*`) and corrected. Real HTTP discovered the US metadata 500 (`123836-*`, fixed separately) and physical capture cleanup refusal (`124743-*`, fixed separately). No deletion guard was disabled.

## Open gates

Formal capture remains blocked by regional business qualification, and Development snapshots cannot register delivery. The successful Development checks do not establish the Formal capture/delivery/receipt chain. Nonempty PDF browser fixtures, PostgreSQL-backed browser persistence/restart, full frontend/backend suites, native Linux process-tree CPU/memory enforcement/cancellation, version-fixed external validator comparisons and current official regulatory scope remain open.

Economic-model final read-only review verified source boundaries, exact result counts/archive identity, downloaded ZIP and revision transition: PASS. It did not execute build/test/install/write/delete or read authentication.

## Nonempty PDF continuation

The same unified entry now creates an independent deterministic 598-byte, one-page PDF. Through the actual API it creates explicit M2/M2.2 nodes, uploads an assembly source, previews and confirms one row, awaits the real worker, generates the package and runs independent validation. Chromium then captures Development evidence, downloads the ZIP, opens a draft and rereads the unchanged historical snapshot. No production source change was necessary for this additional scene.

Final command: `pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Scope backend-browser -Machine <existing-machine-id> -RemoteRoot /tmp/ratools-remote`.

Run `20261107T131935-2d9e0298028a490193a99405e1c6c141`, base `14445b0`, uploaded source SHA256 `411f4c57dbd3e58b344b3af9a9e6c86b557e2ede39c980fed8ee5b336e0b1563`: **overall exit0**, browser **7 passed / errors []**, independent Python ZIP check **3 passed / 0 failed / 0 skipped**. Existing process-tree/recycling/helper/build steps all returned zero; overlapping regressions are not added. Economic read-only final review PASS.

The archived PDF and downloaded ZIP's PDF bytes equal the original fixture (SHA256 `94045f896c5b6d7e89b3782e0bb564d23051b47f859bb6368a8ad9772994f106`). The independent ZIP reader also verifies the backbone PDF href and MD5. ZIP length is **69,937 bytes**, SHA256 `b1f1b7e9a76495b94e1f9373e470d7a19c38426bfd333ed6505895360a4baeb0`, retained at `/data/home/qa07-real-080457d2/downloads/delivery-package.zip`. Locked revision4 becomes editable revision5; original manifest is unchanged. Download/PDF reports and the fixture itself are copied into local `results/`.

Runner failures retained: `130936-202ea6e88cc5441fbe41a81c4e968597` treated the assembly numeric enum as strings; corrected to wait for 0/1 and require 2. `131230-eb0db8fcaeea455aa1643b7dc6b1763e` stopped at the separate procfs race, repaired in `14445b0`. `131500-13f67262b488429aa5a4b37f3bc4b31b` passed the browser but the ZIP checker incorrectly required one total leaf; a valid regional XML leaf coexists with the PDF. The checker now selects PDF leaves and uses the actual ICH3.2.2 legacy xlink namespace. These runner errors do not establish production PDF defects.

`/data` had only about 91,374 free inodes/1.51GB; the next run used the separate `/tmp` filesystem with about 1,033,395 free inodes/4.04GB. `-RemoteRoot` allows exactly the two named roots with case-sensitive validation. The uppercase-root negative check was rejected before remote execution, and the final script successfully resumed the `/tmp` run into a new retrieval directory. This local entry validation is not a local product-test fallback. The archived runner predates only this local case-sensitivity tightening; no remote worker changed afterward. `/tmp` artifacts are not guaranteed to survive machine restart; downloaded local evidence and the `/data` ZIP remain the durable record.

The tested scope remains **InMemory / US / Development / one synthetic PDF**. PostgreSQL-backed API/database restart, Formal successful delivery/receipt and complex PDF content remain unrun. A bounded remote tool probe found no postgres/initdb/psql/pg_ctl executable or `/usr/lib/postgresql` installation; setup is a prerequisite, not a database test failure.

## PostgreSQL product restart acceptance

The current API, production frontend and **actual DatabaseMigrator source** also run in a fresh isolated PostgreSQL16.15 cluster:

```powershell
pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Scope backend-pg-browser -Machine <existing-machine-id> -RemoteRoot /tmp/ratools-remote
```

Final run **20261107T135532-f8772164aa6c40dda54fad62e4b27f0d**, base **9b21dfe**, source archive **8a7c7ed8a8d99401102f0f2e4ca932f0c5fd20fd5885a3c7d706c5524ad895da**. Whole job exits **0**, browser **8 passed / errors []**, independent ZIP **3/3, no skips**. Earlier overlapping helper, capture, process and build checks remain separately reported.

The migrator output is explicitly asserted: **31 migrations applied** on the fresh cluster; after restart, **no migrations applied**. The full nonempty-PDF workflow runs against PostgreSQL. After capture/download/draft, the API must stop with exit0 before requesting a database restart. The parent stops PostgreSQL, starts it and verifies actual SQL readiness before permitting a new API process. The API PID changed **68453 → 69123**. The restarted API returns identical full workspace, manifest and finalized-version list, and Chromium reloads and opens the retained snapshot.

The actual report endpoint is read before and after restart, with HTTP200 and exact original-byte comparison. Both retained JSON files are **26,584 bytes**, SHA256 **8d7e665f83b15a7e4048494dd6dd6c52d69e23c64ceaa28d700dd013b4aaa901**. A boolean report-availability flag alone is not accepted. The downloaded ZIP is **69,937 bytes**, SHA256 **de016458f2f3edb961041cc75b5aad6bde968f6facaa4bcddf20056ca66f2f2b**, retained under `/data/home/qa07-real-fbf42c9d/downloads/`. Independent PDF byte/backbone checks pass against the original598B fixture.

The PostgreSQL adapter is confined to native database processes. Product API/browser/migrator inherit the no-delete seccomp guard, without the PostgreSQL preload shim. Four database shutdown process lists and the final remaining-native list are empty;45 recycle entries are retained. `postgres-platform.json`, `backend-browser.json`, `download-pdf.json`, migration logs and both full report files are downloaded locally. Prior135216-30d76a491bb3427d987c4c9de1c9d111 ran successfully but lacked explicit migration-count and report-byte assertions; it is supporting evidence, superseded by this final run.

This closes the selected **PostgreSQL-backed Development browser plus API/database process-restart** gate. It does not cover machine reboot, crash/power-loss recovery, all historical migrations, the complete backend/frontend suites, Windows, complex PDF cases or Formal successful delivery/receipts. Compilation remains the documented Linux direct-csc path, not MSBuild/analyzer parity. No production business code or qualification gate changed in this subtask.


## 2026-11-08 current MSBuild/browser/native-download acceptance

Current API and DatabaseMigrator binaries now come from the actual Linux MSBuild
run recorded in [QA-07-ENVIRONMENT-RECOVERY.md](QA-07-ENVIRONMENT-RECOVERY.md), not
from the historical direct-csc backend build. Before reuse, all 862 source inputs
were rechecked; 111 API output files and 52 independent migrator output files
were bound to SHA-256 inventories. Independent copies are used for the browser.
The original API deps manifest is retained, with only the test-only ephemeral
DataProtection startup assembly added to its copied manifest. Product source and
business qualification gates are unchanged.

Frontend evidence is `.artifacts/local-check/browser-build-20261108T215950-044b983f83bb492b834cb102018ab983/`:
217 input hashes (211 frontend and six supporting inputs) match current files,
193 app roots plus one node root have zero TypeScript diagnostics, and all 29
original production budgets pass. Build Job exit0/stopped/forcedfalse. Thirty
static assets are bound to the uploaded browser archive. This is a **Windows
production frontend build running in a Linux browser**, not a Linux frontend
build or fresh frontend installation claim.

InMemory browser run
`.artifacts/local-check/linux-browser-20261108T223734-eca83af56b5045bdac0651d794110a83/`
passes seven checks plus three independent ZIP/PDF checks. Native Chromium emits
`Browser.downloadProgress` with `state=completed`; the persisted ZIP is 69,937
bytes, SHA-256 `261ff5d06a46e076e0dc6da0db211d4cfb2668718f49c44d37ce4f4d8ec1ca88`.
Both ZIP and original 598-byte synthetic PDF were retrieved locally. Source
archive SHA-256 is `91e237776124760fe753bfe87379200dceb562c7f2ca5e07ec48d23e235b5bb9`.
First run `222822-fc91c4e4a8894b2cb0ba090c9d66e608` passed upload/assembly/writer/
validation but Chromium could not create its too-long Unix socket path. The next
run used a unique short `/dev/shm/rb-*` root. Failed profile/crash evidence remains;
seccomp was not relaxed.

Current PostgreSQL restart run
`.artifacts/local-check/linux-pg-browser-20261108T230609-61d082f80708478bb997a3d41c323869/`
passes **eight browser checks and three ZIP/PDF checks**, overall exit0, no timeout,
truncation or OOM. A unique database on the verified PG16.15 cluster receives 31
migrations; after API and database clean restart, the migrator applies zero.
The API PID changes 113137 to 113473. Full workspace, immutable manifest and
finalized-version list remain equal; the browser reloads and reopens the snapshot.
Both before/after report files are exactly 26,584 bytes and byte-identical,
SHA-256 `c18e08ada6317f7e8593989bcbdb2e8ea86e8a60ff4acc762597fe6a1aa9a0ca`.
The native download completes and the local ZIP is 69,937 bytes,
SHA-256 `31b6917f3ed2f31a47e8b85caa88cdd87ed47de434f1ed126efb11ae8d1fbbd3`.
Its one PDF is byte-identical to the uploaded synthetic fixture, and backbone
href/MD5 match. Source archive SHA-256:
`0d5df197527df7d508cb321dffdeb45728c1c410752eb2b208a4e84795e67327`.

Two PG shutdown lists, the coordinator native list, outer PG list, all three
stage process groups and Chromium-profile list are empty. The task acquires a
lifecycle marker only after refusing any existing PG instance; outer cleanup
can therefore stop owned PG workers even if the coordinator times out. The exact
no-delete.py launch and source guard hash are retained. Timeout cleanup is
reviewed code, not a dynamically injected timeout acceptance claim.

First PG browser run `230046-58abc31c3b3e4ebf876acd8a7f4b9b10` failed before product
migration because copying only the three migrator main files omitted its
configuration dependency. The final run preserves the complete 52-file migrator
output separately and points the existing browser protocol to it. That earlier
failure and its clean PG shutdown remain recorded.

These results close the current Development native browser download and selected
PostgreSQL/API process-restart gates. They do not qualify machine reboot/crash
recovery, complex PDF content/OCR, the complete native test suite, a single
combined resources/native/database run, or Formal successful delivery/receipts.
The InMemory accepted driver predates the added PG branch; each source archive
binds its own result. Only the final PG run covers the current PG driver branch.
