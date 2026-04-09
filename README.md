# RATools-for-eCTD

Backend starter for an eCTD publishing system using a layered architecture.

## Structure

- `src/RATools.Api`: HTTP API and composition root.
- `src/RATools.Application`: use cases, contracts, and orchestration.
- `src/RATools.Domain`: core business model and invariants.
- `src/RATools.Infrastructure`: persistence and external service implementations.

## Current scope

- Foundation with layered boundaries and sample Application aggregate.
- Persistence uses EF Core + PostgreSQL.

## Local run

- Start PostgreSQL and create a database named `ratools` (or update connection string).
- Quick start with Docker: `docker compose up -d`.
- Default connection string is in `src/RATools.Api/appsettings.json`.
- Run: `dotnet run --project src/RATools.Api/RATools.Api.csproj`.

## Database migrations

- Create migration:
  `dotnet ef migrations add <MigrationName> --project src/RATools.Infrastructure/RATools.Infrastructure.csproj --startup-project src/RATools.Api/RATools.Api.csproj --context RAToolsDbContext --output-dir Persistence/EfCore/Migrations`
- Apply migrations:
  `dotnet ef database update --project src/RATools.Infrastructure/RATools.Infrastructure.csproj --startup-project src/RATools.Api/RATools.Api.csproj --context RAToolsDbContext`
- The API now applies migrations automatically on startup when `Persistence:Provider` is `PostgreSql`.

## Useful commands

- Check PostgreSQL health: `docker ps`.
- View PostgreSQL logs: `docker logs ratools-postgres`.
- Stop database: `docker compose down`.

## Smoke test

- Start the API first: `dotnet run --project src/RATools.Api/RATools.Api.csproj`
- Run the end-to-end smoke test: `powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1`
- Optional: target a different API URL: `powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1 -BaseUrl http://localhost:5001`
- Optional: keep the temporary sample file: `powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1 -KeepSampleFile`
- Optional: skip audit linkage check: `powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1 -SkipAuditCheck`
- Optional: clean publish output before run: `powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1 -CleanPublishOutput`
- Optional: inject warning scenarios to verify warningCount/warningSummary: `powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1 -InjectWarnings`
- Optional: corrupt the persisted publish report to verify tolerant report/history handling: `powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1 -CorruptReportAfterPublish`
- The smoke test now uses `POST /api/publish-jobs/execute` and prints the unified publish report summary.
- The smoke test also round-trips `GET /api/publish-jobs/{id}/report` to verify persisted report retrieval.
- The smoke test also checks `GET /api/publish-jobs/{id}/artifacts` and verifies the expected publish outputs are present.
- The smoke test also checks `GET /api/applications/{id}/publish-history` and verifies the current publish job appears in application history.
- The smoke test also verifies `publish-history` filtering and pagination using `sequenceNumber`, `page`, and `pageSize`.
- The smoke test also verifies `publish-history` status and `createdUtc` range filters.
- The smoke test also verifies `publish-history.statusSummary` values for the current filtered history views.
- The smoke test also verifies `publish-history.lifecycleSummary` values for the default non-lifecycle scenario.
- The smoke test also downloads `PublishReport` and `PackageZip` through the artifact download endpoint and verifies the responses match artifact metadata.
- With `-InjectWarnings`, the smoke test also verifies `NON_STANDARD_SECTION_PATTERN` is returned in the validation report.
- The smoke test also verifies validation `sectionMatches` and `MatchedPrefixes` audit details.
- The smoke test also verifies `lifecycleMatches` and `LifecycleResults` summaries for the default validation path.
- When audit checks are enabled (default), the script prints filtered PublishJob, SequenceValidation, and PublishJobArtifact audit details for the current run.
- The smoke test also verifies that `publish-report.json`, `index.xml`, and the packaged zip are all created.
- The smoke test also verifies the packaged zip path is job-specific so repeated publishes do not overwrite history.
- The smoke test also verifies `index.xml` uses a job-safe unique document `href` based on the uploaded document id.
- The smoke test now uploads two documents with the same file name and verifies their published `href` values remain unique.
- The smoke test also verifies the generated backbone includes `dtd-version="3.2.2"`, `xlink:type="simple"`, and `checksum-type="md5"`.
