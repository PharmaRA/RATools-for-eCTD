#!/usr/bin/env python3
from __future__ import annotations

from hashlib import sha256
from pathlib import Path, PurePosixPath
import json
import os
import re
import subprocess
import tarfile


BACKUP_SCHEMA_VERSION = 1
FILE_ROOTS = {"app-data", "workspaces"}
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")


class BackupValidationError(RuntimeError):
    pass


class CommandFailedError(RuntimeError):
    pass


def run_text(command: list[str], *, cwd: Path, check: bool = True) -> subprocess.CompletedProcess[str]:
    completed = subprocess.run(
        command,
        cwd=cwd,
        check=False,
        capture_output=True,
        text=True,
    )
    if check and completed.returncode != 0:
        raise CommandFailedError(command_error(command, completed.returncode, completed.stderr))
    return completed


def stream_command_to_file(command: list[str], destination: Path, *, cwd: Path) -> None:
    with destination.open("xb") as output:
        completed = subprocess.run(
            command,
            cwd=cwd,
            check=False,
            stdout=output,
            stderr=subprocess.PIPE,
        )
    if completed.returncode != 0:
        destination.unlink(missing_ok=True)
        stderr = completed.stderr.decode("utf-8", errors="replace")
        raise CommandFailedError(command_error(command, completed.returncode, stderr))


