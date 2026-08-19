#!/usr/bin/env python3
from pathlib import Path
import re


REPO_ROOT = Path(__file__).resolve().parents[2]


def require(source: str, pattern: str, message: str) -> None:
    if not re.search(pattern, source, re.MULTILINE | re.DOTALL):
        raise AssertionError(message)


def main() -> None:
    api_program = (REPO_ROOT / "src" / "RATools.Api" / "Program.cs").read_text(encoding="utf-8")
    migrator_program = (REPO_ROOT / "src" / "RATools.DatabaseMigrator" / "Program.cs").read_text(encoding="utf-8")
    migrator_project = (REPO_ROOT / "src" / "RATools.DatabaseMigrator" / "RATools.DatabaseMigrator.csproj").read_text(encoding="utf-8")
    api_project = (REPO_ROOT / "src" / "RATools.Api" / "RATools.Api.csproj").read_text(encoding="utf-8")
    infrastructure_project = (REPO_ROOT / "src" / "RATools.Infrastructure" / "RATools.Infrastructure.csproj").read_text(encoding="utf-8")
    test_project = (REPO_ROOT / "tests" / "RATools.Tests" / "RATools.Tests.csproj").read_text(encoding="utf-8")
    postgres_fixture = (REPO_ROOT / "tests" / "RATools.Tests" / "Persistence" / "Postgres" / "PostgresFixture.cs").read_text(encoding="utf-8")
    dockerfile = (REPO_ROOT / "Dockerfile").read_text(encoding="utf-8")
    compose = (REPO_ROOT / "compose.production.yml").read_text(encoding="utf-8")
    ci = (REPO_ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
    smoke = (REPO_ROOT / ".github" / "workflows" / "smoke.yml").read_text(encoding="utf-8")
    readme = (REPO_ROOT / "README.md").read_text(encoding="utf-8")

    assert not re.search(r"Database\.Migrate(?:Async)?\s*\(", api_program), (
        "The API process must never apply database migrations"
    )
    assert "GetPendingMigrationsAsync" in api_program, (
        "The API must fail fast when the independent migration job was skipped"
    )
    assert "Database.MigrateAsync()" in migrator_program
    assert "FileSecretConfiguration.Apply(configuration)" in migrator_program
    assert "EnsureCreated" not in migrator_program and "EnsureDeleted" not in migrator_program

    assert '<OutputType>Exe</OutputType>' in migrator_project
    assert "RATools.Infrastructure.csproj" in migrator_project
    assert "prometheus-net.AspNetCore" in api_project
    assert "prometheus-net" not in infrastructure_project, (
        "Monitoring exporters belong to the API and must not inflate the migration artifact"
    )
    assert "RATools.DatabaseMigrator.csproj" in test_project
    assert 'ReferenceOutputAssembly="false"' in test_project
    assert "GetPendingMigrationsAsync" in postgres_fixture
    assert "MigrateAsync" not in postgres_fixture

    assert "AS migrator" in dockerfile
    assert "dotnet publish src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj" in dockerfile
    assert 'ENTRYPOINT ["dotnet", "RATools.DatabaseMigrator.dll"]' in dockerfile
    assert dockerfile.rfind(" AS runtime") > dockerfile.rfind(" AS migrator"), (
        "The default final Docker target must remain the API image"
    )

    migration_block = re.search(r"^  migration:\n(.*?)(?=^  api:)", compose, re.MULTILINE | re.DOTALL)
    if migration_block is None:
        raise AssertionError("Production Compose must define an independent migration service")
    migration = migration_block.group(1)
    for expected in (
        "target: migrator",
        'network_mode: "service:proxy"',
        "condition: service_healthy",
        'restart: "no"',
        "read_only: true",
        "FileSecrets__PostgreSqlPasswordPath: /run/secrets/postgres_password",
    ):
        assert expected in migration, f"Migration service is missing: {expected}"
    assert "ApiKey" not in migration, "The migration job must not receive the browser/API credential"

    api_block = re.search(r"^  api:\n(.*?)(?=^secrets:)", compose, re.MULTILINE | re.DOTALL)
    if api_block is None:
        raise AssertionError("Production Compose API service is missing")
    require(
        api_block.group(1),
        r"depends_on:\s+migration:\s+condition: service_completed_successfully",
        "The API must wait for successful migration completion",
    )

    migrator_command = "dotnet run --project src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj"
    assert ci.count(migrator_command) == 2, "CI must prove that the migrator is idempotent"
    assert smoke.count(migrator_command) >= 1
    assert smoke.index("- name: Migrate database") < smoke.index("- name: Start API")
    assert "applies migrations automatically on startup" not in readme


if __name__ == "__main__":
    main()
