#!/usr/bin/env python3
from argparse import ArgumentParser
from pathlib import Path
import os
import secrets
import sys


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RUNTIME_ROOT = REPO_ROOT / "deploy" / "production" / "runtime"


def write_secret(path: Path, value: str) -> None:
    try:
        with path.open("x", encoding="utf-8") as secret_file:
            secret_file.write(value)
        if os.name != "nt":
            path.chmod(0o600)
    except Exception:
        path.unlink(missing_ok=True)
        raise


def main() -> None:
    parser = ArgumentParser(description="Create local production Compose secret files.")
    parser.add_argument(
        "--runtime-root",
        type=Path,
        default=DEFAULT_RUNTIME_ROOT,
        help="Runtime directory for generated secret files.",
    )
    args = parser.parse_args()

    secrets_root = args.runtime_root.resolve() / "secrets"
    secrets_root.mkdir(parents=True, exist_ok=True)
    secret_paths = [
        secrets_root / "api-key",
        secrets_root / "postgres-password",
    ]
    existing_paths = [path for path in secret_paths if path.exists()]
    if existing_paths:
        names = ", ".join(path.name for path in existing_paths)
        raise FileExistsError(
            f"Refusing to overwrite existing production secrets: {names}. Credential rotation must also update the running database."
        )

    created_paths: list[Path] = []
    try:
        write_secret(secret_paths[0], secrets.token_urlsafe(48))
        created_paths.append(secret_paths[0])
        write_secret(secret_paths[1], secrets.token_urlsafe(48))
        created_paths.append(secret_paths[1])
    except Exception:
        for created_path in created_paths:
            created_path.unlink(missing_ok=True)
        raise
    print(f"Production secrets initialized under {secrets_root}")


if __name__ == "__main__":
    try:
        main()
    except FileExistsError as exception:
        print(exception, file=sys.stderr)
        raise SystemExit(1) from None
