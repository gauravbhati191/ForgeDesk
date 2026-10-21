# ForgeDesk — Phase 4 Implementation Prompt
## Git Integration, Professional Reports, and Developer Workflow Improvements

Act as a senior .NET architect, WPF application engineer, Git integration specialist, and software quality engineer.

Implement Phase 4 of my existing ForgeDesk application. Extend the existing Phase 1 foundation, Phase 2 Roslyn analysis engine, and Phase 3 graph and issue exploration features.

Do not create a new application, replace the architecture, or remove existing functionality.

The goal is to make ForgeDesk more useful for real development workflows by adding repository awareness, analysis reporting, comparison capabilities where supported by the existing data, and polished usability improvements.

Inspect the actual repository before making changes. Do not assume that a feature described in this prompt has already been implemented correctly.

---

# 1. Existing Solution and Technology

The solution contains nine projects:

- ForgeDesk.App
- ForgeDesk.Presentation
- ForgeDesk.Application
- ForgeDesk.Domain
- ForgeDesk.Infrastructure
- ForgeDesk.Analysis
- ForgeDesk.Graph
- ForgeDesk.UnitTests
- ForgeDesk.IntegrationTests

The existing technology stack includes:

- C# and .NET 8, subject to the repository's current target framework
- WPF
- MVVM with CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting and dependency injection
- SQLite and Entity Framework Core
- Roslyn-based C# analysis
- Existing project onboarding, scan lifecycle, themes, logging, graph, and issue explorer

Respect existing package versions, project boundaries, coding conventions, database models, and dependency-injection registrations.

Before coding:

1. Inspect the entire solution.
2. Review Phase 1, Phase 2, and Phase 3 implementations.
3. Run the existing build and full test suite.
4. Verify Git status and preserve all existing user changes.
5. Identify which features are implemented and which are incomplete.
6. Document a concise plan and then implement the work.

Do not overwrite user modifications or reset the repository to a clean state.

# 2. Phase 4 Objectives

Implement these capabilities:

A. Git repository detection and status inspection.
B. Commit history and change awareness.
C. Git-aware project navigation and contextual analysis where supported.
D. Exportable analysis reports.
E. Report summaries, findings, severity counts, and dependency statistics.
F. Comparison of previous and current analysis results where reliable.
G. Improved usability, empty states, error handling, and long-running-operation feedback.
H. Testing, documentation, and release-readiness improvements.

Do not implement cloud synchronization or mandatory remote services. Do not add AI integration in this phase.

# 3. Git Integration Architecture

Use a well-maintained .NET-compatible Git library, preferably LibGit2Sharp if compatible with the existing target and platform, or another justified library.

Inspect the existing package constraints before selecting dependencies.

Create or extend suitable abstractions, such as:

- IGitRepositoryService
- GitRepositoryInfo
- GitStatusSnapshot
- GitFileChange
- GitCommitInfo
- GitBranchInfo
- GitOperationResult

Adapt these names to the existing architecture rather than duplicating existing contracts.

Keep Git operations outside WPF code-behind. Use application-layer services and dependency injection.

Support repositories that contain a `.git` directory or worktree metadata. Handle repository discovery from a selected project root and its parent directories without accidentally selecting an unrelated repository.

# 4. Git Repository Information

For an onboarded project, display Git information when a repository is detected:

- Repository root
- Current branch
- Current commit identifier
- Current commit message and author, where available
- Latest commit date
- Working-tree status
- Staged changes
- Unstaged changes
- Untracked files
- Repository detection errors or unsupported repository states

Clearly distinguish:

- A project that is not a Git repository
- A valid clean repository
- A repository with pending changes
- A repository that cannot be read

Do not show fabricated branch names, commits, or status values.

Git information must be optional. Projects that are not Git repositories must continue to work normally.

Do not require GitHub credentials or remote access to inspect local repository information.

# 5. Git Status and Change Explorer

Implement a useful Git change view showing files grouped by status.

Where the selected Git library supports it reliably, distinguish:

- Added files
- Modified files
- Deleted files
- Renamed files
- Untracked files
- Staged changes
- Unstaged changes
- Conflicts

Show file paths relative to the repository root.

Provide a refresh action and clear loading/error states.

If diff display is implemented, show a readable text diff for supported files and handle binary or unavailable diffs explicitly.

Do not modify, stage, commit, reset, checkout, merge, or delete user files automatically.

Phase 4 is primarily for repository inspection. Any future Git mutation features must be explicit, confirmed, and separately tested.

# 6. Commit History

Add a commit-history view for the selected repository.

Support:

- Recent commits with configurable limits
- Commit identifier
- Commit message
- Author name
- Commit date
- Parent relationships where available
- Files changed when supported
- Refreshing history after local repository changes

Allow a user to inspect a commit and see its associated file changes where practical.

Keep history retrieval bounded so that opening a large repository does not freeze the UI.

Do not assume that commit timestamps are local time. Convert and label dates consistently.

