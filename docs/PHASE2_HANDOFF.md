# Phase 2 handoff

## What Phase 2 can reuse

Phase 1 leaves a working path from a selected folder to persisted scan and source-file metadata:

```text
ProjectsViewModel
  -> IProjectWorkspaceService
  -> IProjectOnboardingService
  -> IProjectValidationService + IFileDiscoveryService
  -> ProjectOnboardingStore / ForgeDeskDbContext
  -> Project, Scan, SourceFile records
```

A Phase 2 analyzer should not reimplement path validation, project identity, discovery exclusions, or scan lifecycle. It should consume a completed scan and add analysis rows under that scan.

## Roslyn integration points

1. Add a Roslyn implementation in `ForgeDesk.Analysis`, behind `IAnalysisEngine` in `Contracts/AnalysisContracts.cs`.
2. Build a Roslyn `AdhocWorkspace`/`MSBuildWorkspace` adapter from persisted `.sln`, `.slnx`, or `.csproj` metadata. Keep workspace loading in Infrastructure or a dedicated analysis adapter; do not put it in WPF.
3. Read `SourceFile.RelativePath`, resolve it against the registered `Project.RootPath`, and verify it is still inside the root before reading contents.
4. Use `SourceFile.Id` and `Scan.Id` to create deterministic `Symbol.SymbolKey` values. Persist `Symbol.Name`, `Kind`, `StartLine`, and `EndLine` from source locations.
5. Map semantic symbol relationships to `Dependency.SourceSymbolId`, `TargetSymbolId`, `RelationshipType`, and `IsResolved`.
6. Map rule diagnostics to `Finding.RuleId`, `SourceFileId`, `SymbolId`, `Severity`, `Message`, `StartLine`, `EndLine`, and UTC creation time.

`AnalysisFileInput` is intentionally a contract, not a Phase 1 implementation. Phase 2 should avoid loading all large files into memory at once and should accept a cancellation token.

## Existing data model

- `Project` identifies the root and stores type, favorite, last-opened, and last-scanned timestamps.
- `Scan` owns one execution and records status plus `FilesDiscovered` and `FilesProcessed`. The Phase 1 completed status means metadata discovery completed, not semantic analysis.
- `SourceFile` owns relative path, filename, extension, optional small-file hash, size, last modification, language, and discovery status. It does not store content.
- `Symbol`, `Dependency`, and `Finding` tables and EF relationships already exist but are empty after Phase 1 onboarding.

A Phase 2 analysis run should create a new scan or explicitly add an analysis stage to the scan model. It must not silently change the meaning of a Phase 1 discovery-completed scan.

## Database extension guidance

- Add migrations; do not edit `InitialCreate` after it has been applied.
- Prefer additional analysis status/count fields on `Scan` only if the distinction between discovery and semantic analysis is explicit.
- Add indexes for the actual query patterns used by Code Explorer and dependency views, such as `(ScanId, SourceFileId, Kind)` and `(ScanId, TargetSymbolId)`.
- Preserve cascade rules so deleting a project removes all of its scan-owned analysis data.
- Keep enum conversions stable as strings unless there is a measured reason to change.

## Recommended next implementation tasks

1. Add a Phase 2 analysis service that reads a completed discovery scan and accepts a cancellation token/progress reporter.
2. Add a Windows-safe Roslyn workspace loader for `.sln`/`.slnx`/`.csproj` roots.
3. Add integration fixtures with a small C# solution and assert symbol/dependency/finding persistence under a scan.
4. Add Code Explorer viewmodels only after analysis records are real and queryable.
5. Add migration(s) for analysis-specific status and query indexes.
6. Keep future graph/UI pages honest until `Dependency` rows are produced from real semantic analysis.

## Known technical debt

- The current UI has no scan-history detail page or retry action, although failed/cancelled records are preserved.
- `ProjectOnboardingService` runs one metadata discovery pass each time a project is opened; a later phase can add incremental discovery based on `ContentHash` and timestamps.
- SQLite recent ordering is client-side because EF Core SQLite does not translate `DateTimeOffset` ordering. If recent-project volume grows materially, add a normalized numeric sort column in a migration.
- WPF visual and accessibility checks still require Windows. Do not infer them from the Linux build.
