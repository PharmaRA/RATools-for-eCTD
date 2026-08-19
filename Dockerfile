# syntax=docker/dockerfile:1

ARG APP_VERSION=0.1.0
ARG VCS_REF=local
ARG BUILD_DATE=1970-01-01T00:00:00Z

FROM node:22.18.0-bookworm-slim AS frontend-build
ARG APP_VERSION
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN test "$(node -p "require('./package.json').version")" = "$APP_VERSION" \
    && npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0.423-bookworm-slim AS backend-build
ARG APP_VERSION
ARG VCS_REF
WORKDIR /src
COPY global.json Directory.Build.props VERSION ./
COPY reference/ ./reference/
COPY src/RATools.Domain/RATools.Domain.csproj src/RATools.Domain/
COPY src/RATools.Application/RATools.Application.csproj src/RATools.Application/
COPY src/RATools.Infrastructure/RATools.Infrastructure.csproj src/RATools.Infrastructure/
COPY src/RATools.Api/RATools.Api.csproj src/RATools.Api/
COPY src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj src/RATools.DatabaseMigrator/
RUN dotnet restore src/RATools.Api/RATools.Api.csproj
RUN dotnet restore src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj
COPY src/ ./src/
RUN test "$(tr -d '\r\n' < VERSION)" = "$APP_VERSION" \
    && dotnet publish src/RATools.Api/RATools.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false /p:ContinuousIntegrationBuild=true /p:SourceRevisionId="$VCS_REF"
RUN dotnet publish src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj --configuration Release --no-restore --output /app/migrator /p:UseAppHost=false /p:ContinuousIntegrationBuild=true /p:SourceRevisionId="$VCS_REF"

FROM mcr.microsoft.com/dotnet/runtime:8.0.29-bookworm-slim AS migrator
ARG APP_VERSION
ARG VCS_REF
ARG BUILD_DATE
WORKDIR /app
COPY --from=backend-build --chown=$APP_UID:$APP_UID /app/migrator ./
LABEL org.opencontainers.image.title="RATools Database Migrator" \
      org.opencontainers.image.description="One-shot schema migrator for RATools for eCTD" \
      org.opencontainers.image.version="$APP_VERSION" \
      org.opencontainers.image.revision="$VCS_REF" \
      org.opencontainers.image.created="$BUILD_DATE" \
      org.opencontainers.image.source="https://github.com/PharmaRA/RATools-for-eCTD" \
      org.opencontainers.image.documentation="https://github.com/PharmaRA/RATools-for-eCTD/blob/master/README.md" \
      org.opencontainers.image.licenses="AGPL-3.0-only"
USER $APP_UID
ENTRYPOINT ["dotnet", "RATools.DatabaseMigrator.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0.29-bookworm-slim AS runtime
ARG APP_VERSION
ARG VCS_REF
ARG BUILD_DATE
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    Deployment__Containerized=true
EXPOSE 8080
COPY --from=backend-build --chown=$APP_UID:$APP_UID /app/publish ./
COPY --from=frontend-build --chown=$APP_UID:$APP_UID /src/frontend/dist ./wwwroot
RUN rm -f appsettings.Development.json \
    && mkdir -p App_Data /data/workspaces \
    && chown -R $APP_UID:$APP_UID /app /data/workspaces
LABEL org.opencontainers.image.title="RATools for eCTD" \
      org.opencontainers.image.description="Local-only eCTD authoring and publishing application" \
      org.opencontainers.image.version="$APP_VERSION" \
      org.opencontainers.image.revision="$VCS_REF" \
      org.opencontainers.image.created="$BUILD_DATE" \
      org.opencontainers.image.source="https://github.com/PharmaRA/RATools-for-eCTD" \
      org.opencontainers.image.documentation="https://github.com/PharmaRA/RATools-for-eCTD/blob/master/README.md" \
      org.opencontainers.image.licenses="AGPL-3.0-only"
USER $APP_UID
ENTRYPOINT ["dotnet", "RATools.Api.dll"]
