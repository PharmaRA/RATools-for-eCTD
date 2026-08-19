"""Verify the pinned EMA EU M1 source snapshot has not drifted."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SNAPSHOT = ROOT / "reference" / "eu-m1" / "3.1.1"


def test_manifest_matches_bundled_files() -> None:
    manifest_path = SNAPSHOT / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

    assert manifest["specificationVersion"] == "3.1.1"
    assert manifest["validationCriteriaVersion"] == "8.2"
    assert manifest["effective"] == "2025-12-01"
    assert manifest["assetStatus"] == "acquired-not-active"
    assert manifest["authority"] == "European Medicines Agency"

    for artifact in manifest["artifacts"]:
        path = SNAPSHOT / artifact["path"]
        assert path.is_file(), artifact["path"]
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        assert digest == artifact["sha256"], artifact["path"]

    package_path = ROOT / ".artifacts" / "eu-m1-3.1.1" / "util (4).zip"
    if package_path.is_file():
        digest = hashlib.sha256(package_path.read_bytes()).hexdigest()
        assert digest == manifest["package"]["sha256"]


def test_manifest_keeps_official_dtd_md5_evidence() -> None:
    manifest = json.loads((SNAPSHOT / "manifest.json").read_text(encoding="utf-8"))
    regional_dtd = next(item for item in manifest["artifacts"] if item["path"] == "util/dtd/eu-regional.dtd")

    assert regional_dtd["md5"] == "f8e473246d58499f9ffff8e51a32380d"
    assert regional_dtd["sourcePathInPackage"] == "util/dtd/eu-regional.dtd"


if __name__ == "__main__":
    test_manifest_matches_bundled_files()
    test_manifest_keeps_official_dtd_md5_evidence()
    print("EU M1 asset contract passed")
