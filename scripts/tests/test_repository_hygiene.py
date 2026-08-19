#!/usr/bin/env python3
from pathlib import Path, PurePosixPath
import os
import shutil
import string
import subprocess
import sys


REPO_ROOT = Path(__file__).resolve().parents[2]

RUNTIME_PREFIXES = (
    "deploy/production/runtime/",
    "src/RATools.Api/App_Data/uploads/",
    "src/RATools.Api/App_Data/publish/",
)
RUNTIME_ALLOWLIST = {
    "src/RATools.Api/App_Data/publish/.gitkeep",
}
GENERATED_DIRECTORY_NAMES = {
    ".artifacts",
    ".pytest_cache",
    "TestResults",
    "__pycache__",
    "bin",
    "dist",
    "node_modules",
    "obj",
}
SENSITIVE_SUFFIXES = {
    ".db",
    ".key",
    ".p12",
    ".pem",
    ".pfx",
    ".sqlite",
    ".sqlite3",
    ".snk",
}
PRIVATE_KEY_NAMES = {
    "id_dsa",
    "id_ecdsa",
    "id_ed25519",
    "id_rsa",
}
ENV_ALLOWLIST = {
    ".env.example",
    "frontend/.env.development",
}


def find_git() -> str:
    discovered = shutil.which("git")
    if discovered:
        return discovered

    if os.name == "nt":
        candidates: list[Path] = []
        for variable in ("ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA"):
            base = os.environ.get(variable)
            if not base:
                continue
            candidates.extend(
                (
                    Path(base) / "Git" / "cmd" / "git.exe",
                    Path(base) / "Programs" / "Git" / "cmd" / "git.exe",
                )
            )

        candidates.extend(
            Path(f"{drive}:/Program Files/Git/cmd/git.exe")
            for drive in string.ascii_uppercase
        )
        for candidate in candidates:
            if candidate.is_file():
                return str(candidate)

    raise RuntimeError("git executable was not found; add Git to PATH before running this check")


def list_tracked_paths() -> list[str]:
    result = subprocess.run(
        [find_git(), "ls-files", "-z"],
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
    )
    return sorted(
        path.decode("utf-8")
        for path in result.stdout.split(b"\0")
        if path
    )


def violation_for(path: str) -> str | None:
    normalized = path.replace("\\", "/")
    lowered = normalized.lower()
    pure_path = PurePosixPath(normalized)

    if normalized in RUNTIME_ALLOWLIST:
        return None

    for prefix in RUNTIME_PREFIXES:
        if lowered.startswith(prefix.lower()):
            return f"runtime data under {prefix}"

    generated_segments = GENERATED_DIRECTORY_NAMES.intersection(pure_path.parts)
    if generated_segments:
        return f"generated directory segment {sorted(generated_segments)[0]}"

    if pure_path.name in PRIVATE_KEY_NAMES:
        return "private key filename"

    if pure_path.suffix.lower() in SENSITIVE_SUFFIXES:
        return f"database or key file suffix {pure_path.suffix.lower()}"

    if pure_path.name == ".env" or pure_path.name.startswith(".env."):
        if normalized not in ENV_ALLOWLIST and pure_path.name not in ENV_ALLOWLIST:
            return "non-example environment file"

    return None


def verify_policy_examples() -> None:
    examples = {
        "src/RATools.Api/App_Data/uploads/document.pdf": True,
        "src/RATools.Api/App_Data/publish/job/report.json": True,
        "src/RATools.Api/App_Data/publish/.gitkeep": False,
        "src/RATools.Api/bin/Release/RATools.Api.dll": True,
        "frontend/node_modules/react/index.js": True,
        "local.sqlite3": True,
        "deploy/signing-key.pfx": True,
        "deploy/id_ed25519": True,
        ".env.production": True,
        ".env.example": False,
        "frontend/.env.development": False,
        "deploy/production/runtime/secrets/api-key": True,
        "deploy/production/Caddyfile": False,
        "reference/dtd/ich-ectd-3-2.dtd": False,
    }
    for path, should_fail in examples.items():
        failed = violation_for(path) is not None
        if failed != should_fail:
            raise AssertionError(
                f"repository hygiene policy example produced the wrong result: {path}"
            )


def main() -> None:
    verify_policy_examples()
    violations = [
        (path, reason)
        for path in list_tracked_paths()
        if (reason := violation_for(path)) is not None
    ]
    if not violations:
        return

    print("Tracked repository files violate the hygiene policy:", file=sys.stderr)
    for path, reason in violations:
        print(f"- {path}: {reason}", file=sys.stderr)
    raise SystemExit(1)


if __name__ == "__main__":
    main()
