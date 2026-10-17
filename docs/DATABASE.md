# ForgeDesk database

## Location and lifecycle

The desktop composition uses `DatabasePathProvider`. By default the database is:

```text
%LOCALAPPDATA%\\ForgeDesk\\forgedesk.db
```

The directory is created before EF migrations run. Tests pass a temporary SQLite path or use an in-memory temporary directory. Do not put the database in Program Files, beside source code, or in source control. The application does not require a password because SQLite is a local file; file-system permissions are the privacy boundary.

`DatabaseInitializer.InitializeAsync` calls `Database.MigrateAsync`. `EnsureCreated` is not used as the production schema path. When the configured database is an existing file and pending migrations are detected, it first creates a unique SQLite online backup under `backups/` and verifies it with `PRAGMA quick_check`. A failed backup or migration is logged and prevents the shell from reporting a false ready state.

## Entities

### Projects

| Field | Type | Notes |
|---|---|---|
| `Id` | GUID | Primary key. |
| `Name` | text | Required, max 200. |
| `RootPath` | text | User-facing full path, max 4096. |
| `NormalizedRootPath` | text | Stable lexical identity; unique. |
| `ProjectType` | text enum | `SourceFolder`, `CSharpProject`, or `Solution`. |
| `CreatedAtUtc` / `UpdatedAtUtc` | UTC timestamp | Audit timestamps. |
| `LastScannedAtUtc` | nullable UTC timestamp | Updated only after discovery completes. |
| `IsFavorite` | boolean | Recent-list ordering/future pinning. |
| `LastOpenedAtUtc` | nullable UTC timestamp | Recent marker; clearing it does not delete history. |
| `Status` | text enum | Active/Archived. |
| `Description` | nullable text | Optional metadata. |

A project has many scans. Deleting a project cascades to its scans and all scan-owned records.

### Scans

| Field | Type | Notes |
|---|---|---|
| `Id` | GUID | Primary key. |
| `ProjectId` | GUID | Required foreign key to Projects. |
| `QueuedAtUtc` | UTC timestamp | Internal queue timestamp. |
| `StartedAtUtc` | nullable UTC timestamp | Set on Pending → Running. |
| `CompletedAtUtc` | nullable UTC timestamp | Set on terminal state. |
| `Status` | text enum | Pending, Running, Completed, Failed, Cancelled. |
| `FilesDiscovered` | integer | Number found by discovery. |
| `FilesProcessed` | integer | Number converted to SourceFile metadata. |
| `ErrorMessage` | nullable text | Failure detail, max 4000. |
| `ToolVersion` | text | Phase 1 tool marker, max 128. |
| `CancellationRequested` | boolean | Lifecycle audit flag. |
| `AnalysisStatus` | text enum | NotStarted, Running, Completed, Failed, or Cancelled; independent of discovery `Status`. |
| `AnalysisStartedAtUtc` / `AnalysisCompletedAtUtc` | nullable UTC timestamp | Semantic-analysis stage timestamps. |
| `AnalysisErrorMessage` | nullable text | Analysis failure detail, max 4000. |
| `AnalysisFilesProcessed` | integer | Source files read/bound by the analysis stage. |
| `AnalysisSymbolCount` / `AnalysisDependencyCount` | integer | Counts from the latest completed analysis result. |
| `FindingCount` | integer | Count from the latest completed analysis result. |

### SourceFiles

Each record is metadata only; source contents are never stored. Fields are `Id`, `ScanId`, `RelativePath`, `FileName`, `Extension`, nullable `ContentHash`, `FileSizeBytes`, `LastModifiedAtUtc`, `Language`, `DiscoveryStatus`, plus a normalized physical path used for de-duplication. The `(ScanId, RelativePath)` index is unique and `(ScanId, Extension)` supports counts. The analysis service reads eligible C# files from this metadata; it does not add content columns.

### Symbols

