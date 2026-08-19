# RATools-for-eCTD

RATools-for-eCTD is an eCTD publishing system for regulatory submission workflows. It provides a .NET backend and React frontend for managing applications, sequences, document placements, validation, publish jobs, publish artifacts, and publish history.

## Structure

- `src/RATools.Api`: HTTP API, authentication policies, Swagger, and composition root.
- `src/RATools.Application`: use cases, contracts, validation, publishing, and orchestration.
- `src/RATools.Domain`: core business entities and state transitions.
- `src/RATools.Infrastructure`: EF Core/PostgreSQL persistence, in-memory repositories, file storage, workspace policies, and local publish output.
- `src/RATools.DatabaseMigrator`: single-purpose PostgreSQL schema migration entrypoint.
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
  placeholder, not the full official EU Module 1 validation rule set. A dated official
  source snapshot is now recorded under `reference/eu-m1/3.1.1/` with the EMA source
  URLs, package digest, per-file digests, and implementation timeline. The snapshot is
  acquired but not active: the EU section dictionary and regional writer still need to
  be adapted and tested before the capability matrix can claim formal EU support.
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

Apply migrations:

```powershell
$env:ConnectionStrings__PostgreSql = "Host=localhost;Port=5432;Database=ratools;Username=postgres;Password=postgres"
dotnet run --project src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj
Remove-Item Env:ConnectionStrings__PostgreSql
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

## Production Image

The root `Dockerfile` creates API and migration targets. Node 22 builds the React
application, .NET 8 publishes both entrypoints, and the default final ASP.NET runtime
image serves the frontend from `wwwroot` on the same origin as `/api`. Both targets
run as the built-in non-root .NET user and contain no development settings, `.env`
file, or runtime `App_Data` content. At startup, `/runtime-config` supplies the
browser-visible local-only API access key from the API's runtime configuration with
`no-store`; the key is not baked into an image layer.

The root `VERSION` file is the release version source. `/version` returns the .NET
informational version, including the source revision when supplied by CI. Production
images carry matching OCI version, revision, creation time, source, and license
labels; local builds use the same version with `local` revision metadata.

```powershell
docker build --pull --tag ratools:local .
```

The standalone image listens on container port `8080`. `Deployment:Containerized=true`
only allows the required wildcard listener inside the container; the local-only
support boundary still requires published ports and the database to remain local to
the controlled host. Prefer the production Compose topology below for an actual run.

## Local Production Deployment

`compose.production.yml` runs the application behind Caddy with local HTTPS. The API,
PostgreSQL, and Prometheus share the proxy container's network namespace and listen
only on its loopback interface; Docker publishes only Caddy on host loopback ports 80
and 443. No API, database, or monitoring port joins a separately routable bridge
network.

Prerequisites: Docker Compose, Python 3, and free host ports 80/443. Initialize the
file-backed secrets once, validate the configuration, then start the stack:

```powershell
python scripts/initialize_production.py
docker compose -f compose.production.yml config --quiet
docker compose -f compose.production.yml up --detach --build
docker compose -f compose.production.yml ps
```

Compose runs the single-purpose `migration` container after PostgreSQL becomes
healthy. The API starts only after that container exits successfully. The migrator is
safe to run repeatedly; a current database produces a successful no-op. The API never
changes the schema and fails fast with the pending migration IDs if this step is
skipped.

The initializer writes independent random API and PostgreSQL credentials under the
ignored `deploy/production/runtime/secrets` directory. It refuses to overwrite any
existing secret: changing the PostgreSQL secret file alone does not rotate the
password stored by PostgreSQL. Compose mounts both files read-only under
`/run/secrets`; secret values do not appear in the Compose environment or image.

Caddy issues the `localhost` certificate from a persistent internal CA. Trust that CA
only after confirming it came from this local stack. On Windows, export and trust it
for the current user:

```powershell
New-Item -ItemType Directory -Force .artifacts/certs | Out-Null
docker compose -f compose.production.yml cp proxy:/data/caddy/pki/authorities/local/root.crt .artifacts/certs/ratools-local-root.crt
Import-Certificate -FilePath .artifacts/certs/ratools-local-root.crt -CertStoreLocation Cert:\CurrentUser\Root
```

Open `https://localhost`. HTTP redirects to HTTPS. Caddy removes its server header and
sets HSTS, CSP, clickjacking, MIME-sniffing, referrer, permissions, and cross-origin
isolation headers. The services use read-only root filesystems where compatible;
recursive workspace purge remains disabled.