def stream_file_to_command(source: Path, command: list[str], *, cwd: Path) -> None:
    with source.open("rb") as input_file:
        completed = subprocess.run(
            command,
            cwd=cwd,
            check=False,
            stdin=input_file,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
    if completed.returncode != 0:
        stderr = completed.stderr.decode("utf-8", errors="replace")
        raise CommandFailedError(command_error(command, completed.returncode, stderr))


def command_error(command: list[str], returncode: int, stderr: str) -> str:
    executable = " ".join(command[:4])
    detail = stderr.strip() or "no error output"
    return f"Command failed with exit code {returncode} ({executable} ...): {detail}"


def sha256_file(path: Path) -> str:
    digest = sha256()
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def tar_inventory(path: Path, mode: str = "r:*") -> list[dict[str, object]]:
    try:
        with tarfile.open(path, mode) as archive:
            return _read_tar_inventory(archive)
    except (tarfile.TarError, OSError) as exception:
        raise BackupValidationError(f"Unable to inspect file archive {path.name}: {exception}") from exception


def _read_tar_inventory(archive: tarfile.TarFile) -> list[dict[str, object]]:
    inventory: list[dict[str, object]] = []
    paths: set[str] = set()
    for member in archive:
        normalized = validate_archive_member(member)
        if member.isdir():
            continue
        if normalized in paths:
            raise BackupValidationError(f"Duplicate archive member: {normalized}")
        paths.add(normalized)

        extracted = archive.extractfile(member)
        if extracted is None:
            raise BackupValidationError(f"Unable to read archive member: {normalized}")
        digest = sha256()
        while chunk := extracted.read(1024 * 1024):
            digest.update(chunk)
        inventory.append(
            {
                "path": normalized,
                "sizeBytes": member.size,
                "sha256": digest.hexdigest(),
            }
        )

    return sorted(inventory, key=lambda item: str(item["path"]))


def validate_archive_member(member: tarfile.TarInfo) -> str:
    path = PurePosixPath(member.name)
    if path.is_absolute() or ".." in path.parts or not path.parts:
        raise BackupValidationError(f"Unsafe archive path: {member.name}")
    if path.parts[0] not in FILE_ROOTS:
        raise BackupValidationError(f"Unexpected archive root: {member.name}")
    if not member.isdir() and not member.isfile():
        raise BackupValidationError(f"Archive contains a non-file entry: {member.name}")
    return str(path)


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    restrict_file_permissions(path)


def load_json(path: Path) -> dict[str, object]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise BackupValidationError(f"Unable to read {path.name}: {exception}") from exception
    if not isinstance(value, dict):
        raise BackupValidationError(f"{path.name} must contain a JSON object")
    return value


def validate_inventory(value: object) -> list[dict[str, object]]:
    if not isinstance(value, dict) or value.get("schemaVersion") != BACKUP_SCHEMA_VERSION:
        raise BackupValidationError("File inventory has an unsupported schema version")
    entries = value.get("files")
    if not isinstance(entries, list):
        raise BackupValidationError("File inventory must contain a files array")

    validated: list[dict[str, object]] = []
    seen_paths: set[str] = set()
    for entry in entries:
        if not isinstance(entry, dict):
            raise BackupValidationError("File inventory entries must be objects")
        path_value = entry.get("path")
        size = entry.get("sizeBytes")
        checksum = entry.get("sha256")
        if not isinstance(path_value, str):
            raise BackupValidationError("File inventory entry has an invalid path")
        normalized = validate_inventory_path(path_value)
        if normalized in seen_paths:
            raise BackupValidationError(f"Duplicate inventory path: {normalized}")
        if not isinstance(size, int) or isinstance(size, bool) or size < 0:
            raise BackupValidationError(f"Invalid inventory size for {normalized}")
        if not isinstance(checksum, str) or SHA256_PATTERN.fullmatch(checksum) is None:
            raise BackupValidationError(f"Invalid inventory checksum for {normalized}")
        seen_paths.add(normalized)
        validated.append(
            {
                "path": normalized,
                "sizeBytes": size,
                "sha256": checksum,
            }
        )

    return sorted(validated, key=lambda item: str(item["path"]))


def validate_inventory_path(value: str) -> str:
    path = PurePosixPath(value)
    if path.is_absolute() or ".." in path.parts or len(path.parts) < 2:
        raise BackupValidationError(f"Unsafe inventory path: {value}")
    if path.parts[0] not in FILE_ROOTS or str(path) != value:
        raise BackupValidationError(f"Unexpected inventory path: {value}")
    return str(path)


def artifact_path(backup_root: Path, filename: object) -> Path:
    if not isinstance(filename, str) or Path(filename).name != filename or filename in {".", ".."}:
        raise BackupValidationError(f"Invalid backup artifact name: {filename!r}")
    path = (backup_root / filename).resolve()
    if path.parent != backup_root.resolve():
        raise BackupValidationError(f"Backup artifact escapes its directory: {filename}")
    return path


def verify_artifact(backup_root: Path, descriptor: object) -> Path:
    if not isinstance(descriptor, dict):
        raise BackupValidationError("Backup artifact descriptor must be an object")
    path = artifact_path(backup_root, descriptor.get("file"))
    if not path.is_file():
        raise BackupValidationError(f"Backup artifact is missing: {path.name}")
    expected_size = descriptor.get("sizeBytes")
    if not isinstance(expected_size, int) or isinstance(expected_size, bool) or path.stat().st_size != expected_size:
        raise BackupValidationError(f"Backup artifact size mismatch: {path.name}")
    expected_sha256 = descriptor.get("sha256")
    if (
        not isinstance(expected_sha256, str)
        or SHA256_PATTERN.fullmatch(expected_sha256) is None
        or sha256_file(path) != expected_sha256
    ):
        raise BackupValidationError(f"Backup artifact checksum mismatch: {path.name}")
    return path


def artifact_descriptor(path: Path) -> dict[str, object]:
    return {
        "file": path.name,
        "sizeBytes": path.stat().st_size,
        "sha256": sha256_file(path),
    }


def restrict_file_permissions(path: Path) -> None:
    if os.name != "nt":
        path.chmod(0o600)


def parse_row_counts(output: str) -> dict[str, int]:
    counts: dict[str, int] = {}
    for line in output.splitlines():
        if not line.strip():
            continue
        parts = line.strip().split("|", maxsplit=1)
        if len(parts) != 2 or not parts[1].isdigit():
            raise BackupValidationError(f"Unexpected database row-count output: {line}")
        counts[parts[0]] = int(parts[1])
    if not counts:
        raise BackupValidationError("Database row-count query returned no rows")
    return counts


ROW_COUNT_SQL = """
SELECT 'applications', COUNT(*) FROM applications
UNION ALL SELECT 'audit_logs', COUNT(*) FROM audit_logs
UNION ALL SELECT 'document_placements', COUNT(*) FROM document_placements
UNION ALL SELECT 'documents', COUNT(*) FROM documents
UNION ALL SELECT 'publish_jobs', COUNT(*) FROM publish_jobs
UNION ALL SELECT 'sequences', COUNT(*) FROM sequences
ORDER BY 1;
""".strip()
