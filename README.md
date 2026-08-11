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

Frontend development requires Node.js 22.18 or newer.

Open `http://localhost:3000`. The Vite dev server proxies `/api` and `/health` to the backend at `http://localhost:5000`.

Default backend configuration is in `src/RATools.Api/appsettings.json`.

## Supported Deployment Boundary

The current release supports one trusted operator, one API/worker process, and a
browser, API, PostgreSQL database, and workspaces on the same controlled host. Keep
the API and database bound to loopback; do not expose this build through a LAN,
reverse proxy, public address, or multiple replicas.

Startup enforces this boundary: `Deployment:Mode` must remain `LocalOnly`, every API
listener and PostgreSQL host must be loopback, and a cross-process lock rejects a
second relational API/worker process. Outside the `Development` environment, startup
also rejects short/development API keys and default PostgreSQL passwords. These checks
are support boundaries, not a substitute for host access controls.

The browser-visible shared API key is an access gate, not a user identity or a
browser secret. The public audit API is read-only: validation and publish business
services create audit events and derive the current `system` actor on the server.
This protects audit records from client-supplied actors, but it does not provide
multi-user attribution or non-repudiation. Shared or horizontally scaled deployment
remains unsupported until the remaining identity and migration controls in the
locally maintained ADR-0001 are complete. ADRs are not part of the public repository.

The tracked Compose port is bound to `127.0.0.1`, but its credentials and the
development settings are still development conveniences, not a hardened deployment
profile. Review the ADR's local-only requirements before using real regulatory documents.

## Key Configuration

- `Deployment:Mode`: fixed to `LocalOnly`; any other value is rejected because shared
  deployment is not implemented.
- `Deployment:InstanceLockPath` (default `App_Data/ratools-api.lock`): cross-process lock
  held for the lifetime of a relational API/worker process. Put all replicas of one local
  installation on the same lock path; only one may run.
- `Urls`: configured values must contain only HTTP(S) loopback listeners. Configured
  `Kestrel:Endpoints:*:Url` values are checked by the same rule.
- `Security:ApiKey`: required for non-InMemory providers; outside `Development`, it must
  be a non-development value of at least 32 characters.
- `Security:AllowedWorkspaceRoots`: whitelist roots for every workspace read/write/delete.
- `Security:AllowDestructiveOperations` (default `false`): gates `deleteMode=PurgeWorkspace`
  (recursive workspace deletion). Keep it off unless an environment explicitly needs purge.
- `FileStorage:MaxUploadBytes` (default 500 MB): request body / multipart upload limit.
- `BackboneOutput:RootPath`: server-controlled publish output root. It must be inside a
  `Security:AllowedWorkspaceRoots` entry; publish requests cannot override this physical path.
- `BackboneOutput:RetainJobRuns` (default 5): publish keeps the newest N `_jobs/{jobId}`
  delivery copies per application and prunes older ones; `_artifacts` and `_packages`
  are never pruned. Set `0` or negative to disable pruning.
- `PublishJobs:ExecutionTimeout` (default `00:15:00`): maximum execution time for a queued
  publish attempt. A timed-out or interrupted attempt is returned to the durable queue until
  `MaxAttempts` is exhausted.
- `PublishJobs:PollInterval` (default `00:00:01`): maximum delay before a worker polls the
  database when no in-process wake signal is available.
- `PublishJobs:LeaseDuration` (default `00:01:00`): exclusive database claim lifetime.
- `PublishJobs:HeartbeatInterval` (default `00:00:15`): lease renewal interval; it must be
  shorter than `LeaseDuration`.
- `PublishJobs:RetryDelay` (default `00:00:05`): delay before a failed attempt is eligible again.
- `PublishJobs:MaxAttempts` (default `3`): maximum claims before an execution failure becomes
  terminal. Lease tokens fence stale workers from persisting state after ownership changes.
  Startup recovery marks only `Running` jobs with expired or missing leases as `Failed` and
  never touches `Pending` jobs or another instance's unexpired lease.

For a non-development local run, override both tracked development credentials and keep
all endpoints on loopback, for example with `ASPNETCORE_URLS`, `Security__ApiKey`, and
`ConnectionStrings__PostgreSql` environment variables. Startup validates these values
before acquiring the instance lock or applying a database migration.

