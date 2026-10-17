# ForgeDesk user guide

## Install and start

ForgeDesk currently ships as a self-contained `win-x64` ZIP rather than an installer. Extract `artifacts/ForgeDesk-win-x64.zip` to a user-writable application directory and start `ForgeDesk.App.exe`. The package is multi-file and should remain together after extraction.

The application requests `asInvoker` execution and does not require administrator privileges. Application binaries and user data are separate:

- Application files: the directory where the ZIP is extracted.
- Database: `%LOCALAPPDATA%\ForgeDesk\forgedesk.db`.
- Logs: `%LOCALAPPDATA%\ForgeDesk\logs\forgedesk.log` and numbered rotated files.
- Automatic migration backups: `%LOCALAPPDATA%\ForgeDesk\backups\`.

The first launch creates the data directory when possible, applies checked-in migrations, loads the saved theme, and opens the workspace. It does not create fake project data.

## Open a project

1. Open **Projects** and choose the folder picker.
2. Select a solution, C# project, or source folder.
3. Review the discovered file and project metadata.
4. Reopen a recent project from the project list when needed.

ForgeDesk reads metadata during onboarding. It does not store source contents in SQLite. Discovery skips configured generated folders, inaccessible entries, and reparse-point targets outside the selected root. A missing or moved project is marked stale; removing it from the recent list only clears the recent marker and does not delete source files or scan history.

## Run analysis

From the selected project, open **Code Explorer** and choose **Start analysis**.

- **Reading** means discovered C# files are being read after path validation.
- **Binding** uses project-aware Roslyn analysis when the solution/project can be loaded.
- **Syntax-only fallback** is shown when project-aware loading is unavailable; semantic dependency edges may then be absent.
- **Rules** produces configured compiler and code-quality findings.

Use **Cancel** to request cancellation. A cancelled or failed run is kept as a terminal analysis state and is not presented as a completed result. Starting the command again replaces the scan-owned semantic result set transactionally.

## Explore the graph and issues

- **Dependencies** shows persisted symbols and dependency edges from the selected scan.
- Search by symbol, qualified name, or relative path.
- Filter symbol kinds, relationship kinds, direction, and neighborhood depth.
- Select a node to inspect its source location and related findings.
- **Code Health** filters findings by severity, rule, file, and text, and can show a safe source preview.

Empty, partial, stale, failed, and completed states are intentionally distinct. A graph or issue list is scan-scoped and never combines rows from another project.

## Inspect Git context

**Git Changes** reads the nearest local repository. It shows branch/HEAD metadata, changed paths, conflicts, untracked files, and a bounded recent history.

ForgeDesk does not modify the repository. It never stages, commits, resets, checks out, merges, pulls, pushes, runs hooks, or contacts a remote. A project without Git remains usable and shows an explicit not-a-repository state.

## Export reports

**Reports** builds an export from an explicitly selected persisted analysis run. Supported formats are:

- JSON for structured processing.
- HTML for a self-contained human-readable report.
- CSV for findings-oriented tabular use.

Reports include counts, limitations, findings, and optional current Git context. Absolute project and repository roots are redacted by default. The report-time Git context is not a historical commit association because the current schema does not persist one.

Review the destination and report contents before sharing them. Reports can contain project names, relative paths, finding messages, symbol identities, branch names, and commit IDs.

## Themes and local settings

Use **Settings** to switch between the supported light and dark themes. The preference is stored locally in the SQLite `Settings` table. No API keys or cloud credentials are required by the current release.

## Backups and upgrades

ForgeDesk automatically creates a uniquely named SQLite backup before applying pending migrations to an existing file-backed database. It does not automatically prune backups. Keep backups until the corresponding upgrade has been verified.

For a manual backup or restoration workflow, close the app and follow [`DATABASE.md`](DATABASE.md) and [`RELEASE.md`](RELEASE.md). A database backup contains ForgeDesk metadata and analysis results, not the project source tree. Restoring a database does not restore or move a project folder.

## Uninstall and data retention

There is no installer or automatic uninstaller. Delete the extracted application directory to remove the binaries. User data remains in `%LOCALAPPDATA%\ForgeDesk` so an application update does not delete project history.

To remove user data intentionally, close ForgeDesk, preserve any desired backups, and delete that directory using normal Windows file-management tools. This action removes the local database, logs, settings, and migration backups; it does not delete registered project folders or source files.

## Privacy

ForgeDesk is local-first and has no mandatory network service. Git and Roslyn operate on local files. Do not share databases, logs, or reports without reviewing them for project metadata and proprietary information.
