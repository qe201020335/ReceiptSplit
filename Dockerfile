# syntax=docker/dockerfile:1
# The 1.x frontend, for COPY --parents (stable from 1.20).

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
# The image has no HTTP client for the health check below.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
# The SQLite database and uploaded photos live here. It's owned by the non-root app user so a
# named volume mounted over it inherits that ownership.
RUN mkdir -p /app/data && chown $APP_UID /app/data
USER $APP_UID
ENV Storage__Root=/app/data
EXPOSE 8080
# Healthy only while /health answers 200, so the container shows unhealthy while receipts can't be read. With
# --fail-with-body the health log keeps Degraded or Unhealthy; curl's own exit code 22 becomes 1, as Docker reserves
# 2. The start period covers migrations and the startup check's 30 s timeout.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --start-interval=5s --retries=3 \
    CMD curl -sS --fail-with-body -w "\n" "http://localhost:${ASPNETCORE_HTTP_PORTS:-8080}/health" || exit 1

# Node for building receiptsplit.client: Vite 8 needs Node 20.19+ and the SDK image has none.
FROM node:24-slim AS node

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx
ARG BUILD_CONFIGURATION=Release
WORKDIR /source
# Only the build settings and project files first, so the restore layers stay cached until a project or package
# changes. --parents keeps each project file in its folder, so a new project under src/ is picked up by itself.
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY --parents src/*/*.csproj ./
COPY ["receiptsplit.client/receiptsplit.client.esproj", "receiptsplit.client/package.json", "receiptsplit.client/package-lock.json", "receiptsplit.client/"]
RUN dotnet restore "src/ReceiptSplit/ReceiptSplit.csproj"
# Restoring doesn't install the client's npm packages; building the server would, after every code change. Installed
# here, they are cached with the restore, and the build finds node_modules already there.
RUN npm ci --prefix receiptsplit.client
COPY . .
WORKDIR "/source/src/ReceiptSplit"
# --no-restore makes a project the restore above missed fail the build instead of restoring it uncached.
RUN dotnet build "./ReceiptSplit.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Publishing runs the client's npm build and places its dist output in wwwroot.
RUN dotnet publish "./ReceiptSplit.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ReceiptSplit.dll"]