## Working Directories

- Creating an application requires `workingDirectoryParentPath`.
- `applicationNumber` must be a portable single path segment; traversal, rooted paths,
  mixed separators, and Windows reserved device names are rejected.
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
- Build frontend and enforce the gzip bundle budget: `cd frontend && npm run build`.
- Check the existing frontend build against the bundle budget: `cd frontend && npm run bundle:check`.
- Regenerate the committed OpenAPI snapshot and TypeScript contracts after an API
  request/response change: `cd frontend && npm run api:generate`.
- Check generated TypeScript contracts against the snapshot: `cd frontend && npm run api:check`.

Backend tests compare the live Swagger document with `src/RATools.Api/openapi.v1.json`;
the frontend check independently compares generated TypeScript with that snapshot. Both
guards must pass, so API contract changes cannot leave either artifact stale.

### Real-PostgreSQL constraint tests

`tests/RATools.Tests/Persistence/Postgres` verifies constraint semantics that only a real
PostgreSQL can enforce: the case-insensitive unique index on `ApplicationNumber`, the partial
unique index that permits one active publish job per sequence, `ON DELETE CASCADE`/`RESTRICT`
propagation, and transaction rollback. EF InMemory enforces none of these, and SQLite's
`EnsureCreated()` never creates indexes declared as raw migration SQL.

The same suite seeds 55,000 publish-job rows as a history-query performance baseline. It
requires the filtered aggregate and page query to complete within five seconds, then checks
PostgreSQL JSON execution plans for the application/readiness/time, application/status/time,
and application/sequence/time indexes.

The database is supplied externally rather than started by the test process. Point
`RATOOLS_TEST_POSTGRES` at an instance; the target database is migrated, so use a scratch one:

```powershell
$env:RATOOLS_TEST_POSTGRES = "Host=localhost;Port=5432;Database=ratools_tests;Username=postgres;Password=postgres"
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter "FullyQualifiedName~Persistence.Postgres"
```

In CI the `backend` job declares a `postgres:16` service container and sets that variable, the same
arrangement the smoke workflow uses.

Without the variable these tests report as **skipped**, which is expected on a developer machine
that has no test database. To keep that from becoming a silent green if the CI service container is
ever removed, `PostgresGateTests` fails outright when the variable is missing *and* `CI` is set.

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
- `-CorruptReportAfterPublish`: corrupt the persisted report to verify the detail endpoint returns `422` while the materialized history snapshot remains available.

The smoke test covers application and sequence creation, document upload, canonical storage, placement reassignment, validation, publish execution, persisted report retrieval, artifact listing, artifact download, publish history filters, audit logs, duplicate file-name handling, and generated backbone metadata.

Publish history list queries use validation, readiness, artifact, and lifecycle summaries
materialized on `publish_jobs`; filtering, aggregation, and pagination stay in the
repository query. Full report JSON is read only by the single-job report/detail path.

## API Examples

Publish jobs use one asynchronous command:

- `POST /api/publish-jobs/execute` enqueues background execution and returns `202 Accepted` with `PublishJobDto`;
  poll `GET /api/publish-jobs/{id}` for status and fetch `/report` and `/artifacts` once completed.
- Send a stable `Idempotency-Key` header (1-128 visible ASCII characters) when retrying the
  command. Reusing the key for the same application/sequence returns the original job; using
  it for different request data returns `409 Conflict`.
- The former synchronous `POST /api/publish-jobs` endpoint is deprecated and returns `410 Gone`; it never starts a publish.
- Publish files are isolated under `BackboneOutput:RootPath/{applicationId-no-dashes}`;
  the business-facing application number is never used as the publish storage path segment.

Use `RATools.Api.http` for local HTTP examples. New application and import requests use `ectdTemplateKey`:

```json
{
  "applicationNumber": "IND-0001",
  "ectdTemplateKey": "us-fda-ectd-3.2.2",
  "sponsorName": "Demo Sponsor",
  "workingDirectoryParentPath": "D:\\eCTD-work"
}
```
