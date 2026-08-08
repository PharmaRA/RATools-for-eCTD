# RATools-for-eCTD

RATools-for-eCTD is an eCTD publishing system for regulatory submission workflows. It provides a .NET backend and React frontend for managing applications, sequences, document placements, validation, publish jobs, publish artifacts, and publish history.

## Structure

- `src/RATools.Api`: HTTP API, authentication policies, Swagger, and composition root.
- `src/RATools.Application`: use cases, contracts, validation, publishing, and orchestration.
- `src/RATools.Domain`: core business entities and state transitions.
- `src/RATools.Infrastructure`: EF Core/PostgreSQL persistence, in-memory repositories, file storage, workspace policies, and local publish output.
- `frontend`: React UI for application management, sequence workspaces, validation, publishing, and publish history.
- `tests/RATools.Tests`: backend xUnit tests.
- `scripts`: local development and smoke-test scripts.

## Current Scope

- Application and sequence lifecycle management.
- Application import from an existing eCTD workspace.
- Template-driven application setup for `us-fda-ectd-3.2.2`.
- Document upload with canonical CTD section-folder storage.
- Document placement creation, section reassignment, metadata editing, and deletion.
- Sequence validation for section matches, lifecycle targets, file existence, and publish readiness.
- FDA eCTD 3.2.2 backbone generation.
- Publish job execution with report, index, checksum, package zip, and artifact metadata.
- Publish history filtering, pagination, report retrieval, artifact listing, and artifact download.
- Audit log capture for validation and publish events.

## Capability Matrix

Support maturity is tracked per region and capability. "Production-ready" means the
path is exercised by the smoke test and covered by regression tests against the
official DTDs. "Controlled skeleton" means the architecture, wiring, and readiness
dry-run exist and are tested, but the feature is intentionally minimal and not a
complete official regulatory rule set.

| Capability | US FDA eCTD 3.2.2 (`us-fda-ectd-3.2.2`) | EU eCTD 3.2.2 (`eu-ectd-3.2.2`) |
| --- | --- | --- |
| Application / sequence lifecycle | Production-ready | Production-ready |
| Workspace import | Production-ready | Production-ready |
| Document upload / placement | Production-ready (full CTD section dictionary) | Controlled skeleton (Module 1 top-level sections only) |
| Section structure tree | Production-ready | Controlled skeleton (minimal EU M1 tree) |
| Sequence validation (sections, lifecycle, readiness) | Production-ready | Production-ready (shared checks; EU sections validated against the skeleton dictionary) |
| ICH M2–M5 backbone (`index.xml`) | Production-ready | Production-ready (shared writer) |
| Regional Module 1 backbone | Production-ready (`us-regional.xml`) | Controlled skeleton (`eu-regional.xml`, bundled placeholder DTD) |
| Publish job execution + artifacts | Production-ready | Controlled skeleton (end-to-end publish covered by regression test against the placeholder DTD) |
| PDF compliance rules | Production-ready (rule engine) | Shared rules apply |
| Publish history / audit | Production-ready | Production-ready |

Notes:

- EU support is an intentionally narrow second region added to prove the multi-region
  architecture. The bundled `reference/dtd/eu-regional.dtd` is a test/architecture
  placeholder, not the full official EU Module 1 validation rule set. The EU section
  dictionary (`EuEctd322`) covers only Module 1 top-level sections, matching the EU
  regional writer's m1-only boundary; expanding EU support means replacing both with
  the official EU M1 specification artifacts.
- PDF compliance runs through the shared eCTD validation rule engine
  (`IEctdValidationRule`), so region-specific PDF policies can be layered on later
  without bespoke checks in the readiness service. Font-embedding and security
  restriction checks are tri-state: findings distinguish verified failures from
  "could not verify" (reported as low-severity for manual review).

## Local Run

Start PostgreSQL:

```powershell
docker compose up -d
```

Run the backend:

```powershell
dotnet run --project src/RATools.Api/RATools.Api.csproj
```

Run the frontend:

```powershell
cd frontend
npm install
npm run dev
```

Open `http://localhost:3000`. The Vite dev server proxies `/api` and `/health` to the backend at `http://localhost:5000`.

Default backend configuration is in `src/RATools.Api/appsettings.json`.

## Key Configuration

- `Security:ApiKey`: required for non-InMemory providers; startup fails fast when empty.
- `Security:AllowedWorkspaceRoots`: whitelist roots for every workspace read/write/delete.
- `Security:AllowDestructiveOperations` (default `false`): gates `deleteMode=PurgeWorkspace`
  (recursive workspace deletion). Keep it off unless an environment explicitly needs purge.
- `FileStorage:MaxUploadBytes` (default 500 MB): request body / multipart upload limit.
- `BackboneOutput:RetainJobRuns` (default 5): publish keeps the newest N `_jobs/{jobId}`
  delivery copies per application and prunes older ones; `_artifacts` and `_packages`
  are never pruned. Set `0` or negative to disable pruning.

