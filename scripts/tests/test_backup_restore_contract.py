#!/usr/bin/env python3
from __future__ import annotations

from io import BytesIO
from pathlib import Path
import json
import sys
import tarfile
import tempfile
import unittest


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "scripts"))

from production_backup_support import (  # noqa: E402
    BACKUP_SCHEMA_VERSION,
    BackupValidationError,
    artifact_descriptor,
    tar_inventory,
    write_json,
)
from restore_production_backup import validate_backup  # noqa: E402


def add_bytes(archive: tarfile.TarFile, name: str, value: bytes) -> None:
    member = tarfile.TarInfo(name)
    member.size = len(value)
    member.mode = 0o600
    archive.addfile(member, BytesIO(value))


class BackupRestoreContractTests(unittest.TestCase):
    def test_valid_backup_manifest_and_inventory_are_accepted(self) -> None:
        with tempfile.TemporaryDirectory(prefix="ratools-backup-contract-") as temp_root:
            backup_root = Path(temp_root)
            self.create_valid_backup(backup_root)

            validated = validate_backup(backup_root)

            self.assertEqual(validated["migrationCount"], 12)
            self.assertEqual(validated["rowCounts"]["audit_logs"], 1)
            self.assertEqual(len(validated["inventory"]), 2)

    def test_artifact_tampering_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory(prefix="ratools-backup-contract-") as temp_root:
            backup_root = Path(temp_root)
            self.create_valid_backup(backup_root)
            with (backup_root / "database.dump").open("ab") as dump_file:
                dump_file.write(b"tampered")

            with self.assertRaisesRegex(BackupValidationError, "size mismatch"):
                validate_backup(backup_root)

    def test_archive_traversal_and_non_file_entries_are_rejected(self) -> None:
        unsafe_members = ("../escape.txt", "/absolute.txt")
        for member_name in unsafe_members:
            with self.subTest(member=member_name), tempfile.TemporaryDirectory(
                prefix="ratools-backup-contract-"
            ) as temp_root:
                archive_path = Path(temp_root) / "unsafe.tar.gz"
                with tarfile.open(archive_path, "w:gz") as archive:
                    add_bytes(archive, member_name, b"unsafe")
                with self.assertRaisesRegex(BackupValidationError, "Unsafe archive path"):
                    tar_inventory(archive_path)

        with tempfile.TemporaryDirectory(prefix="ratools-backup-contract-") as temp_root:
            archive_path = Path(temp_root) / "symlink.tar.gz"
            with tarfile.open(archive_path, "w:gz") as archive:
                member = tarfile.TarInfo("app-data/link")
                member.type = tarfile.SYMTYPE
                member.linkname = "../../escape.txt"
                archive.addfile(member)
            with self.assertRaisesRegex(BackupValidationError, "non-file entry"):
                tar_inventory(archive_path)

    def test_implementation_keeps_restore_resources_isolated(self) -> None:
        backup_script = (REPO_ROOT / "scripts" / "backup_production.py").read_text(encoding="utf-8")
        restore_script = (REPO_ROOT / "scripts" / "restore_production_backup.py").read_text(
            encoding="utf-8"
        )
        ci = (REPO_ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        readme = (REPO_ROOT / "README.md").read_text(encoding="utf-8")

        self.assertIn('"stop", "--timeout", "60", "api"', backup_script)
        self.assertIn('"--format=custom"', backup_script)
        self.assertIn('"start", "api"', backup_script)
        self.assertIn('POSTGRES_IMAGE = "postgres:16.14-bookworm"', backup_script)
        self.assertIn('POSTGRES_IMAGE = "postgres:16.14-bookworm"', restore_script)
        self.assertIn('postgres_container = f"ratools-restore-postgres-{suffix}"', restore_script)
        self.assertIn('"--network",\n                "none"', restore_script)
        self.assertIn('["docker", "volume", "rm", "--force", volume_name]', restore_script)
        self.assertNotIn("ratools_postgres_data", restore_script)
        self.assertNotIn("ratools_app_data", restore_script)
        self.assertIn("python3 scripts/restore_production_backup.py", ci)
        self.assertIn("python scripts/restore_production_backup.py", readme)

    @staticmethod
    def create_valid_backup(backup_root: Path) -> None:
        database_dump = backup_root / "database.dump"
        database_dump.write_bytes(b"PGDMP-test-dump")

        archive_path = backup_root / "files.tar.gz"
        with tarfile.open(archive_path, "w:gz") as archive:
            add_bytes(archive, "app-data/publish/package.zip", b"package")
            add_bytes(archive, "workspaces/example/source.pdf", b"document")
        inventory = tar_inventory(archive_path)

        inventory_path = backup_root / "files.inventory.json"
        write_json(
            inventory_path,
            {
                "schemaVersion": BACKUP_SCHEMA_VERSION,
                "files": inventory,
            },
        )
        row_counts = {
            "applications": 0,
            "audit_logs": 1,
            "document_placements": 0,
            "documents": 0,
            "publish_jobs": 0,
            "sequences": 0,
        }
        manifest = {
            "schemaVersion": BACKUP_SCHEMA_VERSION,
            "createdUtc": "2026-08-19T00:00:00+00:00",
            "postgresImage": "postgres:16.14-bookworm",
            "database": {
                **artifact_descriptor(database_dump),
                "migrationCount": 12,
                "rowCounts": row_counts,
            },
            "files": {
                "archive": artifact_descriptor(archive_path),
                "inventory": artifact_descriptor(inventory_path),
                "fileCount": len(inventory),
                "totalBytes": sum(int(entry["sizeBytes"]) for entry in inventory),
            },
        }
        (backup_root / "manifest.json").write_text(
            json.dumps(manifest, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )


if __name__ == "__main__":
    unittest.main()
