# Phase 4 analysis reports and comparison core

ForgeDesk's report core is a read-only application/infrastructure feature. It is
usable without the WPF presentation layer and reads only a caller-selected
`projectId` + `scanId` pair.

## Architecture

* `IAnalysisReportDataSource` is the application boundary for report data.
* `AnalysisReportDataSource` is the EF Core/SQLite adapter. Every query filters
  `Scans` by both project and scan identity, then reads only rows owned by that
  scan. It uses `AsNoTracking`, total row counts, and bounded `Take` queries.
* `IAnalysisReportService` is implemented by `AnalysisReportService`. It maps
  domain entities to report DTOs, computes sorted groupings and dependency
  statistics, and delegates optional Git inspection through the existing
  `IGitRepositoryService` abstraction.
* `IAnalysisComparisonService` is implemented by
  `AnalysisComparisonService`. It builds two selected reports and performs a
  read-only fingerprint comparison. No database row is changed and no UI type
  is referenced.
* `AnalysisReportSerializer` produces JSON, self-contained HTML, and findings
  CSV. `AnalysisReportFileNames` provides safe generated names and validates a
  caller-selected destination.

No EF schema change was required. The existing `Project`, `Scan`, `SourceFile`,
`Symbol`, `Dependency`, and `Finding` rows already contain all report data.
`InitialCreate` and all existing migrations remain unchanged.

## Report schema and privacy

JSON and the equivalent HTML metadata carry:

* `schemaVersion: "1.0"`, `applicationVersion`, and
  `fingerprintVersion: "1"`;
* selected project metadata and selected scan/discovery/analysis lifecycle
  status, timestamps, errors, and limitations;
* source-file metadata (never source contents), symbols, supported dependency
  rows/statistics, findings, line ranges, and sorted severity/rule/file groups;
* optional current local Git context when an `IGitRepositoryService` is
  registered.

Absolute project and repository roots are omitted by default. The report keeps
only the directory name as non-sensitive context and marks the root as
redacted. A trusted caller may set `IncludeAbsolutePaths = true`; this is an
explicit privacy decision and should be exposed by a UI only with an appropriate
explanation. Git context is labeled as captured at report time and
`IsAssociatedWithAnalysisRun` is false because the current database model does
not persist a commit with each analysis run.

For `NotStarted` analysis, symbol/dependency/finding counts are `null`, not
zero. A zero is emitted when a completed analysis genuinely has no rows. Running,
failed, or cancelled analysis is included as partial data with a limitation.
Persisted lifecycle counters and row totals are checked; a mismatch is called
out rather than hidden.

## Formats and export safety

```csharp
var report = await reportService.BuildAsync(projectId, scanId, cancellationToken: token);
await reportService.ExportAsync(
    projectId,
    scanId,
    AnalysisReportFormat.Json,
    selectedOutputPath,
    new AnalysisReportOptions { IncludeGitContext = true },
    token);
```

JSON uses indented UTF-8, camel-case property names, string enum values, and
sorted collections. The same persisted input produces the same serialization.
The stable top-level JSON shape is `schemaVersion`, `applicationVersion`,
`fingerprintVersion`, `project`, `scan`, `summary`, `sourceFiles`, `symbols`,
`dependencies`, `findings`, `limitations`, and `gitContext`. `summary` contains
`sourceFileCount`, nullable analysis counts, completeness flags, three finding
group maps, and `dependencyStatistics`; `scan` contains both discovery and
analysis statuses/timestamps/errors. New fields may be added in a later schema
version, but these names and the fingerprint version are stable for schema 1.0.
HTML contains inline CSS only: there are no remote fonts, images, scripts, or
links to source files. Every repository-derived value is HTML encoded. CSV
contains the columns `fingerprint`, `severity`, `ruleId`, `message`,
`relativePath`, `startLine`, `endLine`, and `symbol`; commas, quotes, and line
breaks are RFC-style quoted.

Exports are bounded by row limits and a maximum UTF-8 payload size. The service
writes a temporary file in the selected directory, flushes it, and moves it into
place. Existing files are rejected unless `AllowOverwrite = true`; output
directories are not created automatically. Traversal segments, invalid names,
wrong extensions, missing directories, permission failures, disk failures, and
cancellation are surfaced as actionable errors. Cancellation removes the
temporary file and does not leave a destination report behind.

`AnalysisReportFileNames.Create(projectName, scanId, format)` is useful when a
caller needs a generated name. It removes unsafe characters, handles Windows
reserved names, limits the project-name portion, and appends the format-specific
extension.

## Comparison and fingerprint strategy

`AnalysisFindingFingerprint.Create` hashes this canonical, versioned sequence:

```text
rule-id <US> normalized-relative-path <US> symbol-identity <US>
start-line <US> end-line <US> normalized-message
```

Relative paths use forward slashes and invariant casing for cross-platform
matching. Whitespace in messages is collapsed. Severity is intentionally not in
the fingerprint, so a severity-only change is reported as `Changed`.

Comparison matching is conservative and deterministic:

1. exact fingerprints are paired;
2. unmatched findings with the same rule/path/symbol/message are paired to
   identify line changes;
3. unmatched findings with the same rule/symbol/message are paired only when the
   path match is unambiguous, identifying path changes;
4. unmatched findings with the same rule/path/symbol/location are paired when
   the location is unambiguous, identifying other metadata changes;
5. remaining rows are `New` or `Removed`.

Database-generated IDs are never used as identity. Duplicate fingerprints or
ambiguous keys are not silently collapsed: occurrence order is used only where
the comparison reports an ambiguity, and otherwise rows remain new/removed.
Missing paths, lines, or symbol identities mark a match as tentative and add a
limitation. Different tool versions, incomplete analysis states, truncated
queries, and the lack of persisted analysis rule/binding configuration are also
reported. Comparison does not claim a reliable trend when those limitations
make the selected snapshots incompatible.

The result includes total baseline/current counts, unchanged/new/removed/changed
finding lists, and baseline/current deltas grouped by severity, rule, and file.

## WPF usage and manual verification

Open **Reports** after selecting a project with a persisted analysis run. Build
the selected run, verify nullable counts/limitations for incomplete analysis, and
use the native save dialog to export JSON, HTML, and CSV. Open the JSON with a
parser and the HTML in Edge; confirm source contents and absolute roots are not
included by default. Select an older run and compare it with the current run;
review reliability and limitations before interpreting new/removed/changed
findings. Try an existing destination, cancel an export, and inspect a project
without Git. No export or comparison mutates the database or repository.

## Tests and verification

The canonical offline check is:

```text
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 make test
```

`ForgeDesk.UnitTests/ReportCoreTests.cs` covers grouping/count correctness,
empty and incomplete runs, schema parsing/determinism, HTML and CSV escaping,
safe filenames, cancellation, overwrite protection, and comparison matching,
duplicates, and missing metadata. `ForgeDesk.IntegrationTests/ReportPersistenceIntegrationTests.cs`
covers SQLite migrations, scan isolation, bounded queries, JSON/HTML reopening,
persisted comparison, and DI registration.

On Windows, print the HTML to PDF, switch themes, resize the Reports page, and
confirm accessible button names and actionable empty/error messages. The report
core remains independent of WPF and never executes source code.

## Phase 5 handoff

The report page binds a selected project/scan through the App scope adapter,
shows `Limitations`, and passes a native file-dialog path to `ExportAsync`.
It binds `IAnalysisComparisonService` results and does not recreate the
fingerprint or query EF directly. If future work wants Git metadata
to be associated with a run, add an additive migration and capture it at
analysis start; do not infer a historical commit from current report-time Git
state.
