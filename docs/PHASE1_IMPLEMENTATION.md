# ForgeDesk Phase 1 implementation

## Implemented result

Phase 1 is implemented as a native WPF solution targeting .NET 8 LTS. The application is local-first: there is no server, account, authentication, cloud, or AI dependency. Startup composes the WPF shell with the application and infrastructure layers, applies pending EF Core migrations to the user's local SQLite database, loads the saved theme, and then opens the main window.

The user-visible first flow is:

1. Start ForgeDesk.
2. Select **Projects > Open project**.
3. Choose a local folder containing C# source or project metadata.
4. ForgeDesk normalizes and validates the path, determines whether it is a solution, C# project, or source folder, and safely discovers `.cs`, `.csproj`, `.sln`, and `.slnx` files.
5. The project record, a metadata-discovery scan, and file metadata are persisted transactionally in SQLite.
6. The project appears in the persisted recent-project list. Opening the same normalized path again reuses the project record and creates a new scan; removing a recent entry only clears its recent marker.

No file contents are stored in SQLite and no Phase 2 analysis result is fabricated.

## Architecture overview

- `ForgeDesk.Domain` contains entities, enums, path rules, and scan state transitions. It references no WPF or EF Core assembly.
- `ForgeDesk.Application` contains the onboarding and scan use cases, validation, DTOs, clock and persistence interfaces, and the theme setting service.
- `ForgeDesk.Infrastructure` implements SQLite/EF Core, migrations, repositories, the transactional onboarding store, database path management, and safe cancellable file discovery.
- `ForgeDesk.Presentation` contains CommunityToolkit.Mvvm viewmodels, WPF views, navigation models, theme/style dictionaries, and UI contracts.
- `ForgeDesk.App` is the composition root and WPF executable. It owns startup/shutdown, global exception handling, local file logging, the Windows folder picker, database-backed UI adapters, and theme resource application.
- `ForgeDesk.Analysis` and `ForgeDesk.Graph` contain contracts only for future work. They do not claim to analyze code or build graphs.

## Week 1 checklist

- [x] Solution with App, Presentation, Application, Domain, Infrastructure, Analysis, Graph, UnitTests, and IntegrationTests projects.
- [x] WPF application startup with host-based dependency injection and constructor injection.
- [x] Main window with sidebar, project context, status area, responsive content area, and vector resource icons.
- [x] Home, Projects, and Settings pages are functional.
- [x] Code Explorer, Dependencies, Code Health, Git Changes, and Reports show honest Phase 1 boundary states.
- [x] Dark and light resource dictionaries with runtime replacement of the active dictionary.
- [x] Theme preference stored through the SQLite `Settings` table and loaded before the window is shown.
- [x] Reusable card, button, navigation, focus, disabled, and empty-state styles.
- [x] File logger under `%LOCALAPPDATA%\\ForgeDesk\\logs\\forgedesk.log` plus console logging for development.
- [x] Dispatcher, unobserved-task, and AppDomain exception handling boundaries.
- [x] Async commands, busy guards, progress reporting, and cancellation tokens for long-running project operations.

## Week 2 checklist

- [x] SQLite database in per-user application data, with a test-overridable path.
- [x] EF Core entity configurations, relationships, foreign keys, indexes, and checked-in migration.
- [x] Project, Scan, SourceFile, Symbol, Dependency, Finding, and Setting entities.
- [x] Pending, Running, Completed, Failed, and Cancelled scan lifecycle with transition guards.
- [x] Native Windows folder selection through `OpenFolderDialog`.
- [x] Folder validation for missing, inaccessible, empty, unsupported, solution, project, and source-folder inputs.
- [x] Safe discovery exclusions for `.git`, `bin`, `obj`, `.vs`, `.idea`, `TestResults`, and `packages`.
- [x] Reparse-point/symbolic-link boundary checks, duplicate physical-path checks, and per-file metadata.
- [x] Optional SHA-256 hashing for small files; onboarding intentionally leaves it disabled for a quick metadata pass.
- [x] Transactional project/scan/file persistence and failure/cancellation status persistence.
- [x] Recent project loading, stale-path warning, open action, and remove-from-recent action.
- [x] Unit and SQLite integration tests using temporary roots and databases.

## Verification performed in this environment

The checked-in local SDK was used because the starting environment had no `dotnet`, `msbuild`, or WPF workload available. A .NET 8.0.416 SDK was installed under the ignored `.dotnet/` directory for verification. `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` was required because the Debian container has no ICU package.

- `./.dotnet/dotnet restore ForgeDesk.sln --force-evaluate` — completed and regenerated the locked package assets.
- `./.dotnet/dotnet build ForgeDesk.sln --configuration Release --no-restore` — passes after final fixes.
- `./.dotnet/dotnet test ForgeDesk.UnitTests/ForgeDesk.UnitTests.csproj --configuration Release --no-build --no-restore` — 13 tests passed.
- `./.dotnet/dotnet test ForgeDesk.IntegrationTests/ForgeDesk.IntegrationTests.csproj --configuration Release --no-build --no-restore` — 4 tests passed.
- `dotnet ef migrations add InitialCreate` was run with the local `dotnet-ef` 8.0.11 tool; the clean SQLite migration was applied by integration tests.

A Windows GUI launch was not possible in this Linux container. The WPF project did compile with Windows targeting enabled. Native window launch, Windows folder picker behavior, DPI rendering, and visual theme verification remain Windows-only checks.

## Known limitations

- .NET 8 LTS is used instead of .NET 10 because the audited environment did not contain an installed SDK. The target is isolated in `global.json` and package versions are pinned so a future .NET 10 migration can be deliberate.
- The desktop UI currently shows discovery counts and status, but does not yet expose a full scan-history detail panel.
- The Phase 1 metadata pass does not parse Roslyn symbols, dependencies, or quality findings. `Symbol`, `Dependency`, and `Finding` tables are intentionally empty for a newly discovered project.
- `OpenFolderDialog`, WPF rendering, keyboard traversal, high-DPI layout, and theme resource replacement require Windows verification.
- SQLite stores `DateTimeOffset` values as configured by EF Core; ordering of recent entries is intentionally performed in memory because SQLite cannot translate a `DateTimeOffset` `ORDER BY` expression.
- The app log is best-effort and silently ignores a log-file write failure so logging cannot prevent a desktop startup.
