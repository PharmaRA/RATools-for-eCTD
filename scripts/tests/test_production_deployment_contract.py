#!/usr/bin/env python3
from pathlib import Path
import os
import re
import subprocess
import sys
import tempfile


REPO_ROOT = Path(__file__).resolve().parents[2]
COMPOSE = REPO_ROOT / "compose.production.yml"
CADDYFILE = REPO_ROOT / "deploy" / "production" / "Caddyfile"
DOCKERFILE = REPO_ROOT / "Dockerfile"
GITIGNORE = REPO_ROOT / ".gitignore"
INITIALIZER = REPO_ROOT / "scripts" / "initialize_production.py"


def require(source: str, pattern: str, message: str) -> None:
    if not re.search(pattern, source, re.MULTILINE):
        raise AssertionError(message)


def verify_initializer() -> None:
    with tempfile.TemporaryDirectory(prefix="ratools-production-contract-") as temp_root:
        command = [
            sys.executable,
            str(INITIALIZER),
            "--runtime-root",
            temp_root,
        ]
        subprocess.run(command, cwd=REPO_ROOT, check=True, capture_output=True, text=True)

        secrets_root = Path(temp_root) / "secrets"
        api_key = (secrets_root / "api-key").read_text(encoding="utf-8")
        postgres_password = (secrets_root / "postgres-password").read_text(encoding="utf-8")
        assert len(api_key) >= 64, "Generated API key is too short"
        assert len(postgres_password) >= 64, "Generated PostgreSQL password is too short"
        assert api_key != postgres_password, "Generated secrets must be independent"
        assert not api_key.endswith(("\r", "\n")), "Secret files must not have trailing newlines"
        assert not postgres_password.endswith(("\r", "\n")), "Secret files must not have trailing newlines"

        if os.name != "nt":
            assert (secrets_root / "api-key").stat().st_mode & 0o777 == 0o600
            assert (secrets_root / "postgres-password").stat().st_mode & 0o777 == 0o600

        second_run = subprocess.run(
            command,
            cwd=REPO_ROOT,
            check=False,
            capture_output=True,
            text=True,
        )
        assert second_run.returncode != 0, "Initializer must refuse to overwrite existing secrets"
        assert "Refusing to overwrite" in second_run.stderr
        assert "Traceback" not in second_run.stderr
        assert (secrets_root / "api-key").read_text(encoding="utf-8") == api_key
        assert (secrets_root / "postgres-password").read_text(encoding="utf-8") == postgres_password


def main() -> None:
    compose = COMPOSE.read_text(encoding="utf-8")
    caddyfile = CADDYFILE.read_text(encoding="utf-8")
    dockerfile = DOCKERFILE.read_text(encoding="utf-8")
    gitignore = GITIGNORE.read_text(encoding="utf-8")

    assert '"127.0.0.1:80:8080"' in compose
    assert '"127.0.0.1:443:8443"' in compose
    assert 'network_mode: "service:proxy"' in compose
    assert compose.count('network_mode: "service:proxy"') == 2
    assert "0.0.0.0:" not in compose, "Production services must not publish or target wildcard addresses"
    assert "networks:" not in compose, "API and PostgreSQL must not join a separately routable bridge network"

    require(compose, r"^\s+POSTGRES_PASSWORD_FILE: /run/secrets/postgres_password$", "PostgreSQL must consume a file secret")
    require(compose, r"^\s+FileSecrets__ApiKeyPath: /run/secrets/api_key$", "API key must come from a file secret")
    require(compose, r"^\s+FileSecrets__PostgreSqlPasswordPath: /run/secrets/postgres_password$", "API database password must come from a file secret")
    assert not re.search(r"^\s+POSTGRES_PASSWORD:\s*", compose, re.MULTILINE)
    assert not re.search(r"^\s+Security__ApiKey:\s*", compose, re.MULTILINE)
    assert "./deploy/production/runtime/secrets/api-key" in compose
    assert "./deploy/production/runtime/secrets/postgres-password" in compose

    for volume_target in (
        "/var/lib/postgresql/data",
        "/app/App_Data",
        "/data/workspaces",
        "/data",
        "/config",
    ):
        assert volume_target in compose, f"Missing persistent mount target: {volume_target}"
    assert compose.count("read_only: true") == 3
    assert compose.count("no-new-privileges:true") == 3
    assert 'Security__AllowDestructiveOperations: "false"' in compose

    assert "tls internal" in caddyfile
    assert "redir https://localhost{uri} 308" in caddyfile
    assert "reverse_proxy 127.0.0.1:5000" in caddyfile
    for header in (
        "Strict-Transport-Security",
        "Content-Security-Policy",
        "Cross-Origin-Opener-Policy",
        "Cross-Origin-Resource-Policy",
        "Permissions-Policy",
        "Referrer-Policy",
        "X-Content-Type-Options",
        "X-Frame-Options",
    ):
        assert header in caddyfile, f"Missing security header: {header}"

    assert "mkdir -p App_Data /data/workspaces" in dockerfile
    assert "chown -R $APP_UID:$APP_UID /app /data/workspaces" in dockerfile
    assert "/deploy/production/runtime/" in gitignore
    verify_initializer()


if __name__ == "__main__":
    main()
