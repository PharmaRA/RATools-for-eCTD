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
PROMETHEUS = REPO_ROOT / "deploy" / "production" / "prometheus.yml"
ALERTS = REPO_ROOT / "deploy" / "production" / "alerts.yml"
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
    prometheus = PROMETHEUS.read_text(encoding="utf-8")
    alerts = ALERTS.read_text(encoding="utf-8")
    dockerfile = DOCKERFILE.read_text(encoding="utf-8")
    gitignore = GITIGNORE.read_text(encoding="utf-8")

    assert '"127.0.0.1:80:8080"' in compose
    assert '"127.0.0.1:443:8443"' in compose
    assert 'network_mode: "service:proxy"' in compose
    assert compose.count('network_mode: "service:proxy"') == 4
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
        "/prometheus",
    ):
        assert volume_target in compose, f"Missing persistent mount target: {volume_target}"
    assert compose.count("read_only: true") == 5
    assert compose.count("no-new-privileges:true") == 5
    assert 'Security__AllowDestructiveOperations: "false"' in compose
    assert "target: migrator" in compose
    assert "condition: service_completed_successfully" in compose
    assert "prom/prometheus:v3.13.2" in compose
    assert "--web.listen-address=127.0.0.1:9090" in compose
    assert '["CMD", "/bin/promtool", "query", "instant", "http://127.0.0.1:9090", "up"]' in compose
    assert "9090:" not in compose, "Prometheus must not publish a host port"

    assert "tls internal" in caddyfile
    assert "redir https://localhost{uri} 308" in caddyfile
    assert "reverse_proxy 127.0.0.1:5000" in caddyfile
    assert "@metrics path /metrics" in caddyfile
    assert "respond @metrics 404" in caddyfile
    assert "health_uri /health/ready" in caddyfile
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

    assert "127.0.0.1:5000" in prometheus
    assert "metrics_path: /metrics" in prometheus
    for alert_name in (
        "RAToolsApiUnavailable",
        "RAToolsPublishQueueBacklog",
        "RAToolsPublishQueueSampleFailed",
        "RAToolsPublishJobP95Slow",
        "RAToolsPublishJobFailureRateHigh",
    ):
        assert f"alert: {alert_name}" in alerts, f"Missing alert rule: {alert_name}"
    for metric_name in (
        "ratools_publish_queue_depth",
        "ratools_publish_queue_sample_success",
        "ratools_publish_job_duration_seconds_bucket",
        "ratools_publish_jobs_terminal_total",
    ):
        assert metric_name in alerts, f"Alerts do not use required metric: {metric_name}"

    assert "mkdir -p App_Data /data/workspaces" in dockerfile
    assert "chown -R $APP_UID:$APP_UID /app /data/workspaces" in dockerfile
    assert "/deploy/production/runtime/" in gitignore
    verify_initializer()


if __name__ == "__main__":
    main()
