#!/usr/bin/env python3
from __future__ import annotations

from argparse import ArgumentParser
from datetime import datetime, timezone
from pathlib import Path
import os
import re
import shutil
import sys
import uuid

from production_backup_support import (
    BACKUP_SCHEMA_VERSION,
    BackupValidationError,
    CommandFailedError,
    ROW_COUNT_SQL,
    artifact_descriptor,
    parse_row_counts,
    restrict_file_permissions,
    run_text,
    stream_command_to_file,
    tar_inventory,
    write_json,
)


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_COMPOSE_FILE = REPO_ROOT / "compose.production.yml"
DEFAULT_OUTPUT_ROOT = REPO_ROOT / "deploy" / "production" / "runtime" / "backups"
BACKUP_NAME_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")
POSTGRES_IMAGE = "postgres:16.14-bookworm"


def compose_command(compose_file: Path, *arguments: str) -> list[str]:
    return ["docker", "compose", "--file", str(compose_file), *arguments]


def query_database(compose_file: Path, sql: str, *, field_separator: str | None = None) -> str:
    arguments = [
        "exec",
        "-T",
        "postgres",
        "psql",
        "--no-psqlrc",
        "--username=ratools",
        "--dbname=ratools",
        "--tuples-only",
        "--no-align",
        "--set=ON_ERROR_STOP=1",
    ]
    if field_separator is not None:
        arguments.append(f"--field-separator={field_separator}")
    arguments.extend(["--command", sql])
    return run_text(compose_command(compose_file, *arguments), cwd=REPO_ROOT).stdout.strip()


def ensure_running_services(compose_file: Path) -> None:
    run_text(["docker", "version"], cwd=REPO_ROOT)
    result = run_text(
        compose_command(compose_file, "ps", "--status", "running", "--services"),
        cwd=REPO_ROOT,
    )
    running = set(result.stdout.splitlines())
    missing = sorted({"api", "postgres"} - running)
    if missing:
        raise BackupValidationError(
            "Production backup requires running Compose services: " + ", ".join(missing)
        )


def create_backup(compose_file: Path, output_root: Path, backup_name: str) -> Path:
    if BACKUP_NAME_PATTERN.fullmatch(backup_name) is None or backup_name in {".", ".."}:
        raise BackupValidationError(
            "Backup name must be 1-64 ASCII letters, digits, dots, underscores, or hyphens"
        )
    if not compose_file.is_file():
        raise BackupValidationError(f"Compose file does not exist: {compose_file}")

    ensure_running_services(compose_file)
    output_root.mkdir(parents=True, exist_ok=True)
    if os.name != "nt":
        output_root.chmod(0o700)
    target = output_root / backup_name
    if target.exists():
        raise BackupValidationError(f"Refusing to overwrite existing backup: {target}")

    partial = output_root / f".{backup_name}.partial-{uuid.uuid4().hex}"
    partial.mkdir(mode=0o700)
    api_stopped = False
    completed = False
    failure: BaseException | None = None

    try:
        api_stopped = True
        run_text(compose_command(compose_file, "stop", "--timeout", "60", "api"), cwd=REPO_ROOT)

        migration_output = query_database(
            compose_file,
            'SELECT COUNT(*) FROM "__EFMigrationsHistory";',
        )
        if not migration_output.isdigit():
            raise BackupValidationError("Database migration-count query returned an invalid value")
        migration_count = int(migration_output)
        row_counts = parse_row_counts(query_database(compose_file, ROW_COUNT_SQL, field_separator="|"))

        database_dump = partial / "database.dump"
        stream_command_to_file(
            compose_command(
                compose_file,
                "exec",
                "-T",
                "postgres",
                "pg_dump",
                "--username=ratools",
                "--dbname=ratools",
                "--format=custom",
                "--no-owner",
                "--no-privileges",
            ),
            database_dump,
            cwd=REPO_ROOT,
        )
        restrict_file_permissions(database_dump)
        with database_dump.open("rb") as dump_file:
            dump_header = dump_file.read(5)
        if dump_header != b"PGDMP":
            raise BackupValidationError("PostgreSQL backup is not a custom-format dump")

        file_archive = partial / "files.tar.gz"
        stream_command_to_file(
            compose_command(
                compose_file,
                "--profile",
                "maintenance",
                "run",
                "--rm",
                "--no-deps",
                "-T",
                "backup",
                "-czf",
                "-",
                "-C",
                "/source",
                "app-data",
                "workspaces",
            ),
            file_archive,
            cwd=REPO_ROOT,
        )
        restrict_file_permissions(file_archive)
        inventory = tar_inventory(file_archive)
        inventory_file = partial / "files.inventory.json"
        write_json(
            inventory_file,
            {
                "schemaVersion": BACKUP_SCHEMA_VERSION,
                "files": inventory,
            },
        )

        manifest = {
            "schemaVersion": BACKUP_SCHEMA_VERSION,
            "createdUtc": datetime.now(timezone.utc).isoformat(),
            "postgresImage": POSTGRES_IMAGE,
            "database": {
                **artifact_descriptor(database_dump),
                "migrationCount": migration_count,
                "rowCounts": row_counts,
            },
            "files": {
                "archive": artifact_descriptor(file_archive),
                "inventory": artifact_descriptor(inventory_file),
                "fileCount": len(inventory),
                "totalBytes": sum(int(entry["sizeBytes"]) for entry in inventory),
            },
        }
        write_json(partial / "manifest.json", manifest)
        partial.rename(target)
        completed = True
    except BaseException as exception:
        failure = exception
    finally:
        if not completed:
            shutil.rmtree(partial, ignore_errors=True)
        if api_stopped:
            try:
                run_text(compose_command(compose_file, "start", "api"), cwd=REPO_ROOT)
            except Exception as restart_error:
                if failure is None:
                    failure = restart_error
                else:
                    failure.add_note(f"API restart also failed: {restart_error}")

    if failure is not None:
        raise failure
    return target


def main() -> None:
    parser = ArgumentParser(
        description="Create an atomic backup of the production database, application data, and workspaces."
    )
    parser.add_argument("--compose-file", type=Path, default=DEFAULT_COMPOSE_FILE)
    parser.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT_ROOT)
    parser.add_argument(
        "--backup-name",
        default=datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ"),
    )
    args = parser.parse_args()
    backup = create_backup(args.compose_file.resolve(), args.output_root.resolve(), args.backup_name)
    print(f"Production backup created: {backup}")


if __name__ == "__main__":
    try:
        main()
    except (BackupValidationError, CommandFailedError, FileExistsError, OSError) as exception:
        print(f"Backup failed: {exception}", file=sys.stderr)
        raise SystemExit(1) from None
