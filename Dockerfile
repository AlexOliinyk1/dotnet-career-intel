# Stage 1: Restore dependencies
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY dotnet-career-intel.sln ./
COPY src/CareerIntel.Core/CareerIntel.Core.csproj src/CareerIntel.Core/
COPY src/CareerIntel.Analysis/CareerIntel.Analysis.csproj src/CareerIntel.Analysis/
COPY src/CareerIntel.Intelligence/CareerIntel.Intelligence.csproj src/CareerIntel.Intelligence/
COPY src/CareerIntel.Matching/CareerIntel.Matching.csproj src/CareerIntel.Matching/
COPY src/CareerIntel.Persistence/CareerIntel.Persistence.csproj src/CareerIntel.Persistence/
COPY src/CareerIntel.Resume/CareerIntel.Resume.csproj src/CareerIntel.Resume/
COPY src/CareerIntel.Scrapers/CareerIntel.Scrapers.csproj src/CareerIntel.Scrapers/
COPY src/CareerIntel.Notifications/CareerIntel.Notifications.csproj src/CareerIntel.Notifications/
COPY src/CareerIntel.Cli/CareerIntel.Cli.csproj src/CareerIntel.Cli/
COPY src/CareerIntel.Web/CareerIntel.Web.csproj src/CareerIntel.Web/
COPY tests/CareerIntel.Tests/CareerIntel.Tests.csproj tests/CareerIntel.Tests/
COPY tests/CareerIntel.Web.Tests/CareerIntel.Web.Tests.csproj tests/CareerIntel.Web.Tests/
RUN dotnet restore dotnet-career-intel.sln

# Stage 2: Build
FROM restore AS build
COPY . .
RUN dotnet build src/CareerIntel.Web/CareerIntel.Web.csproj -c Release --no-restore

# Stage 3: Publish
FROM build AS publish
RUN dotnet publish src/CareerIntel.Web/CareerIntel.Web.csproj -c Release -o /app/publish --no-build

# Stage 4: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5050

EXPOSE 5050

# Create data directory (use existing app user from base image)
RUN mkdir -p /app/data && chown -R app:app /app
VOLUME /app/data

COPY --from=publish --chown=app:app /app/publish .

USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:5050/health || exit 1

ENTRYPOINT ["dotnet", "CareerIntel.Web.dll"]