Persistent state is kept in the `ratools_postgres_data`, `ratools_app_data`,
`ratools_workspaces`, `ratools_caddy_data`, `ratools_caddy_config`, and
`ratools_prometheus_data` named volumes. Normal shutdown retains them:

```powershell
docker compose -f compose.production.yml down
```

Do not add `--volumes` to that command unless permanent deletion of the database,
workspaces, publish artifacts, local CA, and retained metrics is intended. Backup and
restore operations assume this single-process topology; it does not support multiple
replicas.

## Backup and Restore Drill

Create a consistent backup while the production stack is running:

```powershell
python scripts/backup_production.py
```

The command verifies that the API and PostgreSQL are running, stops the API for a
short consistency window, writes a PostgreSQL custom-format dump, archives
`App_Data` and the workspace volume, then restarts the API. A manifest records SHA-256
digests, migration count, core-table row counts, and a per-file inventory. A failed
run removes its partial directory; an existing backup name is never overwritten.

Backups default to the ignored `deploy/production/runtime/backups/<UTC timestamp>`
directory. They contain database data, uploaded source documents, publish artifacts,
and workspace content. Treat the whole directory as regulated sensitive data: limit
filesystem access, encrypt any off-host copy, and apply an organization-approved
retention policy. Windows permissions are inherited from the destination directory;
Unix output is restricted to the current operator.

The backup intentionally excludes runtime secret files, the Caddy private CA, and
Prometheus history. Store credentials through a separate approved secret-recovery
process; a rebuilt host receives a new local CA and monitoring history unless those
operational assets are protected independently.

Prove that a backup is usable without changing production state:

```powershell
python scripts/restore_production_backup.py deploy/production/runtime/backups/<UTC timestamp>
```

The drill rejects checksum, manifest, unsafe tar-path, symlink, and inventory
mismatches before restoration. It then restores the database and files into randomly
named temporary Docker volumes, runs the current migration image against the restored
database, verifies migrations, row counts, and every file digest, and removes the
temporary container and volumes. It never mounts or modifies the production volumes.
Run the drill after every scheduled backup and before relying on that backup for
disaster recovery; CI executes the same workflow against seeded data on every change.

## Release Evidence and Rollback

Every production-image CI run generates three CycloneDX JSON SBOMs with pinned Syft:
one scans source dependency manifests (including npm), while the other two scan the
final API and migration images (including NuGet and operating-system runtime
packages). CI rejects an SBOM
whose subject version, format, component references, or required ecosystem coverage
is invalid. The uploaded release-evidence artifact contains both SBOMs, `SHA256SUMS`,
image IDs, source revision, build timestamp, `VERSION`, and `CHANGELOG.md`.

Before changing a release version, update `VERSION`, the frontend package metadata,
Docker/Compose defaults, and `CHANGELOG.md`; `scripts/tests/test_release_contract.py`
fails when those values drift. Create the signed `v<version>` Git tag only after all
CI jobs pass for the exact commit.

Follow [`deploy/production/ROLLBACK.md`](deploy/production/ROLLBACK.md) before every
upgrade and during recovery. It requires a verified pre-upgrade backup and retained
image/SBOM evidence, distinguishes schema-compatible image rollback from stateful
recovery, and never overwrites the failed production volumes.

## Health, Metrics, and Alerts

The API exposes dependency-free liveness at `/health/live`, database-aware readiness
at `/health/ready`, and Prometheus metrics at `/metrics`. Caddy uses readiness for its
upstream health check. The browser-facing proxy deliberately returns 404 for
`/metrics`; only the bundled Prometheus process can scrape it over the shared loopback
network.

Production Compose retains Prometheus samples for 15 days. In addition to bounded HTTP
request metrics, the application publishes durable pending queue depth, queue-sample
health, publish attempt count/duration, end-to-end terminal job duration, and terminal
success/failure count. Metrics use fixed outcome labels and never include job,
application, or sequence identifiers.

`deploy/production/alerts.yml` evaluates these conditions:

- API metrics target unavailable for 2 minutes.
- More than 10 pending jobs for 10 minutes, or queue sampling failing for 2 minutes.
- End-to-end publish job P95 above 5 minutes for 10 minutes.
- At least 5 terminal jobs in 15 minutes with a failure rate above 20% for 5 minutes.

