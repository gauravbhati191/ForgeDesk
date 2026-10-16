# ForgeDesk — Phase 1

ForgeDesk is a local-first Windows developer workspace. Phase 1 provides the native WPF foundation, local SQLite persistence, safe C# project onboarding, metadata discovery, recent projects, scan lifecycle records, and a professional navigation shell. Roslyn analysis, dependency graphs, quality rules, Git integration, reports, and AI features are intentionally deferred.

## Requirements

- Windows 10/11 for the WPF application.
- .NET SDK **8.0.416** (pinned by `global.json`). .NET 8 is the selected LTS baseline because the original environment did not have a .NET SDK or WindowsDesktop workload; the project enables Windows targeting so the solution can be compiled on a Linux CI host.
- Git (recommended for version history).
- `make` is optional; the commands below can be run directly with `dotnet`.

The repository contains no secrets and has no mandatory cloud service. SQLite is stored in `%LOCALAPPDATA%\\ForgeDesk\\forgedesk.db` by default. Use `FORGEDESK_DATABASE_PATH` or an infrastructure options override in tests when an isolated path is needed.

## Restore and build

From the repository root:

```bash
 dotnet restore ForgeDesk.sln --locked-mode
 dotnet build ForgeDesk.sln --configuration Release --no-restore
```

On the buildathon Linux image, the checked-in local SDK can be used instead:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./.dotnet/dotnet restore ForgeDesk.sln --locked-mode
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./.dotnet/dotnet build ForgeDesk.sln --configuration Release --no-restore
```

The local `.dotnet` directory is ignored and is only a verification convenience; a clean checkout should use the pinned SDK from the official .NET installer or Docker image.

## Run the application

WPF must run on Windows. After restoring and building:

```powershell
dotnet run --project ForgeDesk.App/ForgeDesk.App.csproj --configuration Release
```

The application initializes migrations on startup, loads the persisted theme, and opens the Home page. Choose **Projects > Open project**, select a folder containing `.cs`, `.csproj`, `.sln`, or `.slnx` files, and ForgeDesk validates the folder, discovers metadata, persists a scan and source-file records, and adds the project to the recent list. Reopening a normalized path reuses its project record and creates a fresh discovery scan. Removing a recent entry clears only its recent marker; it does not delete source files or history.

## Test

The single deterministic test command is:

```bash
make test
```

It restores locked dependencies, builds the complete solution, then runs both xUnit assemblies against temporary directories and temporary SQLite databases. Tests do not modify developer projects and do not require a server or network after packages are restored. Direct commands are:

```bash
dotnet test ForgeDesk.UnitTests/ForgeDesk.UnitTests.csproj --configuration Release
dotnet test ForgeDesk.IntegrationTests/ForgeDesk.IntegrationTests.csproj --configuration Release
```

## Fresh migration check

The checked-in EF Core migration can be applied to a new database with:

```bash
make migrate-fresh
```

The command writes only to `artifacts/forgedesk-fresh.db`. In production the application calls `DatabaseInitializer`, which applies the migration to the per-user database before showing the main window.

## Windows deployment

Publish a framework-dependent Windows build:

```powershell
dotnet publish ForgeDesk.App/ForgeDesk.App.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/publish/win-x64 --no-restore
```

Copy `artifacts/publish/win-x64` to a Windows machine with the .NET 8 Desktop Runtime, or publish self-contained by changing `--self-contained true`. Do not place the database beside the executable; ForgeDesk intentionally uses the per-user application-data directory.

## Docker verification

The Dockerfile builds all projects and executes both test suites in a Linux SDK container:

```bash
docker build -t forgedesk-phase1 .
docker run --rm forgedesk-phase1
```

A Linux container cannot launch a Windows WPF window. Native launch, resizing, theme switching, and the Windows folder picker still require a Windows verification pass.

## Repository layout

- `ForgeDesk.App` — WPF entry point, startup, composition, window, logging, folder picker, theme resource application.
- `ForgeDesk.Presentation` — MVVM viewmodels, views, commands, styles, themes, and UI contracts.
- `ForgeDesk.Application` — onboarding use case, validation, lifecycle, DTOs, and testable boundaries.
- `ForgeDesk.Domain` — framework-independent entities, enums, and invariants.
- `ForgeDesk.Infrastructure` — EF Core SQLite, migrations, repositories, transactional onboarding store, and safe file discovery.
- `ForgeDesk.Analysis` — Phase 2 analysis contracts only.
- `ForgeDesk.Graph` — future graph contracts only.
- `ForgeDesk.UnitTests` / `ForgeDesk.IntegrationTests` — automated coverage for domain, discovery, onboarding, migrations, SQLite persistence, recent state, and settings.
- `docs/` — architecture, database, development, implementation, and Phase 2 handoff notes.
