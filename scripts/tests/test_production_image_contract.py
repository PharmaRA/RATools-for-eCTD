#!/usr/bin/env python3
from pathlib import Path
import re


REPO_ROOT = Path(__file__).resolve().parents[2]
DOCKERFILE = REPO_ROOT / "Dockerfile"
DOCKERIGNORE = REPO_ROOT / ".dockerignore"
FRONTEND_MAIN = REPO_ROOT / "frontend" / "src" / "main.tsx"
API_PROGRAM = REPO_ROOT / "src" / "RATools.Api" / "Program.cs"
API_SETTINGS = REPO_ROOT / "src" / "RATools.Api" / "appsettings.json"


def require(source: str, pattern: str, message: str) -> None:
    if not re.search(pattern, source, re.MULTILINE):
        raise AssertionError(message)


def main() -> None:
    dockerfile = DOCKERFILE.read_text(encoding="utf-8")
    dockerignore = DOCKERIGNORE.read_text(encoding="utf-8")
    frontend_main = FRONTEND_MAIN.read_text(encoding="utf-8")
    api_program = API_PROGRAM.read_text(encoding="utf-8")
    api_settings = API_SETTINGS.read_text(encoding="utf-8")

    require(dockerfile, r"^FROM node:22\.18\.0-bookworm-slim AS frontend-build$", "Pin the frontend build to Node 22.18.0")
    require(dockerfile, r"^RUN npm ci$", "Frontend dependencies must use the package-lock via npm ci")
    require(dockerfile, r"^RUN npm run build$", "The production frontend build and bundle budget must run in the image build")
    require(dockerfile, r"^FROM mcr\.microsoft\.com/dotnet/sdk:8\.0\.423-bookworm-slim AS backend-build$", "Pin the official SDK image to the repository's .NET 8 feature band")
    require(dockerfile, r"^RUN dotnet restore src/RATools\.Api/RATools\.Api\.csproj$", "Restore the API before copying backend source")
    require(dockerfile, r"^RUN dotnet restore src/RATools\.DatabaseMigrator/RATools\.DatabaseMigrator\.csproj$", "Restore the database migrator before copying backend source")
    require(dockerfile, r"dotnet publish src/RATools\.Api/RATools\.Api\.csproj.*--no-restore", "Publish the API in Release without restoring twice")
    require(dockerfile, r"dotnet publish src/RATools\.DatabaseMigrator/RATools\.DatabaseMigrator\.csproj.*--no-restore", "Publish the migrator in Release without restoring twice")
    require(dockerfile, r"^FROM mcr\.microsoft\.com/dotnet/runtime:8\.0\.29-bookworm-slim AS migrator$", "Use the pinned runtime-only image for the migration job")
    require(dockerfile, r"^FROM mcr\.microsoft\.com/dotnet/aspnet:8\.0\.29-bookworm-slim AS runtime$", "Pin the ASP.NET 8 runtime-only final stage")
    require(dockerfile, r"^COPY --from=frontend-build --chown=\$APP_UID:\$APP_UID /src/frontend/dist ./wwwroot$", "Copy the owned frontend build into the API web root")
    require(dockerfile, r"chown -R \$APP_UID:\$APP_UID /app", "Give the non-root runtime user ownership of the writable application directory")
    require(dockerfile, r"^USER \$APP_UID$", "Run the final .NET 8 image as its built-in non-root user")
    require(dockerfile, r'^ENTRYPOINT \["dotnet", "RATools\.Api\.dll"\]$', "Start only the published API in the final stage")
    require(dockerfile, r'^ENTRYPOINT \["dotnet", "RATools\.DatabaseMigrator\.dll"\]$', "Give the migration target a single-purpose entrypoint")
    require(dockerfile, r"rm -f appsettings\.Development\.json", "Remove development credentials from the production image")
    assert dockerfile.rfind(" AS runtime") > dockerfile.rfind(" AS migrator"), "Keep the API as Docker's default final target"
    assert "Password=" not in api_settings, "Production appsettings must not embed a database password"

    forbidden_copies = ("frontend/.env", "App_Data/uploads", "App_Data/publish")
    for forbidden in forbidden_copies:
        if forbidden in dockerfile:
            raise AssertionError(f"Dockerfile must not copy runtime data or browser credentials: {forbidden}")

    required_ignores = (
        ".git",
        ".claude",
        "docs",
        "**/bin",
        "**/obj",
        "frontend/node_modules",
        "frontend/dist",
        "frontend/.env*",
        "src/RATools.Api/App_Data",
    )
    ignored_lines = {
        line.strip()
        for line in dockerignore.splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    }
    missing = [path for path in required_ignores if path not in ignored_lines]
    if missing:
        raise AssertionError(f".dockerignore is missing required entries: {', '.join(missing)}")

    assert "await initializeRuntimeConfig()" in frontend_main, "The frontend must load runtime configuration before rendering"
    assert (
        'app.MapGet("/runtime-config"' in api_program
        and 'context.Response.Headers.CacheControl = "no-store"' in api_program
    ), "The API must generate an uncacheable same-origin runtime browser configuration"


if __name__ == "__main__":
    main()
