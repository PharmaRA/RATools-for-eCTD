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

## Useful commands

- Check PostgreSQL health: `docker ps`.
- View PostgreSQL logs: `docker logs ratools-postgres`.
- Stop database: `docker compose down`.
