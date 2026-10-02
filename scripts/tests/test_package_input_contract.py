"""Independent digest oracle for captured directory/ZIP manifest v2."""

import hashlib
import json

from test_publisher_reference_contract import ROOT, canonical_json


def sequence_digest(sequence: str) -> str:
    root = ROOT / "tests/RATools.Tests/Fixtures/Publisher/sequences" / sequence
    files = []
    directories = []
    for path in sorted(root.rglob("*"), key=lambda p: p.relative_to(root).as_posix()):
        logical = sequence + "/" + path.relative_to(root).as_posix()
        if path.is_dir():
            directories.append({"logicalPath": logical, "isEmpty": not any(path.iterdir())})
        else:
            data = path.read_bytes()
            files.append({"logicalPath": logical, "length": len(data),
                          "sha256": hashlib.sha256(data).hexdigest(), "md5": hashlib.md5(data).hexdigest()})
    content = {"schemaVersion": 2, "applicationId": "00000000-0000-0000-0000-000000000001",
               "sequenceNumber": sequence, "files": files, "directories": directories,
               "additionalFiles": [], "additionalDirectories": []}
    return hashlib.sha256(canonical_json(content).encode("utf-8")).hexdigest()


def verify_package_input_contract() -> None:
    vectors = json.loads((ROOT / "reference/publisher/input-manifest-v2-vectors.json").read_text(encoding="utf-8"))
    assert vectors["schemaVersion"] == 1
    for sequence, digest in vectors["sequenceDigests"].items():
        assert sequence_digest(sequence) == digest, sequence
    print("Independent package input manifest v2 digests passed")


if __name__ == "__main__":
    verify_package_input_contract()
