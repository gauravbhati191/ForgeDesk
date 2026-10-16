# ForgeDesk database

## Location and lifecycle

The desktop composition uses `DatabasePathProvider`. By default the database is:

```text
%LOCALAPPDATA%\\ForgeDesk\\forgedesk.db
```

The directory is created before EF migrations run. Tests pass a temporary SQLite path or use an in-memory temporary directory. Do not put the database in Program Files, beside source code, or in source control. The application does not require a password because SQLite is a local file; file-system permissions are the privacy boundary.

`DatabaseInitializer.InitializeAsync` calls `Database.MigrateAsync`. `EnsureCreated` is not used as the production schema path. A failed migration is logged and prevents the shell from reporting a false ready state.

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

### SourceFiles

Each record is metadata only; source contents are never stored. Fields are `Id`, `ScanId`, `RelativePath`, `FileName`, `Extension`, nullable `ContentHash`, `FileSizeBytes`, `LastModifiedAtUtc`, `Language`, `DiscoveryStatus`, plus a normalized physical path used for de-duplication. The `(ScanId, RelativePath)` index is unique and `(ScanId, Extension)` supports counts.

### Symbols

`Id`, `ScanId`, `SourceFileId`, `SymbolKey`, `Name`, `Kind`, `StartLine`, and `EndLine`. The table and unique `(ScanId, SymbolKey)` index are ready for Phase 2, but Phase 1 never populates it.

### Dependencies

`Id`, `ScanId`, nullable `SourceSymbolId`, nullable `TargetSymbolId`, `RelationshipType`, and `IsResolved`. The nullable symbol FKs use `SET NULL` so future partial/unresolved records do not orphan a scan. Phase 1 never fabricates dependency rows.

### Findings

`Id`, `ScanId`, `RuleId`, nullable `SourceFileId`, nullable `SymbolId`, `Severity`, `Message`, nullable `StartLine`/`EndLine`, and `CreatedAtUtc`. Future source/symbol deletes set optional references to null. Phase 1 does not run quality rules.

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
- Enum properties are stored as stable strings, not integer ordinals.
- All application timestamps are UTC `DateTimeOffset` values.

## Migrations

The initial migration is checked in under `ForgeDesk.Infrastructure/Persistence/Migrations/`. The local tool manifest pins `dotnet-ef` 8.0.11.

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

## Backup considerations

Back up the database while ForgeDesk is closed, or copy the SQLite database together with any `forgedesk.db-wal` and `forgedesk.db-shm` files. A project record contains paths and metadata, not source contents, so restoring a database does not restore a project folder. If a path is unavailable after restore, the recent-project screen marks it stale and lets the user remove only the recent marker.
