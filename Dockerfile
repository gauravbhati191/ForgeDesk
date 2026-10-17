# Linux verification image. ForgeDesk.App is a Windows-only WPF executable;
# the container builds the full solution and runs its offline SQLite test suites.
FROM mcr.microsoft.com/dotnet/sdk:8.0.416-bookworm-slim AS verify
WORKDIR /src

# ForgeDesk's Git core uses the local Git executable through a no-shell process
# adapter. The package is installed only in the verification image; no NuGet
# dependency or global developer installation is required.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*

COPY global.json .
COPY Directory.Build.props .
COPY ForgeDesk.sln .
COPY Makefile .
COPY ForgeDesk.Domain/ ForgeDesk.Domain/
COPY ForgeDesk.Application/ ForgeDesk.Application/
COPY ForgeDesk.Infrastructure/ ForgeDesk.Infrastructure/
COPY ForgeDesk.Analysis/ ForgeDesk.Analysis/
COPY ForgeDesk.Graph/ ForgeDesk.Graph/
COPY ForgeDesk.Presentation/ ForgeDesk.Presentation/
COPY ForgeDesk.App/ ForgeDesk.App/
COPY ForgeDesk.UnitTests/ ForgeDesk.UnitTests/
COPY ForgeDesk.IntegrationTests/ ForgeDesk.IntegrationTests/
COPY .config/ .config/

RUN dotnet restore ForgeDesk.sln --locked-mode
RUN dotnet build ForgeDesk.sln --configuration Release --no-restore
RUN dotnet test ForgeDesk.UnitTests/ForgeDesk.UnitTests.csproj --configuration Release --no-build --no-restore
RUN dotnet test ForgeDesk.IntegrationTests/ForgeDesk.IntegrationTests.csproj --configuration Release --no-build --no-restore

# `docker run` repeats the deterministic test command. Native WPF launch/publish is
# performed on Windows; see README.md and docs/DEVELOPMENT.md.
ENTRYPOINT ["dotnet", "test", "ForgeDesk.sln", "--configuration", "Release", "--no-build", "--no-restore"]
