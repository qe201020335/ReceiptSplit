FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
# The SQLite database and uploaded photos live here. It's owned by the non-root app user so a
# named volume mounted over it inherits that ownership.
RUN mkdir -p /app/data && chown $APP_UID /app/data
USER $APP_UID
ENV Storage__Root=/app/data
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ReceiptSplit/ReceiptSplit.csproj", "ReceiptSplit/"]
RUN dotnet restore "ReceiptSplit/ReceiptSplit.csproj"
COPY . .
WORKDIR "/src/ReceiptSplit"
RUN dotnet build "./ReceiptSplit.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ReceiptSplit.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ReceiptSplit.dll"]
