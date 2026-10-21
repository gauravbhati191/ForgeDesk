# ForgeDesk — Phase 3 Implementation Prompt
## Interactive Dependency Graph, Issue Explorer, and Source Navigation

Act as a senior .NET architect, WPF UI engineer, graph-visualization specialist, and software-analysis engineer.

Implement Phase 3 of my existing project, ForgeDesk. Build on the existing Phase 1 foundation and Phase 2 analysis engine. Do not create a new application, replace the architecture, or discard working features.

The goal is to transform persisted C# analysis results into a useful, interactive desktop developer workspace.

---

# 1. Existing Project Architecture

The solution contains these nine projects:

- ForgeDesk.App
- ForgeDesk.Presentation
- ForgeDesk.Application
- ForgeDesk.Domain
- ForgeDesk.Infrastructure
- ForgeDesk.Analysis
- ForgeDesk.Graph
- ForgeDesk.UnitTests
- ForgeDesk.IntegrationTests

Technology stack:

- C# and .NET 8, subject to the existing repository configuration
- WPF desktop UI
- MVVM with CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting and dependency injection
- SQLite and Entity Framework Core
- Roslyn-based C# analysis
- Existing structured logging, themes, project onboarding, and scan lifecycle

Inspect the actual repository before implementing anything. Verify the current code, contracts, package versions, database schema, migrations, tests, and Phase 2 implementation. Do not assume that an interface or feature exists merely because it is described in this prompt.

Preserve the existing architecture and coding conventions.

# 2. Mandatory Preflight

Before coding:

1. Inspect the full solution and its project references.
2. Review the Phase 1 onboarding and persistence implementation.
3. Review the Phase 2 symbol extraction, dependency extraction, finding generation, and analysis lifecycle.
4. Inspect the existing Symbol, Dependency, Finding, Project, SourceFile, and Scan models.
5. Verify how analysis results are associated with a particular scan or analysis run.
6. Run the current build and all existing tests.
7. Identify missing prerequisites and document the baseline.
8. Create a short implementation plan, then proceed with implementation.

If Phase 2 is incomplete, do not invent dependency records or quality findings. Integrate with the available real data and clearly report missing prerequisites. Implement reusable components and tests where possible, but do not claim full Phase 3 completion until real persisted analysis data can power the UI.

Do not stop after producing the plan.

# 3. Phase 3 Objectives

Implement the following capabilities:

A. Interactive dependency graph.
B. Searchable and filterable graph.
C. Symbol and dependency details.
D. Issue explorer for persisted code-quality findings.
E. Source-file navigation with line-level selection where practical.
F. Connections between graph nodes, source files, and findings.
G. Loading, empty, error, and cancellation states.
H. Unit and integration tests.
I. Documentation for the new graph and navigation features.

Do not implement Git integration, AI explanations, cloud synchronization, or advanced impact analysis in this phase. These belong to later phases.

# 4. Dependency Graph Architecture

Implement the graph functionality inside the existing ForgeDesk.Graph project, reusing its current abstractions where possible.

Create or extend appropriate contracts, such as:

- IGraphDataProvider
- IGraphLayoutService
- GraphNode
- GraphEdge
- GraphData
- GraphQueryOptions
- GraphSelection
- GraphFilterOptions

These are suggested names, not requirements to duplicate existing contracts.

Separate graph data retrieval, graph transformation, layout calculation, and WPF rendering.

The graph must be built from actual persisted Symbol and Dependency records for the selected analysis run.

Each node should represent a meaningful code symbol, such as a class, interface, method, or other supported symbol type.

Each edge should represent a supported dependency relationship from Phase 2.

Include relevant metadata:

- Stable node identifier
- Display name
- Symbol kind
- Containing namespace or type, if available
- Source-file relative path
- Start and end line
- Dependency relationship type
- Analysis or scan identifier

Use the existing database IDs or a consistent stable identity strategy. Do not use display names alone as identifiers because different namespaces, types, overloads, and projects may contain symbols with the same name.

