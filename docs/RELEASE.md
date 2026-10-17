# ForgeDesk release and upgrade guide

## Release metadata

- Product: ForgeDesk
- Current application version: `1.0.0`
- Supported package RID: `win-x64`
- Target: .NET 8 WPF on Windows 10/11-class desktop environments
- Publish mode: Release, self-contained, multi-file
- Single-file, trimming, and Native AOT: explicitly disabled
- Installer: not included
- Code signing: not configured

The application version is defined in `ForgeDesk.App/ForgeDesk.App.csproj`. Change it deliberately for a release and keep the assembly/file version compatible with the chosen semantic-versioning policy.

## Reproducible package build

### Windows PowerShell

From the repository root, with the SDK selected by `global.json` installed:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\package-release.ps1
```

The script performs locked restore, cleans only `artifacts/publish/win-x64` and the prior ZIP, publishes the WPF application self-contained, checks the executable/runtime/native SQLite files, rejects databases/logs/secrets/signing files, and validates the ZIP.

### Make

On a system with `make`, `dotnet`, and `zip`:

```bash
make package-release
```

The same workflow is split into:

```bash
make restore
make build
make publish
```

Expected artifacts are:

```text
artifacts/publish/win-x64/
artifacts/ForgeDesk-win-x64.zip
```

The artifacts directory is ignored by Git and must not be committed. A release archive must not contain a user database, logs, `.env` files, development settings, private keys, or signing material.

## Pre-release validation

Run these checks from a clean checkout:

```bash
make test
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./.dotnet/dotnet list ForgeDesk.sln package --vulnerable --include-transitive
git diff --check
```

On Windows, extract the ZIP to a temporary user-writable directory and verify:

1. `ForgeDesk.App.exe` starts without an installed .NET runtime.
2. `%LOCALAPPDATA%\ForgeDesk` is created and no database/log is written beside the executable.
3. A project can be added, analyzed, inspected, and reported.
4. Git Changes works with a local repository and remains read-only.
5. Light/dark themes, cancellation, errors, report dialogs, keyboard navigation, and high-DPI resizing behave correctly.
6. Closing and reopening the app preserves the database and settings.

The current Linux environment can compile and publish the Windows target, but it cannot verify these native WPF workflows.

## Installation

1. Download the ZIP from a trusted release location.
2. Optionally verify the published hash from the release record.
3. Extract the complete ZIP to a user-writable directory.
4. Run `ForgeDesk.App.exe`.

No administrator privileges are required by the application manifest. Do not place the database in the installation directory manually.

## Manual upgrade

1. Close all ForgeDesk processes.
2. Preserve `%LOCALAPPDATA%\ForgeDesk`, especially `forgedesk.db` and the `backups` directory.
3. Extract the new package to a new directory or replace only the old application files.
4. Start the new executable.
5. Allow `DatabaseInitializer` to apply checked-in migrations. For an existing file-backed database with pending migrations, ForgeDesk creates a unique SQLite backup before migration.
6. Confirm that recent projects, analysis history, and settings are present.

Never replace an existing migration file to make an upgrade appear clean. A schema change must add a new migration and test both fresh and upgrade paths.

## Rollback and restore

If a migration or release needs to be rolled back:

1. Close ForgeDesk.
2. Preserve the current database and all backups; do not overwrite them.
3. Use the verified backup created before migration, or restore a separately copied database using the database recovery service/test workflow.
4. Start the older application only with a database schema it supports.

A restored database contains ForgeDesk metadata and results, not the project source. Reopen or relocate the source project if its path changed.

## Uninstall and data retention

There is no installer or automated uninstaller. Removing the extracted application directory removes binaries only. User data remains under `%LOCALAPPDATA%\ForgeDesk` until the user explicitly deletes it.

To fully remove local data, close ForgeDesk, preserve any desired backup, and delete `%LOCALAPPDATA%\ForgeDesk`. This does not delete project folders or source files.
