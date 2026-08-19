#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import json
import re
import sys
import tempfile
import unittest


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "scripts"))

from validate_release_artifacts import (  # noqa: E402
    ReleaseArtifactError,
    repository_version,
    validate_sbom,
    write_checksums,
)


class ReleaseContractTests(unittest.TestCase):
    def test_repository_has_one_consistent_release_version(self) -> None:
        version = repository_version()
        directory_props = (REPO_ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        package = json.loads((REPO_ROOT / "frontend" / "package.json").read_text(encoding="utf-8"))
        lock = json.loads((REPO_ROOT / "frontend" / "package-lock.json").read_text(encoding="utf-8"))
        dockerfile = (REPO_ROOT / "Dockerfile").read_text(encoding="utf-8")
        compose = (REPO_ROOT / "compose.production.yml").read_text(encoding="utf-8")
        changelog = (REPO_ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
        api_program = (REPO_ROOT / "src" / "RATools.Api" / "Program.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("ReadAllText('$(MSBuildThisFileDirectory)VERSION').Trim()", directory_props)
        self.assertEqual(package["version"], version)
        self.assertEqual(lock["version"], version)
        self.assertEqual(lock["packages"][""]["version"], version)
        self.assertIn(f"ARG APP_VERSION={version}", dockerfile)
        self.assertEqual(dockerfile.count("org.opencontainers.image.version"), 2)
        self.assertEqual(dockerfile.count("org.opencontainers.image.revision"), 2)
        self.assertEqual(dockerfile.count("org.opencontainers.image.created"), 2)
        self.assertIn("COPY global.json Directory.Build.props VERSION ./", dockerfile)
        self.assertEqual(compose.count(f"RATOOLS_VERSION:-{version}"), 2)
        self.assertRegex(changelog, rf"## \[{re.escape(version)}\] - \d{{4}}-\d{{2}}-\d{{2}}")
        self.assertIn("AssemblyInformationalVersionAttribute", api_program)

    def test_release_workflow_generates_and_validates_both_sboms(self) -> None:
        ci = (REPO_ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        syft_config = (REPO_ROOT / ".syft.yaml").read_text(encoding="utf-8")
        runbook = (REPO_ROOT / "deploy" / "production" / "ROLLBACK.md").read_text(
            encoding="utf-8"
        )

        self.assertEqual(ci.count("anchore/sbom-action@v0.24.0"), 3)
        self.assertEqual(ci.count("syft-version: v1.50.0"), 3)
        self.assertIn("RATools-for-eCTD-migrator", ci)
        self.assertIn("scripts/validate_release_artifacts.py", ci)
        self.assertIn("actions/upload-artifact@v7", ci)
        self.assertIn("frontend/node_modules/**", syft_config)
        self.assertIn("Stateful Rollback", runbook)
        self.assertIn("automated restore drill never applies", runbook)
        self.assertIn("backup in place", runbook)

    def test_cyclonedx_validation_and_checksum_output(self) -> None:
        with tempfile.TemporaryDirectory(prefix="ratools-release-contract-") as temp_root:
            root = Path(temp_root)
            source = root / "source.cdx.json"
            image = root / "image.cdx.json"
            checksums = root / "SHA256SUMS"
            version = repository_version()
            self.write_sbom(source, "RATools-for-eCTD-source", version, "pkg:npm/react@19.2.4")
            self.write_sbom(
                image,
                "RATools-for-eCTD-image",
                version,
                "pkg:nuget/Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4",
            )

            self.assertEqual(
                validate_sbom(
                    source,
                    expected_name="RATools-for-eCTD-source",
                    expected_version=version,
                    required_purl_prefix="pkg:npm/",
                ),
                1,
            )
            write_checksums([source, image], checksums)
            checksum_lines = checksums.read_text(encoding="ascii").splitlines()
            self.assertEqual(len(checksum_lines), 2)
            self.assertTrue(all(len(line.split()[0]) == 64 for line in checksum_lines))

            with self.assertRaisesRegex(ReleaseArtifactError, "subject"):
                validate_sbom(
                    source,
                    expected_name="RATools-for-eCTD-source",
                    expected_version="9.9.9",
                    required_purl_prefix="pkg:npm/",
                )

    @staticmethod
    def write_sbom(path: Path, name: str, subject_version: str, purl: str) -> None:
        path.write_text(
            json.dumps(
                {
                    "bomFormat": "CycloneDX",
                    "specVersion": "1.6",
                    "serialNumber": "urn:uuid:00000000-0000-4000-8000-000000000001",
                    "metadata": {
                        "component": {
                            "type": "application",
                            "name": name,
                            "version": subject_version,
                        }
                    },
                    "components": [
                        {
                            "type": "library",
                            "bom-ref": purl,
                            "name": purl.split("/")[-1].split("@")[0],
                            "version": purl.rsplit("@", maxsplit=1)[1],
                            "purl": purl,
                        }
                    ],
                }
            ),
            encoding="utf-8",
        )


if __name__ == "__main__":
    unittest.main()
