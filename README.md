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

Use `RATools.Api.http` for local HTTP examples. New application and import requests use `ectdTemplateKey`:

```json
{
  "applicationNumber": "IND-0001",
  "ectdTemplateKey": "us-fda-ectd-3.2.2",
  "sponsorName": "Demo Sponsor",
  "workingDirectoryParentPath": "D:\\eCTD-work"
}
```
