"""Copy frozen publisher inputs and inject explicit, reviewable defects."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import warnings
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "tests/RATools.Tests/Fixtures/Publisher"


def local_path(root: Path, relative: str) -> Path:
    path = PurePosixPath(relative)
    if path.is_absolute() or ".." in path.parts or "\\" in relative or ":" in relative:
        raise ValueError(f"Unsafe fixture file path: {relative}")
    resolved = (root / relative).resolve()
    if not resolved.is_relative_to(root.resolve()):
        raise ValueError(f"Fixture path leaves its root: {relative}")
    return resolved


def refresh_backbone_checksums(root: Path) -> None:
    # Do not repair PDF checksums: deliberate payload corruption must remain visible.
    for sequence in sorted(root.iterdir()):
        index = sequence / "index.xml"
        if not index.is_file():
            continue
        data = index.read_bytes()
        document = ET.fromstring(data)
        for leaf in document.iter("leaf"):
            href = leaf.get("{http://www.w3c.org/1999/xlink}href", "")
            if href == "m1/us/us-regional.xml":
                old = leaf.get("checksum", "")
                new = hashlib.md5(local_path(sequence, href).read_bytes()).hexdigest()
                data = data.replace(f'checksum="{old}"'.encode(), f'checksum="{new}"'.encode())
        index.write_bytes(data)
        (sequence / "index-md5.txt").write_bytes(hashlib.md5(data).hexdigest().encode("ascii"))


def write_zip(root: Path, sequence: str, target: Path, mutation: dict | None = None) -> None:
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(local_path(root, sequence).rglob("*")):
            if path.is_file():
                name = path.relative_to(root).as_posix()
                data = path.read_bytes()
                if mutation and mutation["kind"] == "replace" and mutation["path"] == name:
                    data = local_path(root, mutation["source"]).read_bytes()
                archive.writestr(name, data)
        if mutation and mutation["kind"] == "add":
            # Malicious ZIP entry names are intentionally stored, never extracted here.
            with warnings.catch_warnings():
                warnings.filterwarnings("ignore", message="Duplicate name:.*", category=UserWarning)
                archive.writestr(mutation["path"], mutation["text"].encode("utf-8"))


def inventory(root: Path) -> list[dict]:
    return [
        {"path": path.relative_to(root).as_posix(), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        for path in sorted(root.rglob("*")) if path.is_file()
    ]


def materialize(output: Path) -> None:
    output = output.resolve()
    if output.exists() or output.is_relative_to(FIXTURE.resolve()):
        raise ValueError("The output must be a new directory outside the frozen fixture")
    cases = json.loads((FIXTURE / "invalid-cases.json").read_text(encoding="utf-8"))["cases"]
    baseline = output / "baseline"
    shutil.copytree(FIXTURE / "sequences", baseline)
    for sequence in ("0000", "0001", "0002", "0003"):
        write_zip(baseline, sequence, output / f"{sequence}.zip")

    report = []
    for case in cases:
        case_root = local_path(output / "invalid", case["id"])
        application = case_root / "application"
        shutil.copytree(FIXTURE / "sequences", application)
        if clone := case.get("cloneSequence"):
            destination = local_path(application, clone["destination"])
            shutil.copytree(local_path(application, clone["source"]), destination)
            for xml in destination.rglob("*.xml"):
                xml.write_bytes(xml.read_bytes().replace(clone["source"].encode(), clone["destination"].encode()))
        if mutation := case.get("mutation"):
            path = local_path(application, mutation["path"])
            if mutation["kind"] == "text":
                data = path.read_bytes()
                old = mutation["old"].encode("utf-8")
                if data.count(old) != 1:
                    raise ValueError(f"{case['id']}: expected exactly one mutation site")
                path.write_bytes(data.replace(old, mutation["new"].encode("utf-8")))
            elif mutation["kind"] == "append-bytes":
                with path.open("ab") as stream:
                    stream.write(mutation["text"].encode("utf-8"))
            else:
                raise ValueError(f"Unsupported mutation: {mutation['kind']}")
        refresh_backbone_checksums(application)
        write_zip(application, case["targetSequence"], case_root / "package.zip", case.get("zipMutation"))
        selected_history = [
            path.name for path in sorted(application.iterdir())
            if path.is_dir() and path.name < case["targetSequence"] and path.name not in case.get("omitHistory", [])
        ]
        descriptor = {
            "id": case["id"], "expectedDefect": case["expectedDefect"],
            "targetSequence": case["targetSequence"], "selectedHistory": selected_history,
            "inputFiles": inventory(application),
            "zipSha256": hashlib.sha256((case_root / "package.zip").read_bytes()).hexdigest(),
        }
        (case_root / "input.json").write_text(json.dumps(descriptor, indent=2) + "\n", encoding="utf-8")
        report.append({key: descriptor[key] for key in ("id", "expectedDefect", "targetSequence", "selectedHistory")})
    (output / "cases.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Created 4 baseline packages and {len(cases)} defect variants in {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="New output directory; existing paths are rejected")
    materialize(parser.parse_args().output)
