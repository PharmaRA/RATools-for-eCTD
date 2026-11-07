# Actual backend finalization browser acceptance

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
