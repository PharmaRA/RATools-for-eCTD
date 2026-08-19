# syntax=docker/dockerfile:1

FROM node:22.18.0-bookworm-slim AS frontend-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0.423-bookworm-slim AS backend-build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY reference/ ./reference/
COPY src/RATools.Domain/RATools.Domain.csproj src/RATools.Domain/
COPY src/RATools.Application/RATools.Application.csproj src/RATools.Application/
COPY src/RATools.Infrastructure/RATools.Infrastructure.csproj src/RATools.Infrastructure/
COPY src/RATools.Api/RATools.Api.csproj src/RATools.Api/
RUN dotnet restore src/RATools.Api/RATools.Api.csproj
COPY src/ ./src/
RUN dotnet publish src/RATools.Api/RATools.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0.29-bookworm-slim AS runtime
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
USER $APP_UID
ENTRYPOINT ["dotnet", "RATools.Api.dll"]