# 5. Graph Layout and Rendering

Implement a readable interactive graph inside the existing WPF application.

Evaluate suitable WPF-compatible graph-rendering approaches and choose one that works with the current .NET target and package constraints. Prefer a maintained library if it meets the requirements; otherwise, build a carefully encapsulated WPF visualization using appropriate drawing and interaction primitives.

Do not introduce an incompatible framework or a second application UI stack.

The graph should support:

- Pan and zoom
- Fit graph to view
- Reset view
- Node selection
- Visual distinction between symbol kinds
- Directional dependency edges
- Tooltips or details on hover
- Selection highlighting
- Clear handling of large or dense graphs
- Readable labels at appropriate zoom levels
- Empty-state messaging when no dependencies exist

Use a consistent visual language that fits ForgeDesk's existing theme system.

Support both light and dark themes where practical.

Avoid excessive visual decoration. Prioritize legibility, navigation, and useful information.

Do not render thousands of WPF controls without considering performance. Use a suitable rendering strategy and virtualization, filtering, or progressive loading where necessary.

# 6. Graph Interaction

Implement the following interactions:

1. Clicking a node selects it and displays its details.
2. Selecting a node highlights its direct incoming and outgoing dependencies.
3. Selecting an edge displays its relationship type and source/target symbols.
4. A node action opens its source file at the relevant declaration.
5. A finding associated with a symbol can navigate to that symbol in the graph.
6. Search results can select and focus the matching graph node.
7. Filters update the displayed graph without corrupting the underlying persisted analysis data.
8. A clear selection action resets the selection state.
9. A refresh action reloads data for the selected analysis run.

For large graphs, add configurable depth or neighborhood expansion. Start with a useful bounded view around the selected node rather than always rendering every symbol in the repository.

For example, selecting a class may show the class and its direct dependencies first. Users can expand its neighborhood when needed.

Do not invent dependencies or infer relationships that are not supported by the stored analysis results.

# 7. Graph Search and Filtering

Implement useful graph controls:

- Search by symbol name
- Filter by symbol kind
- Filter by dependency relationship type
- Show incoming dependencies
- Show outgoing dependencies
- Show both directions
- Limit the graph to a selected namespace or project where supported
- Show or hide isolated nodes
- Configure neighborhood depth
- Clear all filters

Search should be responsive and should not freeze the UI.

Preserve the user's current selection when a filter change leaves the selected node visible. Otherwise, clear or update selection consistently.

Display the number of visible nodes and edges, and distinguish these counts from the total persisted counts.

# 8. Symbol Details Panel

Create a details panel for the selected symbol.

Display the information actually available from the analysis data:

- Symbol name and kind
- Fully qualified name, if available
- Namespace and containing type
- Accessibility and relevant modifiers, if stored
- Source-file path
- Declaration line range
- Direct incoming dependency count
- Direct outgoing dependency count
- Related finding count

Provide actions such as:

- Open source location
- Show incoming dependencies
- Show outgoing dependencies
- Focus in graph
- View related findings

If a property is unavailable in the existing schema, omit it or add it through an appropriate migration only when justified. Do not display fabricated metadata.

# 9. Issue Explorer

Implement a dedicated issue explorer based on persisted Finding records.

The issue explorer must support:

- Displaying actual findings for the selected analysis run
- Filtering by severity
- Filtering by rule ID or rule category where available
- Filtering by source file
- Searching issue messages and file paths
- Sorting by severity, file, rule, and source line
- Displaying total and filtered counts
- Selecting a finding to open its details
- Navigating to the source location
- Navigating to the related graph symbol when SymbolId exists

Each issue row should show, where available:

- Severity
- Rule ID
- Message
- Relative file path
- Line number or line range
- Associated symbol
- Analysis run or scan

Use severity styling that remains readable in light and dark themes.

Do not use severity color as the only way to communicate meaning. Include text or icons.

Provide clear empty states for:

- No analysis has been run
- Analysis completed with no findings
- No findings match the current filters
- Analysis results could not be loaded

