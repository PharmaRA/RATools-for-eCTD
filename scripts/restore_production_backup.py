#!/usr/bin/env python3
from __future__ import annotations

from argparse import ArgumentParser
from datetime import datetime
from pathlib import Path
from tempfile import TemporaryDirectory
from typing import Any
import secrets
import sys
import time
import uuid

from production_backup_support import (
    BACKUP_SCHEMA_VERSION,
    BackupValidationError,
    CommandFailedError,
    ROW_COUNT_SQL,
    load_json,
    parse_row_counts,
    run_text,
    stream_command_to_file,
    stream_file_to_command,
    tar_inventory,
    validate_inventory,
    verify_artifact,
)


REPO_ROOT = Path(__file__).resolve().parents[1]
POSTGRES_IMAGE = "postgres:16.14-bookworm"
ARCHIVE_IMAGE = "alpine:3.22.5"
MIGRATOR_IMAGE = "ratools-migrator:local"
EXPECTED_TABLES = {
    "applications",
    "audit_logs",
    "document_placements",
    "documents",
    "publish_jobs",
    "sequences",
}


def required_object(value: object, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise BackupValidationError(f"{label} must be an object")
    return value


def required_non_negative_int(value: object, label: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        raise BackupValidationError(f"{label} must be a non-negative integer")
    return value


def validate_backup(backup_root: Path) -> dict[str, object]:
    if not backup_root.is_dir():
        raise BackupValidationError(f"Backup directory does not exist: {backup_root}")
    manifest = load_json(backup_root / "manifest.json")
    if manifest.get("schemaVersion") != BACKUP_SCHEMA_VERSION:
        raise BackupValidationError("Backup manifest has an unsupported schema version")
    created_utc = manifest.get("createdUtc")
    if not isinstance(created_utc, str):
        raise BackupValidationError("Backup manifest has no creation timestamp")
    try:
        parsed_created_utc = datetime.fromisoformat(created_utc)
    except ValueError as exception:
        raise BackupValidationError("Backup manifest has an invalid creation timestamp") from exception
    if parsed_created_utc.tzinfo is None:
        raise BackupValidationError("Backup manifest creation timestamp must include a UTC offset")
    if not isinstance(manifest.get("postgresImage"), str) or not manifest["postgresImage"]:
        raise BackupValidationError("Backup manifest has no PostgreSQL image identifier")

    database = required_object(manifest.get("database"), "database")
    database_dump = verify_artifact(backup_root, database)
    with database_dump.open("rb") as dump_file:
        if dump_file.read(5) != b"PGDMP":
            raise BackupValidationError("Database artifact is not a PostgreSQL custom-format dump")
    migration_count = required_non_negative_int(database.get("migrationCount"), "migrationCount")
    raw_row_counts = required_object(database.get("rowCounts"), "rowCounts")
    if set(raw_row_counts) != EXPECTED_TABLES:
        raise BackupValidationError("Database row counts do not contain the expected table set")
    row_counts = {
        table: required_non_negative_int(raw_row_counts[table], f"rowCounts.{table}")
        for table in sorted(EXPECTED_TABLES)
    }

    files = required_object(manifest.get("files"), "files")
    archive = verify_artifact(backup_root, files.get("archive"))
    inventory_path = verify_artifact(backup_root, files.get("inventory"))
    inventory = validate_inventory(load_json(inventory_path))
    archive_inventory = tar_inventory(archive)
    if archive_inventory != inventory:
        raise BackupValidationError("File archive contents do not match the recorded inventory")
    if required_non_negative_int(files.get("fileCount"), "fileCount") != len(inventory):
        raise BackupValidationError("File count does not match the recorded inventory")
    total_bytes = sum(int(entry["sizeBytes"]) for entry in inventory)
    if required_non_negative_int(files.get("totalBytes"), "totalBytes") != total_bytes:
        raise BackupValidationError("File byte count does not match the recorded inventory")

    return {
        "databaseDump": database_dump,
        "fileArchive": archive,
        "inventory": inventory,
        "migrationCount": migration_count,
        "rowCounts": row_counts,
    }


def wait_for_postgres(container_name: str) -> None:
    for _ in range(60):
        result = run_text(
            [
                "docker",
                "exec",
                container_name,
                "pg_isready",
                "--username=ratools",
                "--dbname=ratools_restore",
            ],
            cwd=REPO_ROOT,
            check=False,
        )
        if result.returncode == 0:
            return
        time.sleep(1)
    logs = run_text(["docker", "logs", container_name], cwd=REPO_ROOT, check=False)
    detail = logs.stderr.strip() or logs.stdout.strip() or "no PostgreSQL logs"
    raise CommandFailedError(f"Temporary PostgreSQL did not become ready: {detail}")


def query_restored_database(container_name: str, sql: str, *, field_separator: str | None = None) -> str:
    command = [
        "docker",
        "exec",
        container_name,
        "psql",
        "--no-psqlrc",
        "--username=ratools",
        "--dbname=ratools_restore",
        "--tuples-only",
        "--no-align",
        "--set=ON_ERROR_STOP=1",
    ]
    if field_separator is not None:
        command.append(f"--field-separator={field_separator}")
    command.extend(["--command", sql])
    return run_text(command, cwd=REPO_ROOT).stdout.strip()


def assert_database_state(container_name: str, expected: dict[str, object]) -> None:
    migration_output = query_restored_database(
        container_name,
        'SELECT COUNT(*) FROM "__EFMigrationsHistory";',
    )
    if not migration_output.isdigit() or int(migration_output) != expected["migrationCount"]:
        raise BackupValidationError("Restored migration count does not match the backup manifest")
    restored_counts = parse_row_counts(
        query_restored_database(container_name, ROW_COUNT_SQL, field_separator="|")
    )
    if restored_counts != expected["rowCounts"]:
        raise BackupValidationError("Restored database row counts do not match the backup manifest")


def run_restore_drill(
    backup_root: Path,
    *,
    postgres_image: str,
    archive_image: str,
    migrator_image: str,
) -> None:
    expected = validate_backup(backup_root)
    run_text(["docker", "version"], cwd=REPO_ROOT)

    suffix = uuid.uuid4().hex
    postgres_container = f"ratools-restore-postgres-{suffix}"
    postgres_volume = f"ratools-restore-postgres-{suffix}"
    files_volume = f"ratools-restore-files-{suffix}"
    password = secrets.token_urlsafe(32)
    created_volumes: list[str] = []
    postgres_started = False
    failure: BaseException | None = None

    try:
        for volume_name in (postgres_volume, files_volume):
            run_text(
                [
                    "docker",
                    "volume",
                    "create",
                    "--label",
                    "io.ratools.restore-drill=true",
                    volume_name,
                ],
                cwd=REPO_ROOT,
            )
            created_volumes.append(volume_name)

        run_text(
            [
                "docker",
                "run",
                "--detach",
                "--name",
                postgres_container,
                "--label",
                "io.ratools.restore-drill=true",
                "--network",
                "none",
                "--read-only",
                "--security-opt",
                "no-new-privileges:true",
                "--tmpfs",
                "/tmp:size=64m,mode=1777",
                "--tmpfs",
                "/var/run/postgresql:size=16m,mode=3775,uid=999,gid=999",
                "--volume",
                f"{postgres_volume}:/var/lib/postgresql/data",
                "--env",
                "POSTGRES_DB=ratools_restore",
                "--env",
                "POSTGRES_USER=ratools",
                "--env",
                f"POSTGRES_PASSWORD={password}",
                postgres_image,
            ],
            cwd=REPO_ROOT,
        )
        postgres_started = True
        wait_for_postgres(postgres_container)

        stream_file_to_command(
            expected["databaseDump"],
            [
                "docker",
                "exec",
                "--interactive",
                postgres_container,
                "pg_restore",
                "--exit-on-error",
                "--no-owner",
                "--no-privileges",
                "--username=ratools",
                "--dbname=ratools_restore",
            ],
            cwd=REPO_ROOT,
        )
        assert_database_state(postgres_container, expected)

        run_text(
            [
                "docker",
                "run",
                "--rm",
                "--network",
                f"container:{postgres_container}",
                "--read-only",
                "--cap-drop",
                "ALL",
                "--security-opt",
                "no-new-privileges:true",
                "--tmpfs",
                "/tmp:size=64m,mode=1777",
                "--env",
                (
                    "ConnectionStrings__PostgreSql=Host=127.0.0.1;Port=5432;"
                    f"Database=ratools_restore;Username=ratools;Password={password}"
                ),
                migrator_image,
            ],
            cwd=REPO_ROOT,
        )
        post_migration_counts = parse_row_counts(
            query_restored_database(postgres_container, ROW_COUNT_SQL, field_separator="|")
        )
        if post_migration_counts != expected["rowCounts"]:
            raise BackupValidationError("Current migrations changed restored business row counts")

        stream_file_to_command(
            expected["fileArchive"],
            [
                "docker",
                "run",
                "--rm",
                "--interactive",
                "--network",
                "none",
                "--read-only",
                "--cap-drop",
                "ALL",
                "--security-opt",
                "no-new-privileges:true",
                "--volume",
                f"{files_volume}:/restore",
                archive_image,
                "tar",
                "-xzf",
                "-",
                "-C",
                "/restore",
            ],
            cwd=REPO_ROOT,
        )
        with TemporaryDirectory(prefix="ratools-restore-verify-") as temp_root:
            restored_archive = Path(temp_root) / "restored.tar"
            stream_command_to_file(
                [
                    "docker",
                    "run",
                    "--rm",
                    "--network",
                    "none",
                    "--read-only",
                    "--cap-drop",
                    "ALL",
                    "--security-opt",
                    "no-new-privileges:true",
                    "--volume",
                    f"{files_volume}:/source:ro",
                    archive_image,
                    "tar",
                    "-cf",
                    "-",
                    "-C",
                    "/source",
                    "app-data",
                    "workspaces",
                ],
                restored_archive,
                cwd=REPO_ROOT,
            )
            if tar_inventory(restored_archive, mode="r:") != expected["inventory"]:
                raise BackupValidationError("Restored files do not match the backup inventory")
    except BaseException as exception:
        failure = exception
    finally:
        if postgres_started:
            run_text(["docker", "rm", "--force", postgres_container], cwd=REPO_ROOT, check=False)
        for volume_name in reversed(created_volumes):
            cleanup = run_text(
                ["docker", "volume", "rm", "--force", volume_name],
                cwd=REPO_ROOT,
                check=False,
            )
            if cleanup.returncode != 0:
                cleanup_error = CommandFailedError(
                    f"Unable to remove temporary restore volume {volume_name}: {cleanup.stderr.strip()}"
                )
                if failure is None:
                    failure = cleanup_error
                else:
                    failure.add_note(str(cleanup_error))

    if failure is not None:
        raise failure


def main() -> None:
    parser = ArgumentParser(
        description="Validate a production backup by restoring it into isolated temporary Docker resources."
    )
    parser.add_argument("backup", type=Path)
    parser.add_argument("--postgres-image", default=POSTGRES_IMAGE)
    parser.add_argument("--archive-image", default=ARCHIVE_IMAGE)
    parser.add_argument("--migrator-image", default=MIGRATOR_IMAGE)
    args = parser.parse_args()

    backup_root = args.backup.resolve()
    run_restore_drill(
        backup_root,
        postgres_image=args.postgres_image,
        archive_image=args.archive_image,
        migrator_image=args.migrator_image,
    )
    print(f"Backup restore drill passed: {backup_root}")


if __name__ == "__main__":
    try:
        main()
    except (BackupValidationError, CommandFailedError, OSError) as exception:
        print(f"Restore drill failed: {exception}", file=sys.stderr)
        raise SystemExit(1) from None
