# Phase 2 implementation

## Status and scope

Phase 2 adds a persisted semantic-analysis stage after metadata discovery. It
uses Roslyn for C# symbols and in-scan relationships, deterministic quality
rules, analysis lifecycle state, and the shared WPF analysis surface. Discovery
and analysis remain separate stages of the same `Scan` record.

The implementation is intentionally local-first. Source contents are read for
the active analysis request and are not stored in SQLite.

## Implementation plan and actual components

| Area | Implementation |
|---|---|
| Domain lifecycle | `ForgeDesk.Domain/Entities/Scan.cs`, `AnalysisStatus.cs` keep discovery `ScanStatus` separate from analysis `NotStarted`, `Running`, `Completed`, `Failed`, and `Cancelled`. |
| Application contracts | `ForgeDesk.Application/Contracts/AnalysisContracts.cs` defines source inputs, quality options, binding mode, symbols, dependencies, findings, progress, repository, engine, and service contracts. `AnalysisRunDto.cs` returns status/counts and the current binding result. |
| Application orchestration | `ForgeDesk.Application/Services/AnalysisService.cs` validates a completed discovery scan, safely reads discovered C# files, starts analysis, clears stale rows, invokes the engine, maps results, and persists terminal state. |
| Roslyn adapter | `ForgeDesk.Analysis/RoslynAnalysisEngine.cs` loads MSBuild projects/solutions when possible, otherwise reports `SyntaxOnlyFallback`. It emits deterministic symbols, in-scan relationships, quality findings, and optional compiler diagnostics. |
| Persistence | `ForgeDesk.Infrastructure/Repositories/AnalysisRepository.cs` loads analysis context, clears old rows, and transactionally replaces symbols, dependencies, and findings. Infrastructure DI registers the application engine and service. |
| Desktop flow | `ForgeDesk.App/Services/PersistedProjectAnalysisService.cs` creates short-lived scopes, invokes the application service, and reloads persisted rows. `AnalysisViewModel` exposes progress, Start, Refresh, Cancel, empty, partial, failed, and cancelled states. |
| Database | Migration `ForgeDesk.Infrastructure/Persistence/Migrations/20261016080000_AddAnalysisState.cs` adds analysis status, timestamps, error, counts, and the `(ProjectId, AnalysisStatus)` index. |
| Packages | `ForgeDesk.Analysis.csproj` references `Microsoft.Build.Locator` `1.7.8`, `Microsoft.CodeAnalysis.CSharp.Workspaces` `4.5.0`, and `Microsoft.CodeAnalysis.Workspaces.MSBuild` `4.5.0`; affected `packages.lock.json` files are checked in. |
| Tests | `ForgeDesk.UnitTests` covers domain lifecycle rules. `ForgeDesk.IntegrationTests/AnalysisPersistenceTests.cs` covers real SQLite persistence, semantic relationships, deterministic/idempotent reruns, cancellation, syntax fallback, rule selection, stable symbols, and failed-retry cleanup. |

## Architecture and data flow

```text
WPF AnalysisViewModel
  -> ForgeDesk.App PersistedProjectAnalysisService
  -> Application IAnalysisService / AnalysisService
  -> IAnalysisRepository --------------------> Infrastructure AnalysisRepository
  -> Application IAnalysisEngine               (status/result persistence)
  -> ForgeDesk.Analysis RoslynAnalysisEngine
       -> MSBuildWorkspace + semantic models
       -> or explicit syntax-only fallback
  -> AnalysisResult
  -> Symbol / Dependency / Finding rows
  -> WPF adapter reloads the selected project's persisted scan
```

The service flow is:

1. Load the requested `Scan`, `Project`, and `SourceFile` metadata.
2. Require discovery `ScanStatus.Completed`; analysis never changes that
   discovery status.
3. Transition analysis to `Running`, persist it, and clear prior result rows.
4. Resolve every discovered `.cs` file from `Project.RootPath`, reject lexical
   traversal and physical-root escapes, and read its contents.
5. Submit an `AnalysisRequest` with source-file IDs, normalized paths, project
   root, and quality options.
6. Map and sort engine results, resolve only relationships whose symbols are in
   the same scan, and replace rows transactionally.
7. Persist `Completed` counts, or persist `Failed`/`Cancelled` without changing
   the discovery result. A retry cannot leave the previous successful rows
   looking current.

## Roslyn project loading and fallback

`RoslynAnalysisEngine` searches the registered project root deterministically,
preferring `.slnx`, then `.sln`, then `.csproj`. It registers the available
MSBuild installation through `MSBuildLocator`, creates an `MSBuildWorkspace`,
opens the solution or project, obtains C# compilations, and binds matching
discovered documents with Roslyn semantic models.

The engine reports `AnalysisBindingMode.MsBuildSemantic` when this succeeds.
Workspace diagnostics are included in the binding message and progress event.

If no workspace entry point exists, MSBuild cannot be registered, loading fails,
or no discovered source belongs to the loaded C# graph, the engine returns
`AnalysisBindingMode.SyntaxOnlyFallback` with a reason. The fallback parses the
provided source text directly. It still emits syntax declarations and syntax
quality findings, but it deliberately emits no semantic dependency edges and
does not pretend to have project references or package references. Optional
compiler diagnostics in this mode are syntax diagnostics only.

## Symbols and deterministic keys

Supported declarations include namespaces, classes, structs, interfaces,
records, enums, delegates, methods, local functions, properties, indexers,
events, enum members, fields, and variables represented by declarations.