Distinguish these states accurately.

# 10. Source Navigation

Implement source navigation using the existing SourceFile metadata and project root path.

Create or extend a source navigation service, such as ISourceNavigationService, if needed.

Requirements:

- Resolve a source file from its stored relative path.
- Validate that the resolved path remains within the permitted project root.
- Account for Windows path separators and normalized paths.
- Reject invalid paths and paths that escape the project root.
- Verify that the file exists before opening it.
- Navigate to the requested line where supported.
- Provide useful errors when the file has moved or no longer exists.
- Never silently navigate to an unrelated file.

Support an editor integration abstraction so that a future phase can add richer editor support without redesigning the UI.

For the initial implementation, prefer the simplest reliable approach compatible with the existing application. An external editor may be used if configured and available. A basic source preview or internal read-only viewer is also acceptable if it fits the current architecture.

Do not claim exact cursor navigation if the selected editor integration cannot guarantee it.

If the file is opened externally, do not assume every editor accepts a universal line-number command. Implement and test the supported behavior explicitly.

Never execute the source code being viewed.

# 11. Source Viewer or Preview

If implementing an internal source viewer, provide:

- Read-only source text
- Line numbers
- Selected-line highlighting
- Basic syntax highlighting if practical
- Search within the file
- Clear indication of the file path
- A graceful message when a file cannot be loaded

Keep source text out of the analysis database unless an explicit existing requirement justifies storing it.

Avoid building a full code editor in Phase 3. The goal is accurate source inspection and navigation, not editing or debugging.

# 12. Navigation and UI Integration

Integrate the graph and issue explorer with the existing WPF navigation and MVVM architecture.

Use existing pages and navigation patterns where possible.

Create dedicated views and ViewModels only where justified, for example:

- DependencyGraphView
- DependencyGraphViewModel
- IssueExplorerView
- IssueExplorerViewModel
- SymbolDetailsViewModel
- SourceViewerViewModel

These names are suggestions and should be adapted to existing conventions.

Requirements:

- Use MVVM commands and observable state.
- Keep business logic out of code-behind.
- Register new services through the existing dependency injection composition root.
- Respect the existing theme preference and navigation.
- Keep the UI responsive during database queries, graph layout, filtering, and source loading.
- Use asynchronous operations where appropriate.
- Support cancellation for expensive graph-data loading and analysis-result queries.
- Prevent stale asynchronous results from overwriting a newer project or analysis selection.

Do not replace working Home, Projects, Settings, or Phase 2 functionality.

# 13. Data Access and Performance

Use the existing EF Core and SQLite architecture.

Create query services or repositories consistent with the existing project structure.

Requirements:

- Query results for the correct project and analysis run.
- Avoid loading unrelated projects' symbols and findings.
- Avoid unnecessary database queries for each individual graph node.
- Fetch related data in batches where practical.
- Use appropriate indexes if query performance requires them.
- Use read-only/no-tracking queries when appropriate.
- Avoid storing graph layout coordinates in the domain entities unless there is a clear persistence requirement.
- Keep temporary layout and selection state separate from analysis facts.
- Handle missing symbols or dependencies gracefully.
- Keep graph layout deterministic for identical input data where practical.

If adding database schema changes is genuinely necessary, create a new EF Core migration. Never modify the original Phase 1 InitialCreate migration or delete existing user data.

Prefer not to change the schema if the existing Phase 2 data already supports the required features.

# 14. Reliability and Security

Handle:

- Missing projects
- Missing analysis runs
- Empty symbol sets
- Empty dependency sets
- Findings without a symbol association
- Missing source files
- Invalid stored relative paths
- Duplicate or malformed graph records
- Database errors
- Cancellation
- Rapid project switching
- Large graphs
- Analysis results from older runs

Ensure that a graph is never silently presented as complete when its data is incomplete.

Display an explicit state when dependencies could not be extracted or analysis was partial.

Validate source paths before file access. Do not trust database paths blindly.