Inspect current rule state without exposing a monitoring port:

```powershell
docker compose -f compose.production.yml exec prometheus promtool query instant http://127.0.0.1:9090 'ALERTS{alertstate="firing"}'
docker compose -f compose.production.yml logs prometheus
```

Prometheus evaluates and persists alert state, but this local-only profile does not
guess an email, chat, or paging destination. Configure an Alertmanager receiver before
relying on notifications outside the host. Non-development API logs remain one-line
structured JSON for collection by the container runtime or host log agent.

## Supported Deployment Boundary

The current release supports one trusted operator, one API/worker process, and a
browser, API, PostgreSQL database, and workspaces on the same controlled host. Keep
the API and database bound to loopback. Only the bundled host-loopback Caddy topology
is supported; do not expose this build through a LAN, public address, shared reverse
proxy, or multiple replicas.

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

The development `docker-compose.yml` binds PostgreSQL to `127.0.0.1`, but its tracked
credentials remain a development convenience. Use `compose.production.yml` for the
file-secret, TLS, security-header, and persistent-volume controls described above.

## Key Configuration

- `Deployment:Mode`: fixed to `LocalOnly`; any other value is rejected because shared
  deployment is not implemented.
- `Deployment:Containerized`: permits wildcard container listeners when explicitly set;
  the production Compose topology overrides the API listener back to container-loopback.
- `Deployment:InstanceLockPath` (default `App_Data/ratools-api.lock`): cross-process lock
  held for the lifetime of a relational API/worker process. Put all replicas of one local
  installation on the same lock path; only one may run.
- `Urls`: configured values must contain only HTTP(S) loopback listeners. Configured
  `Kestrel:Endpoints:*:Url` values are checked by the same rule.
- `Security:ApiKey`: required for non-InMemory providers; outside `Development`, it must
  be a non-development value of at least 32 characters.
- `FileSecrets:ApiKeyPath`: optional absolute path whose non-empty file content overrides
  `Security:ApiKey` before authentication is configured.
- `FileSecrets:PostgreSqlPasswordPath`: optional absolute path whose non-empty file content
  is injected into `ConnectionStrings:PostgreSql` before persistence is configured.
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
- `Monitoring:QueueSampleInterval` (default `00:00:15`): interval for reading the durable
  Pending count into `ratools_publish_queue_depth`; sampling failures set a separate
  health gauge and emit a structured warning without stopping the worker.

For a non-development local run, override both tracked development credentials and keep
all endpoints on loopback, for example with `ASPNETCORE_URLS`, `Security__ApiKey`, and
`ConnectionStrings__PostgreSql` environment variables. Startup validates these values
before acquiring the instance lock or checking that the independent migrator left no
pending schema changes.

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
$env:ConnectionStrings__PostgreSql = "Host=localhost;Port=5432;Database=ratools;Username=postgres;Password=postgres"
dotnet run --project src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj --configuration Release
Remove-Item Env:ConnectionStrings__PostgreSql
```

Production Compose supplies the connection string and password file to the same
migrator entrypoint automatically. The API checks for pending migrations but never
applies them. A migration failure therefore stops deployment before the API starts.

## Useful Commands

- Check PostgreSQL health: `docker ps`.
- View PostgreSQL logs: `docker logs ratools-postgres`.
- Stop database: `docker compose down`.
- Apply pending database migrations: `dotnet run --project src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj --configuration Release`.
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
both the migrator and `RATOOLS_TEST_POSTGRES` at a scratch database:

```powershell
$testDatabase = "Host=localhost;Port=5432;Database=ratools_tests;Username=postgres;Password=postgres"
$env:ConnectionStrings__PostgreSql = $testDatabase
$env:RATOOLS_TEST_POSTGRES = $testDatabase
dotnet run --project src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj --configuration Release
Remove-Item Env:ConnectionStrings__PostgreSql
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter "FullyQualifiedName~Persistence.Postgres"
```

In CI the `backend` job declares a `postgres:16` service container, runs the migrator
twice to prove idempotency, and then sets that variable for the tests. The smoke
workflow also migrates its database before starting the API.

Without the variable these tests report as **skipped**, which is expected on a developer machine
that has no test database. To keep that from becoming a silent green if the CI service container is
ever removed, `PostgresGateTests` fails outright when the variable is missing *and* `CI` is set.

## Smoke Test

Migrate the database as described above, then start the API:

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