Do not require the repository to have a remote or internet access.

If history is unavailable, explain why instead of presenting fake history.

# 7. Git-Aware Analysis Context

Connect Git information to the existing analysis system where technically justified.

Provide a clear indication of the Git commit or working-tree state associated with an analysis run when the necessary information is available.

If the existing analysis model can support it without breaking previous behavior, store optional metadata such as:

- Commit identifier
- Branch name
- Whether the working tree had changes
- Analysis timestamp

Add a new EF Core migration if schema changes are needed. Do not modify the original InitialCreate migration.

Do not claim an analysis represents a particular commit if the working tree was modified and the exact analyzed state was not captured.

Do not automatically rerun analysis every time Git status changes.

If practical, allow the user to start a new analysis manually and display its Git context.

Keep Git metadata optional for projects without repositories.

# 8. Analysis Report Generation

Implement professional reports using the actual persisted analysis data.

Support at least one well-structured export format and a second format where practical.

Recommended formats:

- HTML report
- JSON export

Optional additional format:

- CSV export of findings

Prefer HTML and JSON first because they can represent summary information and structured findings more completely.

Create appropriate report contracts and services, such as:

- IAnalysisReportService
- AnalysisReportModel
- AnalysisReportSummary
- AnalysisReportFinding
- AnalysisReportSymbol
- AnalysisReportDependency

Use names compatible with existing project conventions.

The report must be generated from a specific project and analysis run, not from arbitrary global data.

# 9. Report Contents

Include the information that actually exists in the database:

- Application and report version
- Project name and root metadata, without unnecessarily exposing private absolute paths
- Analysis run identifier
- Analysis timestamp
- Analysis status and limitations
- Number of source files analyzed
- Symbol count
- Dependency count
- Finding count
- Findings grouped by severity
- Findings grouped by rule where practical
- Findings grouped by source file
- Relevant source paths and line numbers
- Supported dependency statistics
- Analysis errors or incomplete-analysis warnings
- Git commit metadata when captured reliably

Do not calculate metrics from incomplete or unrelated scans.

Distinguish missing data from a genuine zero count.

Do not expose source-code contents, secrets, credentials, or sensitive absolute paths by default.

Include an optional setting for absolute paths only if there is a clear legitimate use case and the privacy implications are explained.

# 10. HTML Report Requirements

Generate a self-contained HTML report that can be opened in a normal browser.

Requirements:

- Professional, clean layout
- Summary cards or sections
- Clear severity labels
- Findings table with sorting or filtering where practical
- Source file and line information
- Responsive layout
- Readable light theme
- Print-friendly styles
- Clear project and analysis metadata
- Explicit limitations and incomplete-analysis warnings

Escape all untrusted text correctly when generating HTML. Treat source file names, messages, rule descriptions, commit messages, and other repository-derived strings as untrusted content.

Do not inject raw analysis messages into executable HTML or JavaScript.

Avoid remote scripts, remote fonts, and external resources so the report remains self-contained and local-first.

If JavaScript is used for sorting or filtering, keep it local and minimal.

# 11. JSON and CSV Export

JSON export should use stable property names and a documented schema.

Include:

- Schema version
- Project and analysis metadata
- Summary statistics
- Findings
- Relevant source locations
- Dependency summary
- Git context when available

Use deterministic serialization where practical.

CSV export, if implemented, should focus on findings and include useful columns such as severity, rule ID, message, file, start line, end line, and symbol.

Correctly escape commas, quotes, and line breaks in CSV fields.

Exported files must contain actual results, not sample data.

Use safe filenames and prevent accidental overwriting of unrelated files. Let the user select an output path through an appropriate Windows file dialog.

Handle cancellation, permission errors, invalid paths, and disk failures.

# 12. Report Comparison

Implement comparison of two analysis runs when reliable historical data exists.

Compare:

- Total findings
- Findings by severity
- Findings by rule
- Findings by source file
- Newly introduced findings
- Findings no longer present
- Findings that remain

Use stable finding fingerprints based on appropriate fields such as rule ID, normalized relative path, symbol identity when available, and message or rule-specific location.

Document the matching strategy and its limitations.

Do not use database-generated IDs alone to compare findings across runs.

Avoid falsely labeling a finding as fixed merely because its source file moved, line number changed, or analysis configuration differed.

If the data does not support a reliable comparison, report that limitation instead of inventing a trend.

Keep comparison read-only.

# 13. Usability Improvements

Review the existing application from a developer's perspective.

Improve only where there is a demonstrated usability issue.

Prioritize:

- Clear navigation between Projects, Graph, Issues, Git, Reports, and Settings
- Consistent light and dark themes
- Loading indicators and cancellation where appropriate
- Useful empty and error states
- Clear confirmation for potentially destructive actions already present in the application
- Consistent spacing, typography, labels, and keyboard behavior
- Useful tooltips and accessible control names
- Search and filter consistency
- Large-project responsiveness
- Clear distinction between discovery and analysis status
- Friendly messages that include an actionable next step