`Id`, `ScanId`, `SourceFileId`, `SymbolKey`, `Name`, `Kind`, `StartLine`, and `EndLine`. Phase 2 replaces these rows after a successful engine run. `SymbolKey` is deterministic and scoped by scan/source-file identity. Unique `(ScanId, SymbolKey)` and lookup `(ScanId, SourceFileId, Kind)` indexes prevent duplicate results and support source-oriented queries.

### Dependencies

`Id`, `ScanId`, nullable `SourceSymbolId`, nullable `TargetSymbolId`, `RelationshipType`, and `IsResolved`. Phase 2 persists only in-scan semantic relationships: type references, calls, inheritance, and interface implementation. External targets are omitted; unresolved error-type relationships may have a null target and `IsResolved == false`. The nullable symbol FKs use `SET NULL` so partial/unresolved records do not orphan a scan. Unique `(ScanId, SourceSymbolId, TargetSymbolId, RelationshipType)` and lookup `(ScanId, TargetSymbolId)` indexes support replacement and graph queries.

### Findings

`Id`, `ScanId`, `RuleId`, nullable `SourceFileId`, nullable `SymbolId`, `Severity`, `Message`, nullable `StartLine`/`EndLine`, and `CreatedAtUtc`. Phase 2 populates deterministic rule/compiler findings and replaces them with the rest of a scan's result set. Current built-in rule IDs are `FD001` TODO/FIXME, `FD002` long method, `FD003` excessive parameters, and `FD004` empty catch. Future source/symbol deletes set optional references to null.

### Settings

`Key` is the text primary key, with `Value` and `UpdatedAtUtc`. `ui.theme` stores `Dark` or `Light` through `ThemeSettingsService`. The unique key constraint makes upsert behavior deterministic.

## Relationships and constraints

- Project → Scan: required, cascade delete.
- Scan → SourceFile/Symbol/Dependency/Finding: required, cascade delete.
- SourceFile → Symbol: required, cascade delete.
- SourceFile → Finding: optional, `SET NULL`.
- Symbol → Dependency source/target: optional, `SET NULL`.
- Symbol → Finding: optional, `SET NULL`.
- Normalized project root and setting key are unique.
- `(ProjectId, AnalysisStatus)` supports finding scans by analysis stage.
- Enum properties are stored as stable strings, not integer ordinals.
- All application timestamps are UTC `DateTimeOffset` values.

## Migrations

The initial migration and the Phase 2 migration are checked in under
`ForgeDesk.Infrastructure/Persistence/Migrations/`. Migration
`20261016080000_AddAnalysisState` adds the analysis fields and the
`(ProjectId, AnalysisStatus)` index. The local tool manifest pins `dotnet-ef`
8.0.11.

Apply migrations to the configured database:

```bash
make migrate-fresh
```

Create a later migration after changing the EF model:

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations add DescriptiveName \
  --project ForgeDesk.Infrastructure/ForgeDesk.Infrastructure.csproj \
  --startup-project ForgeDesk.Infrastructure/ForgeDesk.Infrastructure.csproj \
  --output-dir Persistence/Migrations
```

Then rebuild and run the integration tests. Never replace a migration already applied to a user's database; add a new migration instead.

## Backup and restoration

`IDatabaseBackupService` and `SqliteDatabaseBackupService` use SQLite's online backup API rather than blindly copying a live database file. Backups are written with unique names, are never overwritten or automatically deleted, and are integrity-checked before being reported as complete. The default directory is:

```text
%LOCALAPPDATA%\\ForgeDesk\\backups\\
```

Restore only from a verified backup, keep ForgeDesk writes quiesced, and restart the app afterward. The service rejects restoring from the active database path and verifies the destination after the online backup operation. Tests use temporary databases and never touch a developer's real database.

A project record contains paths and metadata, not source contents, so restoring a database does not restore a project folder. If a path is unavailable after restore, the recent-project screen marks it stale and lets the user remove only the recent marker.
