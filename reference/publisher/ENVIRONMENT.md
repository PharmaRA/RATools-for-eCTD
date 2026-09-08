# Publisher verification environments (G0-04)

Use .NET 8 (the repository SDK policy is `global.json`), Python 3.10 or newer,
and PostgreSQL 16. The repository-local EF tool is pinned to 8.0.8 to match EF
dependencies. Restore it with `dotnet tool restore`; an installation in an
ignored personal directory is not a CI prerequisite.

## Reusable gate

Run from the repository root, after restoring backend dependencies:

```powershell
dotnet restore tests/RATools.Tests/RATools.Tests.csproj
dotnet tool restore
py -3.14 -X utf8 scripts/verify_publisher_environment.py --output .artifacts/publisher-check-01 --require-postgres
dotnet ef migrations has-pending-model-changes --project src/RATools.Infrastructure/RATools.Infrastructure.csproj --startup-project src/RATools.Api/RATools.Api.csproj --configuration Release --no-build
```

On Linux use `python3` in place of `py -3.14 -X utf8`. On Windows without the
Python launcher, a real `python3` executable also works (3.12.10 was verified on
8 September 2026); avoid an unrelated application's bundled `python.exe`.
`--output` must name a
new directory. The script builds Release, runs the path-security matrix, runs
the independent migrator twice and then runs the PostgreSQL test collection.
It records commands, output hashes, actual TRX counts and a scope-specific JSON
receipt. Missing tests, skipped tests and unexpected test outcomes cannot yield
a passing database gate. An unconfigured database is `NotEvaluated`; with
`--require-postgres` this also returns exit code 2. Execution failures return 1.

Set `RATOOLS_TEST_POSTGRES` in the invoking process to a **disposable test
database** connection string. The script passes this same connection to
`ConnectionStrings__PostgreSql` for the migrator. It does not create, delete or
guess a database, and must not be pointed at a live application database. It
does apply all pending migrations and run data-writing constraint tests.

For a Docker development host, start a dedicated test database separately:

```text
docker run --name ratools-publisher-test --detach --publish 127.0.0.1:55432:5432 --env POSTGRES_DB=ratools_publisher_tests --env POSTGRES_USER=ratools_tests --env POSTGRES_PASSWORD=local-test-only postgres:16
```

Its local-only example connection string is
`Host=127.0.0.1;Port=55432;Database=ratools_publisher_tests;Username=ratools_tests;Password=local-test-only`.
Use a different port if occupied. Dispose of only this named test container
after testing. Existing workspaces and production database volumes are not used.

For Windows without Docker, PostgreSQL's EDB binary distribution also works.
Extract `pgsql/bin`, `pgsql/lib` and `pgsql/share` into an ignored tools directory.
Create a new cluster with `initdb`, SCRAM authentication, UTF-8 and a password
file; bind `listen_addresses` to `127.0.0.1` and choose an unused port. Start with
`pg_ctl -D <test-data> -l <test-log> -w start`, then create a disposable database
with `createdb`. Preserve credentials only in local ignored files. Use `pg_ctl
status` to inspect the actual cluster and `pg_ctl -D <test-data> -m fast -w stop`
to stop it after testing. In PowerShell, `Start-Process -Wait` may wait for the
spawned server's process tree; use the returned process's bounded `WaitForExit`
when launching pg_ctl as a hidden helper.

## Evidence and remaining gates

The Windows run on 6 September 2026 used SDK 8.0.423, Python 3.14.7 and PostgreSQL
16.14 in a new task-owned cluster. It applied 13 migrations to the empty database;
the second migrator invocation reported no pending migrations. The 12 PostgreSQL
collection tests and 105 path tests passed with no skips. EF reported no pending
model changes. `environment-evidence.json` records hashes of the retained local
receipt and TRX; the receipt in turn hashes all command logs.

The earlier whole-backend run at G0-02 passed 600 tests while skipping 11
PostgreSQL-dependent tests. The later database run supplies those 11 tests plus
the CI configuration guard. These are two explicitly scoped runs, not a claim
that a single 611-test run was executed.

The portable PostgreSQL package came from:
`https://get.enterprisedb.com/postgresql/postgresql-16.14-1-windows-x64-binaries.zip`.
Its downloaded SHA-256 is recorded in the evidence. This is provenance for the
local test tool, not an independently signed release attestation. The archive,
cluster and credentials remain outside Git in `.artifacts/`.

`.github/workflows/ci.yml` supplies a real PostgreSQL service on Ubuntu, restores
the pinned EF tool, checks model drift, runs migrations twice and runs the full
backend suite with `RATOOLS_TEST_POSTGRES`. Its separate path-security job runs
on both `ubuntu-latest` and `windows-latest`. These are executable entry points.
No Linux run for this work was observed locally; WSL is not installed and Docker
is unavailable here. Linux execution remains **NotEvaluated** until its actual
CI result and artifacts are retained. A configured workflow alone is not a
passing receipt.

P1-03 and later database tasks must add their own empty/legacy-data upgrades,
idempotent backfills and rollback evidence. This baseline does not prequalify
future migrations. API startup continues to check migration state only; all
schema changes run through `RATools.DatabaseMigrator`.
