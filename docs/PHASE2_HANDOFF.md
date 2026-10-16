# Phase 2 handoff

Phase 2 core analysis is implemented. See
[`PHASE2_IMPLEMENTATION.md`](PHASE2_IMPLEMENTATION.md) for the complete
component map, data flow, loading modes, rules, commands, limitations, and
verification record.

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

A Phase 2 analyzer does not reimplement path validation, project identity, or
discovery exclusions. It consumes a discovery-completed scan, adds an explicit
analysis stage to that scan, and owns only analysis rows and analysis status.

## Implemented Roslyn integration

1. `ForgeDesk.Analysis/RoslynAnalysisEngine.cs` implements the Application
   `IAnalysisEngine` contract.
2. It attempts `MSBuildWorkspace` loading for `.slnx`, `.sln`, and `.csproj`
   roots using `Microsoft.Build.Locator`. It reports an explicit
   `SyntaxOnlyFallback` when loading is unavailable; it does not present an
   Adhoc/source parse as project-aware semantic loading.
3. `AnalysisService` resolves discovered `SourceFile.RelativePath` values under
   the registered `Project.RootPath` and checks lexical and physical boundaries
   before reading.
4. Symbols use deterministic scan/source-file keys and preserve overload,
   generic, nested, and partial-declaration identity.
5. Semantic relationships cover in-scan type references, calls, inheritance,
   and interface implementation. External targets are omitted rather than
   assigned fabricated IDs.
6. Findings cover TODO/FIXME comments, long methods, excessive parameters,
   empty catches, and optional compiler/syntax diagnostics. Rule IDs and
   thresholds are request options.

`AnalysisFileInput` is an Application contract. The current service reads the
discovered C# inputs into one analysis request and accepts cancellation and
progress; streaming/chunked loading remains future work for very large roots.

## Existing data model

- `Project` identifies the root and stores type, favorite, last-opened, and last-scanned timestamps.
- `Scan` owns one execution and records status plus `FilesDiscovered` and `FilesProcessed`. The Phase 1 completed status means metadata discovery completed, not semantic analysis.
- `SourceFile` owns relative path, filename, extension, optional small-file hash, size, last modification, language, and discovery status. It does not store content.
- `Symbol`, `Dependency`, and `Finding` tables and EF relationships are reused
  by Phase 2. They are empty immediately after onboarding and are populated by
  a completed analysis run.

The implementation explicitly adds analysis status/count/timestamp fields to
`Scan`; it does not change the meaning of discovery `ScanStatus.Completed`.

## Database extension guidance

- Migration `20261016080000_AddAnalysisState` adds analysis fields and the
  `(ProjectId, AnalysisStatus)` index; do not edit `InitialCreate` after it has
  been applied.
- Prefer additional analysis status/count fields on `Scan` only if the distinction between discovery and semantic analysis is explicit.
- Add indexes for the actual query patterns used by Code Explorer and dependency views, such as `(ScanId, SourceFileId, Kind)` and `(ScanId, TargetSymbolId)`.
- Preserve cascade rules so deleting a project removes all of its scan-owned analysis data.
- Keep enum conversions stable as strings unless there is a measured reason to change.

## Current implementation and next checks

1. Run `make restore`, `make build`, and `make test` with the locked package
   graph when the sandbox is available.
2. Run `make migrate-fresh` to smoke-test the checked-in analysis migration.
3. Keep workspace fallback messaging visible if the host lacks a compatible
   MSBuild installation.
4. Keep dependency and health summaries honest when external targets are
   intentionally omitted or syntax-only fallback is active.

## Known technical debt

- The current UI has no scan-history detail page or dedicated retry action,
  although Start can rerun a completed discovery scan and failed/cancelled
  records are preserved.
- `ProjectOnboardingService` runs one metadata discovery pass each time a project is opened; a later phase can add incremental discovery based on `ContentHash` and timestamps.
- SQLite recent ordering is client-side because EF Core SQLite does not translate `DateTimeOffset` ordering. If recent-project volume grows materially, add a normalized numeric sort column in a migration.
- WPF visual and accessibility checks still require Windows. Do not infer them from the Linux build.
- Locked restore, compilation, integration tests, and migration smoke checks
  were blocked in the current session because the OmniRush sandbox was
  unreachable before process startup. See `PHASE2_IMPLEMENTATION.md` for the
  exact error and honest verification status.

## Phase 2 WPF integration

The desktop composition root registers `IProjectAnalysisService` with the
`PersistedProjectAnalysisService` adapter in `ForgeDesk.App`. The adapter
creates a short-lived DI scope, verifies that the selected `ProjectId` owns the
selected completed scan, and invokes the existing application `IAnalysisService`
with a mapped progress callback and cancellation token. It then reads the
actual `Symbols`, `Dependencies`, and `Findings` rows from that scan. The
long-lived WPF viewmodel never owns an EF context.

`AnalysisViewModel` is shared by Code Explorer, Dependencies, and Code Health.
Opening a project selects its persisted `ProjectDto` and scan context. Start
analysis, Refresh, and Cancel are asynchronous MVVM commands. Start invokes
the real core analysis service; when it completes, fails, or is cancelled, the
adapter reloads the persisted status/counts/findings. Findings show the stored
`SourceFile.RelativePath` with the stored line range. The UI deliberately does
not turn a finding into a fake editor/navigation action or display an absolute
path.

The view distinguishes the following states:

- no project selected;
- loading or cancelled refresh/analysis;
- empty data when no scan or no analysis rows exist;
- ready data from a completed scan;
- partial data when the latest scan is incomplete but rows exist; and
- an error when persistence cannot be read or discovery/analysis failed without results;
- a retryable failed/cancelled analysis status when a completed discovery scan remains available.

The UI integration targets the selected `ProjectId` and its completed scan;
the adapter does not create a scan, alter migrations, or fabricate rows. An
onboarded project whose analysis has not started is shown as empty with a
Start analysis action, and a run with no persisted rows remains visibly empty
or partial according to the persisted analysis status.
