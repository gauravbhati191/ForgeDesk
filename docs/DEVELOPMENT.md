# ForgeDesk development

## Tooling

- .NET SDK 8.0.416, selected by `global.json`.
- WPF targeting through `net8.0-windows` and `EnableWindowsTargeting=true`.
- CommunityToolkit.Mvvm 8.3.2.
- Microsoft.Extensions.DependencyInjection/Hosting/Logging 8.0.x.
- EF Core SQLite and Design 8.0.11.
- xUnit 2.9.2 with Microsoft.NET.Test.Sdk 17.11.1.
- `dotnet-ef` 8.0.11 from `.config/dotnet-tools.json`.

The Linux verification container used `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` because ICU is not installed. A normal Windows SDK installation does not need that variable.

## First checkout

```bash
git clone <repository>
cd WEBDOTNET_PROJECT
dotnet --version                 # must resolve through global.json
dotnet tool restore
dotnet restore ForgeDesk.sln --locked-mode
```

No global NuGet packages or global EF tools are required. The repository's local tool manifest keeps the EF CLI version reproducible. Docker users can run `docker build -t forgedesk-phase1 .` instead of installing SDK tooling locally.

## Checks

The canonical check is:

```bash
make test
```

It runs restore, a Release solution build, 13 unit tests, and 4 integration tests. The tests are local-only and use temporary folders/databases. Useful focused commands are:

```bash
make test-unit
make test-integration
make build
```

A clean migration check is:

```bash
make migrate-fresh
```

This uses `artifacts/forgedesk-fresh.db`, which is ignored by Git.

## Windows run/debug

```powershell
dotnet run --project ForgeDesk.App/ForgeDesk.App.csproj --configuration Debug
```

The first run creates `%LOCALAPPDATA%\\ForgeDesk\\forgedesk.db` and `%LOCALAPPDATA%\\ForgeDesk\\logs\\forgedesk.log`. To inspect a clean database, close the app and move the database aside; startup will apply the checked-in initial migration again.

The Windows folder picker is `Microsoft.Win32.OpenFolderDialog`. Do not replace it with a browser, web view, or cross-platform UI toolkit.

## Adding a feature safely

1. Start with a domain/application contract if the behavior is not presentation-specific.
2. Keep the WPF viewmodel dependent on an interface and keep database scopes short-lived.
3. Use UTC timestamps and migrations for schema changes.
4. Add a unit test for a domain/application boundary and an integration test for any persistence behavior.
5. Run `make test` after the change and inspect the diff before committing.

Do not implement Roslyn symbol extraction, dependency graphs, quality rules, Git, AI, authentication, or a server in Phase 1. Use `docs/PHASE2_HANDOFF.md` for the intended extension points.

## Troubleshooting

### `dotnet` selects the wrong SDK

Install SDK 8.0.416 or a compatible 8.0 SDK and verify `dotnet --version`. `global.json` intentionally does not roll to another major version.

### Locked restore reports NU1004

Run `dotnet restore ForgeDesk.sln --force-evaluate` only when project references or package versions have intentionally changed, then commit the updated `packages.lock.json` files. Normal CI and clean verification use `--locked-mode`.

### Linux build complains about Windows targeting

The projects already set `EnableWindowsTargeting=true`. The Linux SDK can compile the WPF project, but it cannot launch the Windows executable. Use Docker for build/test and Windows for GUI checks.

### Startup reports a database failure

Check that `%LOCALAPPDATA%\\ForgeDesk` is writable and that no other process has an incompatible lock. Read `forgedesk.log`. Do not delete the database blindly; back it up first. A corrupt database can be moved aside and allowed to be recreated from migrations, but its historical records will then need a backup restore.

### A recent path is stale

The project record is intentionally retained. Restore or move the folder back into place, or choose Remove in the recent-project list. Remove only clears `LastOpenedAtUtc`; it does not delete source files or scan history.