Semantic identities use Roslyn original definitions with fully qualified
display names. Method identities include generic arity and parameter ref kinds
and types; indexed properties include parameter signatures. Syntax fallback
identities include containing namespaces/types, generic arity, overload
parameter signatures, and declaration names.

Persisted keys have this shape:

```text
scan:{Scan.Id:N}/file:{SourceFile.Id:N}/{symbol identity}
```

Keys longer than the database limit use a SHA-256 suffix. Partial declarations
remain tied to their source-file rows, while overloaded, generic, and nested
declarations retain distinct identities. Framework or other external symbols
never receive fabricated scan-local IDs.

## Dependencies and findings

Semantic dependency extraction emits:

- `TypeReference` for in-source type usage;
- `Calls` for invocations and object creation;
- `Inherits` for base classes;
- `Implements` for interfaces.

Resolved targets are mapped to symbols in the current scan. Resolved external
targets are omitted. An unresolved error type may be retained with a null target
and `IsResolved == false`; no external symbol row is invented. Syntax-only
fallback intentionally omits semantic edges.

Quality options are supplied per request:

| Rule | ID | Default/configuration |
|---|---|---|
| TODO/FIXME comments | `FD001` | Enabled by default; case-insensitive marker detection. |
| Long methods | `FD002` | Enabled by default; `MaxMethodLines` defaults to 50. |
| Excessive parameters | `FD003` | Enabled by default; `MaxParameters` defaults to 5. |
| Empty catch blocks | `FD004` | Enabled by default. |
| Compiler diagnostics | Roslyn diagnostic ID | Disabled by default; enabled with `IncludeCompilerDiagnostics`. |

`EnabledRuleIds` can restrict built-in rule execution. Findings are sorted and
deduplicated before persistence. They retain rule ID, severity, message,
source-file ID, optional symbol key, and source line range.

## Lifecycle and persistence guarantees

`Scan.Status` describes metadata discovery. `Scan.AnalysisStatus` describes the
semantic stage. Only a discovery-completed scan can enter analysis `Running`.
Starting analysis resets analysis counts and error state. The repository clears
old dependencies, findings, and symbols in a transaction before the new run.

Completed results use a transactional delete-and-reinsert replacement, making
reruns idempotent for counts and deterministic symbol keys. Failures and
cancellations persist terminal state. If a cancellation or failure occurs after
result replacement has committed, the service attempts to clear those rows
before persisting the terminal status.

## UI flow

The shared `AnalysisViewModel` is used by Code Explorer, Dependencies, and Code
Health. Opening a project selects its persisted project and completed scan.
Start invokes the real application analysis service through the App adapter;
Refresh rereads persisted rows; Cancel passes a cancellation token. The view
shows empty data when analysis has not started, partial data only when persisted
rows exist for an incomplete context, and explicit failed/cancelled states when
the completed discovery scan remains retryable. Findings show stored relative
paths and line ranges; the UI does not fabricate editor navigation or absolute
path output.

## Database and migration

The checked-in migration `20261016080000_AddAnalysisState` adds:

- `AnalysisStatus` as a stable string enum;
- `AnalysisStartedAtUtc` and `AnalysisCompletedAtUtc`;
- `AnalysisErrorMessage`;
- `AnalysisFilesProcessed`, `AnalysisSymbolCount`, and
  `AnalysisDependencyCount`;
- the existing scan finding count used for analysis summaries;
- index `(ProjectId, AnalysisStatus)`.

Existing scan-owned tables are reused. `Symbols` has unique `(ScanId,
SymbolKey)` and `(ScanId, SourceFileId, Kind)` indexes. `Dependencies` has a
unique source/target/relationship index plus `(ScanId, TargetSymbolId)`. Source
and target symbol references use `SET NULL` for unresolved/future records;
project and scan ownership continues to cascade deletes.

## Commands

The repository pins .NET SDK `8.0.416` in `global.json` and uses lockfiles.
The canonical offline-capable project checks are:

```bash
make restore       # locked restore
make build         # locked restore + Release build
make test          # build + unit and integration tests
make migrate-fresh # fresh SQLite migration check after build
make publish       # Windows win-x64 publish
```

Equivalent direct checks are:

```bash
dotnet restore ForgeDesk.sln --locked-mode
dotnet build ForgeDesk.sln --configuration Release --no-restore
dotnet test ForgeDesk.UnitTests/ForgeDesk.UnitTests.csproj --configuration Release --no-build --no-restore
dotnet test ForgeDesk.IntegrationTests/ForgeDesk.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

## Known limitations

- Project-aware loading depends on an available MSBuild installation and the
  Roslyn/MSBuild package compatibility. Unsupported or unavailable loading is a
  visible syntax-only fallback, not semantic project analysis.
- The current application service reads the discovered C# inputs into memory for
  one analysis request; streaming/chunked source loading is future work for very
  large repositories.
- Binding mode/message is returned during an analysis run and reported through
  progress; it is not stored as a separate persisted scan field.
- External framework/project symbols are intentionally not persisted as target
  rows, so cross-project edges outside the discovered scan are omitted.
- There is no scan-history detail page or dedicated retry command; a completed
  discovery scan can be analyzed again through the existing Start flow.
- WPF visual/accessibility verification still requires Windows.

## Verification status

The intended checks are `make test` or the direct commands above. During this
implementation, build/test execution was blocked before process startup by the
OmniRush sandbox:

```text
omnirush sandbox: the sandbox is not reachable
(dial tcp 127.0.0.1:44207: connectex: No connection could be made because
the target machine actively refused it.)
```

Therefore this document does not claim a passing build or test run. The source
and documentation were inspected, but the locked restore, compilation, tests,
and migration smoke check remain to be run when the sandbox is available.
