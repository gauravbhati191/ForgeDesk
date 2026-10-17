# Phase 3 implementation

Phase 3 adds a persisted dependency-graph workspace, issue explorer, and
read-only source preview to the existing WPF shell. The implementation keeps
analysis facts in SQLite and keeps filtering, layout, selection, and viewport
state in memory.

## Architecture

```text
AnalysisViewModel
  -> PersistedProjectAnalysisService (short-lived EF scope)
  -> ProjectAnalysisSnapshot
       symbols, dependencies, findings, source-file metadata

DependencyGraphViewModel
  -> PersistedProjectGraphService (short-lived EF/application scope)
  -> Application IGraphQueryService
  -> ForgeDesk.Graph IGraphDataProvider / GraphDataBuilder / layout
  -> native DependencyGraphCanvas (WPF DrawingContext)

IssueExplorerViewModel
  -> snapshot findings
  -> filters and deterministic sorting
  -> shared SourceViewerViewModel

SourceViewerViewModel
  -> SafeSourceNavigationService
  -> validated relative path + bounded read-only preview
```

The graph view consumes the public `ForgeDesk.Graph` query contracts. The WPF
canvas is deliberately a single drawing surface rather than thousands of
`Shape` controls. The app adapter creates a fresh DI scope for each graph
query, just as the Phase 2 analysis adapter does for persisted analysis reads.

## Implementation plan

Phase 3 was implemented in this order:

1. verify the Phase 2 persisted symbol, dependency, finding, and scan-state
   boundaries;
2. add scan-scoped, no-tracking graph reads and graph contracts;
3. project real rows through filtering, selection, data-quality states, and a
   deterministic in-memory layout;
4. connect a native WPF canvas and symbol details to the existing navigation;
5. add persisted-finding filtering and cross-navigation to graph/source views;
6. add safe relative-path validation, bounded source preview, tests, and the
   manual Windows verification checklist below.

No new analysis facts or graph layout data are persisted by Phase 3.

## Graph semantics

- Nodes are persisted `Symbol` rows. Their stable identity is the persisted
  symbol ID; display names are never used as IDs.
- Edges are persisted `Dependency` rows with source/target symbol IDs and the
  stored relationship text. The graph core collapses duplicate
  source/target/relationship rows and excludes self-edges by its documented
  default policy. Rows with missing or external endpoints are omitted from the
  rendered graph and counted as unresolved/partial data.
- Node metadata includes symbol kind, symbol key/qualified identity, relative
  source path, declaration line range, incoming/outgoing counts, and related
  finding count.
- Coordinates come from the graph layer's deterministic in-memory layout. No
  layout or selection state is written to the analysis database.
- The canvas supports node and edge selection, direct-neighbor highlighting,
  directional arrowheads, tooltips, mouse-drag panning, wheel zooming, Fit,
  and Reset. Labels are suppressed at very small zoom levels to keep dense
  graphs readable.

## Graph filtering

The dependency page supports:

- case-insensitive search over symbol name, qualified identity, and relative
  source path;
- symbol-kind and relationship-type filters;
- incoming, outgoing, or both-direction selection-scoped filtering;
- a bounded neighborhood depth around a selected node (`0` means no depth
  limit);
- show/hide isolated nodes;
- visible versus total node/edge counts;
- clear filters and refresh actions.

The core graph query returns explicit Missing, Empty, Ready, Partial, Stale,
and Failed states. The WPF view preserves those states rather than displaying
placeholder nodes or counts.

## Issue explorer

The Code Health page uses the findings loaded for the selected scan. It supports
search across message, rule ID, relative path, and associated symbol; severity,
rule, and source-file filters; and sorting by severity, file, rule, or source
line. Every row shows severity text, rule ID, message, relative path/line, and
associated symbol when a `SymbolId` was persisted. Total and filtered counts
are separate. A finding without a symbol still supports source preview when a
valid source location exists, but its graph-navigation command remains
disabled.

Selecting a finding can open its source preview or select its associated graph
node. Selecting a graph symbol exposes related finding counts and can switch
to the issue explorer. The shared source-preview state remains local to the
desktop process.

## Source navigation and preview

`SafeSourceNavigationService` accepts only a stored relative path and the
persisted project root. It:

1. rejects rooted Windows/Unix paths and invalid path text;
2. normalizes both slash styles;
3. resolves the candidate path and checks the lexical root boundary;
4. verifies the file exists and checks the final physical path boundary for
   links/reparse points;
5. reads the file as text without executing it; and
6. returns a read-only, line-numbered preview, bounded to 2,000 lines around
   the requested declaration when a file is larger.

The preview highlights the requested declaration line, shows the normalized
relative path, and supports search over the loaded preview. Missing files,
permission errors, traversal, and root escapes produce visible error states.
The app does not claim external-editor cursor integration in this phase.

## Cancellation and stale operations

Analysis loading, graph queries, finding filtering, and source loading cancel
the previous operation before starting a newer one. Each presentation
operation also carries a monotonically increasing version check, so a slower
response from an earlier project, scan, selection, or filter cannot overwrite
newer UI state.

## Manual Windows UI verification

On a Windows machine with the pinned .NET SDK:

1. Run `dotnet restore ForgeDesk.sln --locked-mode` and `dotnet build
   ForgeDesk.sln --configuration Release`.
2. Start ForgeDesk, open an onboarded C# project, and run semantic analysis.
3. Open **Dependencies**. Confirm the visible/total counts match the selected
   persisted scan, then search for a symbol, select a node, select an edge,
   use incoming/outgoing/depth/isolation filters, drag the canvas, zoom, Fit,
   and Reset.
4. Use **Open source preview** from a node and confirm the relative path and
   declaration line. Move/delete the file, refresh, and confirm the explicit
   missing-file error.
5. Open **Code Health**. Verify severity/rule/file filters, each sort mode,
   filtered/total counts, findings without `SymbolId`, source preview, and
   finding-to-graph navigation.
6. Switch between dark and light themes and resize the window. Confirm labels,
   severity text, focus states, and empty/partial/error messages remain
   readable.

## Commands and known limitations

The canonical checks remain `make build` and `make test` (or their direct
`dotnet` equivalents in `docs/PHASE2_IMPLEMENTATION.md`). WPF rendering and
accessibility still require Windows for manual verification. The graph query
uses the current selected completed scan; scan-history comparison, Git, AI,
and external-editor integration remain Phase 4 or later work.

## Phase 4 handoff

Phase 4 can reuse `ProjectAnalysisSnapshot`, `IProjectGraphService`, and the
graph public contracts to add read-only Git context, report exports, and
analysis-run comparisons. Keep report generation scan-scoped, keep source
paths relative by default, and preserve the graph's explicit partial/stale
state in exported summaries.
