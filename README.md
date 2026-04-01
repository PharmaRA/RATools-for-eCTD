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