## Working Directories

- Creating an application requires `workingDirectoryParentPath`.
- The backend creates and stores `{workingDirectoryParentPath}/{applicationNumber}` as the application working directory.
- Creating a sequence automatically creates `{applicationWorkingDirectoryPath}/{sequenceNumber}`.
- Creating or importing an application uses `ectdTemplateKey`, for example `us-fda-ectd-3.2.2`.
- The recommended upload endpoint is `POST /api/applications/{id}/sequences/{sequenceNumber}/documents/upload` with multipart fields `File` and `CtdSection`.
- Uploads are written into the canonical section folder under the sequence workspace, for example `m1\us\11-forms`.
- Reassigning a placement through `PUT /api/document-placements/{id}/section` moves the physical file into the new canonical section folder and updates the stored document path.
- Importing an existing workspace is available through `POST /api/applications/import`, which scans sequence subdirectories and reads each sequence `index.xml`.
- The import endpoint expects `workingDirectoryPath`, `ectdTemplateKey`, and `sponsorName`; `applicationNumber` is inferred from the imported directory name.

## Database Migrations

Create a migration:

```powershell
dotnet ef migrations add <MigrationName> --project src/RATools.Infrastructure/RATools.Infrastructure.csproj --startup-project src/RATools.Api/RATools.Api.csproj --context RAToolsDbContext --output-dir Persistence/EfCore/Migrations
```

Apply migrations:

```powershell
dotnet ef database update --project src/RATools.Infrastructure/RATools.Infrastructure.csproj --startup-project src/RATools.Api/RATools.Api.csproj --context RAToolsDbContext
```

The API applies migrations automatically on startup when `Persistence:Provider` is `PostgreSql`.

## Useful Commands

- Check PostgreSQL health: `docker ps`.
- View PostgreSQL logs: `docker logs ratools-postgres`.
- Stop database: `docker compose down`.
- Run backend tests: `dotnet test tests/RATools.Tests/RATools.Tests.csproj`.
- Run frontend tests: `cd frontend && npm test`.
- Build frontend: `cd frontend && npm run build`.

### Real-PostgreSQL constraint tests

`tests/RATools.Tests/Persistence/Postgres` verifies constraint semantics that only a real
PostgreSQL can enforce: the case-insensitive unique index on `ApplicationNumber`, the partial
unique index that permits one active publish job per sequence, `ON DELETE CASCADE`/`RESTRICT`
propagation, and transaction rollback. EF InMemory enforces none of these, and SQLite's
`EnsureCreated()` never creates indexes declared as raw migration SQL.

These tests need a database, obtained one of two ways:

- **Docker** (how CI runs them): [Testcontainers](https://dotnet.testcontainers.org/) starts a
  throwaway `postgres:16` container automatically. No configuration needed.
- **An existing instance** (for machines without Docker): set `RATOOLS_TEST_POSTGRES` to a
  connection string. The target database is migrated, so point it at a scratch database:

  ```powershell
  $env:RATOOLS_TEST_POSTGRES = "Host=localhost;Port=5432;Database=ratools_tests;Username=postgres;Password=postgres"
  dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter "FullyQualifiedName~Persistence.Postgres"
  ```

With neither available the tests report as **skipped**, which is expected on a plain developer
machine — they are not silently passing. CI always runs them for real.

## Smoke Test

Start the API first:

```powershell
dotnet run --project src/RATools.Api/RATools.Api.csproj
```

Run the end-to-end smoke test:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
```

Optional smoke-test arguments:

- `-BaseUrl http://localhost:5001`: target a different API URL.
- `-KeepSampleFile`: keep the temporary sample file.
- `-SkipAuditCheck`: skip audit linkage checks.
- `-CleanPublishOutput`: clean publish output before run.
- `-InjectWarnings`: inject warning scenarios to verify warning counts and summaries.
- `-CorruptReportAfterPublish`: corrupt the persisted publish report to verify tolerant report/history handling.

The smoke test covers application and sequence creation, document upload, canonical storage, placement reassignment, validation, publish execution, persisted report retrieval, artifact listing, artifact download, publish history filters, audit logs, duplicate file-name handling, and generated backbone metadata.

## API Examples

Publish job endpoints follow a create-vs-execute split:

- `POST /api/publish-jobs` creates a publish job resource and returns `201 Created` with `PublishJobDto`.
- `POST /api/publish-jobs/execute` enqueues background execution and returns `202 Accepted` with `PublishJobDto`;
  poll `GET /api/publish-jobs/{id}` for status and fetch `/report` and `/artifacts` once completed.

Use `RATools.Api.http` for local HTTP examples. New application and import requests use `ectdTemplateKey`:

```json
{
  "applicationNumber": "IND-0001",
  "ectdTemplateKey": "us-fda-ectd-3.2.2",
  "sponsorName": "Demo Sponsor",
  "workingDirectoryParentPath": "D:\\eCTD-work"
}
```