Do not execute analyzed projects or arbitrary code.

Keep the application local-first. Do not add mandatory cloud APIs, telemetry, or AI services.

# 15. Testing Requirements

Extend ForgeDesk.UnitTests and ForgeDesk.IntegrationTests.

Keep all existing Phase 1 and Phase 2 tests passing.

Add unit tests for:

- Graph construction from persisted symbols and dependencies
- Stable node identity
- Duplicate-edge handling
- Self-edge handling according to the documented policy
- Incoming and outgoing dependency filters
- Symbol-kind filters
- Relationship filters
- Search behavior
- Neighborhood-depth filtering
- Isolated nodes
- Empty graph handling
- Symbol selection and neighbor highlighting
- Issue filtering and sorting
- Findings with and without SymbolId
- Source path normalization
- Path traversal prevention
- Missing source files
- Cancellation and stale-result handling

Add integration tests for:

- Loading real persisted Phase 2 results
- Building graph data for the correct analysis run
- Loading issues from SQLite
- Finding-to-symbol navigation
- Symbol-to-source navigation metadata
- Multiple projects with overlapping symbol names
- Empty and partial analysis results
- Preserving existing project, scan, symbol, dependency, and finding records
- Applying any new migration safely

Use temporary directories and temporary databases. Do not alter the developer's actual ForgeDesk database or external projects during tests.

Do not make tests dependent on network access.

For UI functionality that cannot be exercised by automated tests, document a manual verification procedure.

# 16. Documentation

Add or update documentation covering:

- Phase 3 architecture
- Graph node and edge semantics
- Supported dependency relationships
- Search and filtering behavior
- Issue explorer behavior
- Source navigation behavior and limitations
- Performance considerations
- Test commands
- Manual Windows UI verification
- Known limitations
- The Phase 4 handoff

# 17. Definition of Done

Phase 3 is complete only when:

1. The existing solution builds on the supported Windows/.NET environment.
2. All Phase 1 and Phase 2 tests continue to pass.
3. New unit and integration tests pass.
4. The dependency graph displays real persisted symbol and dependency data.
5. Pan, zoom, selection, and graph filtering work.
6. Selecting a symbol reveals its real details and supported relationships.
7. The issue explorer displays real persisted findings.
8. Findings can be searched, filtered, and sorted.
9. Finding-to-symbol navigation works when a valid symbol association exists.
10. Source paths are validated and source navigation behaves as documented.
11. Empty, missing, partial, and failed data states are distinguished correctly.
12. Large graph behavior is bounded and the UI remains responsive.
13. Light and dark themes remain functional.
14. Existing Phase 1 and Phase 2 behavior is preserved.
15. No fake graph nodes, hardcoded findings, or placeholder counts are used.
16. Documentation and a final implementation report are provided.

# 18. Required Execution Workflow

Implement in this order:

Step A: Inspect the repository and establish a build/test baseline.

Step B: Verify Phase 2 data contracts and confirm real symbols, dependencies, and findings can be queried.

Step C: Implement graph data contracts and query services.

Step D: Implement graph filtering, neighborhood queries, and deterministic layout.

Step E: Implement interactive WPF graph rendering and symbol details.

Step F: Implement the issue explorer and finding filters.

Step G: Implement safe source navigation and the initial source preview or editor integration.

Step H: Connect graph, issues, symbols, and source navigation.

Step I: Add tests and any necessary migrations.

Step J: Run the full test suite and perform a manual Windows UI verification.

Step K: Write a final report describing files changed, architecture decisions, commands executed, actual test results, known limitations, and how to use the new features.

Do not claim a command succeeded unless you actually ran it and observed the result. If the environment prevents a Windows-only test, identify it clearly.

Do not stop at pseudocode, TODO comments, static mock data, or a visual-only prototype.

# Final instruction

Implement Phase 3 directly in the existing ForgeDesk repository. Preserve the existing architecture, build on actual Phase 2 results, prioritize correctness and usability, and finish with a tested, integrated feature set rather than a superficial UI mockup.