Do not redesign every screen without justification.

Do not remove useful information just to make the UI look simpler.

Avoid fake metrics, decorative dashboards that show no real data, and nonfunctional buttons.

# 14. Logging and Error Handling

Use the existing logging infrastructure.

Record meaningful events for:

- Git repository detection
- Git status refresh
- History loading
- Report generation
- Export success or failure
- Analysis-run comparison

Include relevant project and run identifiers when useful.

Do not log credentials, secrets, complete source contents, or unnecessary personal information.

Handle malformed repositories, inaccessible directories, corrupt Git metadata, missing history, file permission errors, and cancellation gracefully.

Errors must be actionable and must not crash the whole application.

# 15. Security and Local-First Requirements

- Do not require GitHub or other remote credentials.
- Do not push or pull from remotes automatically.
- Do not run arbitrary repository scripts.
- Do not automatically stage, commit, reset, or delete files.
- Do not execute source code being analyzed.
- Validate file paths before reading or exporting files.
- HTML-escape all repository-derived content.
- Use safe JSON serialization.
- Avoid path traversal in report export and source-file links.
- Do not add telemetry, cloud storage, or mandatory external APIs.
- Keep all analysis data local unless a user explicitly exports a report.

Treat repository content as untrusted input.

# 16. Testing Requirements

Extend ForgeDesk.UnitTests and ForgeDesk.IntegrationTests.

Preserve all Phase 1, Phase 2, and Phase 3 tests.

Add tests for:

Git:
- Repository discovery
- Valid repository with no commits
- Clean working tree
- Modified and untracked files
- Staged and unstaged changes
- Missing repository
- Invalid or inaccessible repository
- Bounded commit history
- Repositories without remotes

Reports:
- Accurate counts for the selected run
- Correct grouping by severity and rule
- Empty reports
- Incomplete-analysis warnings
- JSON schema and serialization
- HTML escaping of untrusted messages
- CSV escaping if CSV is implemented
- Safe export filenames
- Output cancellation and error handling

Comparison:
- Identical analysis runs
- New findings
- Removed findings
- Findings with changed line numbers
- Findings with changed file paths
- Duplicate messages
- Missing or incompatible analysis metadata

Integration:
- Git metadata associated with a project
- Generating reports from persisted SQLite data
- Reopening exported reports
- Comparison of persisted analysis runs
- Preserving existing data across migrations

Use temporary Git repositories, temporary directories, and temporary SQLite databases.

Tests must not modify the developer's actual repository, real database, or user files.

Do not rely on internet access.

# 17. Documentation

Add or update documentation explaining:

- Git integration architecture
- Supported repository states
- Commit history and file changes
- Git metadata stored with analysis runs
- Report formats and their schemas
- Report privacy behavior
- How to export reports
- How analysis comparison works
- Test commands
- Known limitations
- Manual Windows verification
- Phase 5 handoff

Document any third-party packages added and their licenses.

# 18. Definition of Done

Phase 4 is complete only when:

1. The existing solution builds successfully.
2. All previous phase tests still pass.
3. Git repository detection and status work with real local repositories.
4. Projects without Git continue working normally.
5. Commit history is real and bounded.
6. Reports are generated from actual persisted analysis runs.
7. HTML output is safe, self-contained, and readable.
8. JSON output is valid and documented.
9. Report comparison is implemented correctly or explicitly deferred with a documented reason.
10. The UI remains responsive during Git queries and report generation.
11. All exports handle cancellation and errors.
12. No automatic Git mutations occur.
13. No fake results or placeholder functionality remain in the delivered features.
14. Documentation and an honest final implementation report are provided.

# 19. Required Implementation Workflow

Step A — Inspect the repository and establish the baseline.

Step B — Verify the completeness of the Phase 2 and Phase 3 data models.

Step C — Implement Git repository detection and read-only status services.

Step D — Implement bounded commit history and change inspection.

Step E — Connect optional Git metadata to analysis runs where supported.

Step F — Implement report contracts and HTML generation.

Step G — Implement JSON export and optional CSV export.

Step H — Implement analysis-run comparison if the existing data supports it reliably.

Step I — Integrate the new features into the existing WPF UI.

Step J — Add unit and integration tests.

Step K — Run the full test suite, review security, and perform manual Windows verification.

Step L — Provide a final implementation report with files changed, dependencies added, migrations, commands executed, actual test results, limitations, and usage instructions.

Do not claim tests passed unless they were actually executed.

If a Windows-specific feature cannot be verified in the current environment, state that limitation explicitly.

Do not replace implementation with pseudocode, TODOs, static mock data, or decorative UI.

# Final Instruction

Implement Phase 4 directly in the existing ForgeDesk repository. Preserve all working functionality, favor read-only Git operations and safe report generation, and deliver a tested developer workflow that builds on genuine Phase 2 and Phase 3 results.