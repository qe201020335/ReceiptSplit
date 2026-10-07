FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
# The SQLite database and uploaded photos live here. It's owned by the non-root app user so a
# named volume mounted over it inherits that ownership.
RUN mkdir -p /app/data && chown $APP_UID /app/data
USER $APP_UID
ENV Storage__Root=/app/data
EXPOSE 8080

# Node for building receiptsplit.client: Vite 8 needs Node 20.19+ and the SDK image has none.
FROM node:24-slim AS node

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/ReceiptSplit/ReceiptSplit.csproj", "src/ReceiptSplit/"]
# Restoring the server also runs npm install for the client it references.
COPY ["receiptsplit.client/receiptsplit.client.esproj", "receiptsplit.client/package.json", "receiptsplit.client/package-lock.json", "receiptsplit.client/"]
RUN dotnet restore "src/ReceiptSplit/ReceiptSplit.csproj"
COPY . .
WORKDIR "/src/src/ReceiptSplit"
RUN dotnet build "./ReceiptSplit.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Publishing runs the client's npm build and places its dist output in wwwroot.
RUN dotnet publish "./ReceiptSplit.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ReceiptSplit.dll"]
