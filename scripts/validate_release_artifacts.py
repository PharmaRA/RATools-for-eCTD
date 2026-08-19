#!/usr/bin/env python3
from __future__ import annotations

from argparse import ArgumentParser
from hashlib import sha256
from pathlib import Path
import json
import re
import sys


REPO_ROOT = Path(__file__).resolve().parents[1]
VERSION_FILE = REPO_ROOT / "VERSION"
SEMVER_PATTERN = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")


class ReleaseArtifactError(RuntimeError):
    pass


def repository_version() -> str:
    version = VERSION_FILE.read_text(encoding="ascii").strip()
    if SEMVER_PATTERN.fullmatch(version) is None:
        raise ReleaseArtifactError("VERSION must contain a stable semantic version such as 0.1.0")
    return version


def load_sbom(path: Path) -> dict[str, object]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise ReleaseArtifactError(f"Unable to read SBOM {path}: {exception}") from exception
    if not isinstance(value, dict):
        raise ReleaseArtifactError(f"SBOM must contain a JSON object: {path}")
    return value


def validate_sbom(
    path: Path,
    *,
    expected_name: str,
    expected_version: str,
    required_purl_prefix: str,
) -> int:
    sbom = load_sbom(path)
    if sbom.get("bomFormat") != "CycloneDX":
        raise ReleaseArtifactError(f"SBOM is not CycloneDX JSON: {path}")
    spec_version = sbom.get("specVersion")
    if not isinstance(spec_version, str) or not spec_version.startswith("1."):
        raise ReleaseArtifactError(f"SBOM has an unsupported CycloneDX version: {path}")
    serial_number = sbom.get("serialNumber")
    if not isinstance(serial_number, str) or not serial_number.startswith("urn:uuid:"):
        raise ReleaseArtifactError(f"SBOM has no UUID serial number: {path}")

    metadata = sbom.get("metadata")
    if not isinstance(metadata, dict):
        raise ReleaseArtifactError(f"SBOM has no metadata object: {path}")
    component = metadata.get("component")
    if not isinstance(component, dict):
        raise ReleaseArtifactError(f"SBOM has no subject component: {path}")
    if component.get("name") != expected_name or component.get("version") != expected_version:
        raise ReleaseArtifactError(
            f"SBOM subject must be {expected_name} {expected_version}: {path}"
        )

    components = sbom.get("components")
    if not isinstance(components, list) or not components:
        raise ReleaseArtifactError(f"SBOM contains no software components: {path}")
    bom_refs: set[str] = set()
    matching_purl = False
    for entry in components:
        if not isinstance(entry, dict):
            raise ReleaseArtifactError(f"SBOM contains an invalid component entry: {path}")
        name = entry.get("name")
        version = entry.get("version")
        if not isinstance(name, str) or not name:
            raise ReleaseArtifactError(f"SBOM component is missing its name: {path}")
        if version is not None and (not isinstance(version, str) or not version):
            raise ReleaseArtifactError(f"SBOM component has an invalid version: {path}")
        bom_ref = entry.get("bom-ref")
        if isinstance(bom_ref, str):
            if bom_ref in bom_refs:
                raise ReleaseArtifactError(f"SBOM contains a duplicate bom-ref: {bom_ref}")
            bom_refs.add(bom_ref)
        purl = entry.get("purl")
        if isinstance(purl, str) and purl.lower().startswith(required_purl_prefix.lower()):
            if not isinstance(version, str) or not version:
                raise ReleaseArtifactError(
                    f"Required ecosystem component is missing its version: {path}"
                )
            matching_purl = True

    if not matching_purl:
        raise ReleaseArtifactError(
            f"SBOM does not cover the required ecosystem {required_purl_prefix}: {path}"
        )
    return len(components)


def file_sha256(path: Path) -> str:
    digest = sha256()
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def write_checksums(paths: list[Path], destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    lines = [f"{file_sha256(path)}  {path.name}" for path in sorted(paths, key=lambda item: item.name)]
    destination.write_text("\n".join(lines) + "\n", encoding="ascii")


def main() -> None:
    parser = ArgumentParser(description="Validate versioned CycloneDX release SBOMs.")
    parser.add_argument("--source-sbom", type=Path, required=True)
    parser.add_argument("--image-sbom", type=Path, required=True)
    parser.add_argument("--migrator-sbom", type=Path, required=True)
    parser.add_argument("--checksums", type=Path, required=True)
    args = parser.parse_args()

    version = repository_version()
    source_count = validate_sbom(
        args.source_sbom,
        expected_name="RATools-for-eCTD-source",
        expected_version=version,
        required_purl_prefix="pkg:npm/",
    )
    image_count = validate_sbom(
        args.image_sbom,
        expected_name="RATools-for-eCTD-image",
        expected_version=version,
        required_purl_prefix="pkg:nuget/",
    )
    migrator_count = validate_sbom(
        args.migrator_sbom,
        expected_name="RATools-for-eCTD-migrator",
        expected_version=version,
        required_purl_prefix="pkg:nuget/",
    )
    write_checksums(
        [args.source_sbom, args.image_sbom, args.migrator_sbom],
        args.checksums,
    )
    print(
        f"Validated RATools {version} SBOMs: "
        f"{source_count} source components, {image_count} API image components, "
        f"{migrator_count} migrator image components"
    )


if __name__ == "__main__":
    try:
        main()
    except (ReleaseArtifactError, OSError) as exception:
        print(f"Release artifact validation failed: {exception}", file=sys.stderr)
        raise SystemExit(1) from None
