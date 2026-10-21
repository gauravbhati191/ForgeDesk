# ForgeDesk — Phase 5 Implementation Prompt
## Production Hardening, Performance, Testing, Windows Packaging, and Optional Local AI

Act as a senior .NET architect, Windows desktop application engineer, application security engineer, performance engineer, QA lead, and release engineer.

Implement Phase 5 of my existing ForgeDesk project. This is the final planned phase of the current roadmap.

Your objective is to harden the application, improve reliability and performance, complete automated testing, produce a reproducible Windows release package, document installation and troubleshooting, and optionally add a carefully isolated local AI assistance feature if the core application is already stable.

Do not create a new application or replace the existing architecture. Preserve all completed functionality from Phases 1–4.

Do not claim production readiness merely because the solution compiles.

---

# 1. Existing Project Context

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

The technology stack includes:

- C# and .NET 8, subject to the actual repository configuration
- WPF
- MVVM with CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting and dependency injection
- SQLite and Entity Framework Core
- Roslyn-based C# analysis
- Git integration
- Dependency graph visualization
- Issue explorer and source navigation
- HTML and JSON analysis reports
- Structured local logging
- Light and dark themes

Inspect the actual repository before changing anything. The implementation may differ from this roadmap, so verify the real source code, package versions, models, database schema, migrations, services, views, tests, and release configuration.

Do not assume every feature from earlier phases is complete.

# 2. Mandatory Preflight

Before implementation:

1. Inspect all nine projects and their project references.
2. Review the implementations from Phases 1–4.
3. Run the full build and existing test suite.
4. Review current warnings, failures, known defects, and incomplete features.
5. Inspect Git status and preserve uncommitted user changes.
6. Inspect database migrations and backup/recovery behavior.
7. Inspect file handling, source-path validation, HTML report generation, Git operations, and logging for security issues.
8. Review current application startup, shutdown, cancellation, and exception handling.
9. Identify actual performance bottlenecks before optimizing.
10. Document the baseline and create a prioritized implementation plan.

Proceed with implementation after producing the plan. Do not stop at a report.

If earlier phases are incomplete, prioritize fixing core functionality and its tests before adding optional features.

# 3. Phase 5 Objectives

Implement the following, in priority order:

A. Correctness and reliability hardening.
B. Security and privacy review.
C. Performance improvements based on measured bottlenecks.
D. Comprehensive automated tests.
E. Windows-specific UI and compatibility verification.
F. Safe database migration and recovery behavior.
G. Reproducible Windows release packaging.
H. Installation, usage, architecture, and troubleshooting documentation.
I. Optional local AI assistance, only after the preceding priorities are stable.
J. Final release validation and implementation report.

Do not add cloud synchronization, mandatory hosted AI, analytics telemetry, or other unrelated features.

# 4. Reliability and Application Lifecycle

Audit application startup, operation execution, and shutdown.

Ensure:

- Dependency injection registrations are valid.
- Application services have appropriate lifetimes.
- Database contexts are not shared unsafely across concurrent operations.
- Long-running tasks do not freeze the WPF UI.
- Cancellation tokens propagate to supported operations.
- Unexpected exceptions are logged and surfaced appropriately.
- Application shutdown handles active operations predictably.
- File handles, database connections, Roslyn workspaces, and Git resources are disposed correctly.
- Failed operations do not display misleading success states.
- Repeated commands do not accidentally launch conflicting duplicate operations.
- Rapid project switching does not allow stale results to overwrite current UI state.
- Empty, loading, partial, failed, cancelled, and successful states remain distinguishable.

Review all asynchronous commands and avoid unobserved task exceptions.

Do not suppress exceptions merely to keep the application running.

Use actionable error messages that explain what the user can do next.

# 5. Security and Privacy Audit

Perform a focused security review of the existing implementation.

Review:

- Project-root and source-file path validation.
- Symlink and reparse-point handling.
- File discovery and excluded directories.
- Git repository inspection.
- HTML report escaping.
- JSON and CSV serialization.
- Export paths and overwrite behavior.
- SQLite access and migration handling.
- Logs and exception messages.
- Local settings and any credentials, if present.
- Optional AI request handling.
- Third-party package dependencies.

Requirements:

- Treat source repositories and their contents as untrusted input.
- Never execute arbitrary source code being analyzed.
- Do not automatically run project scripts or custom build targets without an explicit trust model.
- Do not allow source paths to escape their permitted root.
- Avoid logging source-code contents, credentials, secrets, or unnecessary personal information.
- Escape untrusted content when generating HTML.
- Do not introduce remote telemetry or hidden network requests.
- Do not expose sensitive absolute paths in reports by default.
- Do not weaken existing safeguards to make tests pass.

Create a documented security review containing identified risks, fixes, remaining limitations, and manual verification steps.

Do not claim the application is completely secure or formally certified.

# 6. Performance Engineering

Measure before optimizing.

Establish a repeatable baseline using representative C# projects of different sizes where practical.

Measure relevant operations, such as:

- Project onboarding and file discovery.
- Roslyn project loading.
- Symbol and dependency extraction.
- Finding generation and persistence.
- Database query performance.
- Graph data loading and rendering.
- Issue filtering and search.
- Git status and commit-history loading.
- HTML and JSON report generation.
- Memory usage during long analyses.

Record environment, approximate project size, test procedure, and actual measurements.

Improve only identified bottlenecks.

Potential improvements include:

- Bounded batching for database writes.
- Appropriate SQLite indexes.
- Efficient no-tracking queries.
- Avoiding repeated parsing or semantic-model creation.
- Avoiding duplicate database queries for graph nodes.
- Limiting graph neighborhoods for large repositories.
- Debouncing search inputs.
- Virtualizing large result lists.
- Bounded concurrency.
- Cancellation-aware processing.
- Releasing large temporary objects when no longer needed.
- Avoiding repeated file reads and hashing.
- Caching only where invalidation can be made correct.

Do not introduce complex caching or concurrency without evidence that it improves the application.

Do not sacrifice correctness or responsiveness for small benchmark improvements.

Document measured improvements and any known scaling limits. Do not invent performance numbers.

# 7. Database Reliability and Recovery

Audit SQLite initialization, EF Core migrations, persistence, and failure handling.

Requirements:

- Preserve all existing migrations.
- Never rewrite the original InitialCreate migration to simulate a clean database.
- Test migration from an existing Phase 1 database and the current database schema.
- Test migration on a fresh installation.
- Handle interrupted operations and database errors clearly.
- Use transactions for logically related writes.
- Verify that foreign-key relationships remain valid.
- Avoid duplicate records after repeated scans or analysis.
- Preserve completed analysis results unless the user explicitly requests their removal.
- Ensure concurrent operations do not corrupt data.
- Handle database locking and permission errors with useful guidance.

Implement a safe backup strategy before applying migrations to an existing user database where appropriate.

If automatic backup is implemented:

- Use a supported SQLite backup approach or another verified consistent backup mechanism.
- Do not blindly copy an actively written database file and assume it is consistent.
- Avoid overwriting existing backups.
- Clearly identify backup location and result.
- Do not delete backups automatically without an explicit retention policy.

Test restoration from a backup.

Do not use destructive migration commands against the user's actual database during testing.

# 8. Automated Testing

Extend the existing unit and integration test projects.

Keep all earlier phase tests passing.

Review test quality and add missing coverage for:

## Domain and application logic

- Invalid inputs.
- Empty projects.
- Missing files.
- Duplicate operations.
- Cancellation.
- Failure handling.
- Analysis lifecycle transitions.
- Result consistency.
- Filtering and sorting.
- Repeated analysis.
- Multiple projects with overlapping symbol names.

## Roslyn analysis

- Supported project formats.
- Syntax errors.
- Semantic-analysis failures.
- Unresolved references.
- Partial analysis.
- Symbol and dependency identity.
- Rule correctness.
- Duplicate findings.
- Large-file behavior.
- Cancellation.

## Graph and issue explorer

- Large and empty graphs.
- Stable selection.
- Search and filtering.
- Missing nodes or files.
- Neighborhood limits.
- Findings without symbol associations.
- Rapid project switching.

## Git and reports

- Repositories without commits.
- Modified and untracked files.
- Invalid repository metadata.
- HTML injection attempts.
- JSON serialization.
- CSV escaping, if supported.
- Export cancellation.
- Comparison limitations.
- File permission failures.

## Persistence

- Fresh database creation.
- Migration from an existing database.
- Foreign-key integrity.
- Repeated analysis.
- Failed and cancelled runs.
- Transaction rollback where applicable.
- Backup and restoration.

Use temporary databases, temporary directories, and isolated test repositories.

Tests must not mutate the user's actual ForgeDesk database or external source projects.

Avoid network-dependent tests. Where platform-specific behavior cannot be tested automatically, document a manual verification procedure.

Use deterministic test data and avoid relying on timing-sensitive assertions unless necessary.

# 9. Windows UI and Accessibility Verification

The application is a Windows WPF desktop application, so validate it on Windows.

Test:

- Application startup and shutdown.
- Main navigation.
- Project onboarding.
- Project removal behavior.
- Light and dark themes.
- Window resizing and minimum dimensions.
- High-DPI display behavior.
- Long file paths where supported.
- Unicode project names and file paths.
- Keyboard navigation.
- Focus visibility.
- Accessible control names and tooltips.
- Modal dialog behavior.
- Loading indicators.
- Cancellation.
- Error dialogs.
- Long-running analysis.
- Large issue lists.
- Graph pan and zoom.
- Source navigation.
- Report export dialogs.
- Git status and history.
- Database migration behavior.

Verify that the application behaves correctly when:

- A selected project is deleted or moved.
- A file disappears during analysis.
- Access is denied to a directory.
- The database is unavailable.
- A user cancels an operation.
- The window closes during an operation.
- The system has no internet connection.
- A Git repository is malformed.
- No previous analysis results exist.

Fix actual usability defects discovered during verification.

Do not claim Windows UI verification was completed if the environment could not launch WPF.

# 10. Logging and Diagnostics

Review the existing local logging implementation.

Provide enough information to troubleshoot:

- Startup failures.
- Project onboarding errors.
- Analysis failures.
- Database migration failures.
- Git operation errors.
- Report export failures.
- Cancellation and unexpected exceptions.

Requirements:

- Keep logs local.
- Use appropriate severity levels.
- Include operation identifiers when useful.
- Avoid excessive repetitive log messages.
- Avoid logging credentials or full source contents.
- Handle log directory permission errors gracefully.
- Prevent unbounded log growth through a documented rotation or retention strategy.
- Ensure logging failures do not recursively crash the application.

Document the default log location and how users can collect diagnostics safely.

# 11. Configuration and Settings

Review existing settings and persisted preferences.

Ensure settings have:

- Sensible defaults.
- Input validation.
- Clear descriptions.
- Safe persistence.
- Graceful handling of invalid or missing values.
- Backward compatibility where appropriate.

Where useful, provide configurable settings for:

- Default report directory.
- Graph rendering limits.
- Analysis rule thresholds.
- Logging verbosity.
- Optional AI endpoint and model configuration.
- AI request timeouts.

Do not add unnecessary settings that users cannot meaningfully understand or control.

Do not store API keys in source code, committed configuration, or ordinary logs.

# 12. Windows Release Packaging

Produce a reproducible Windows release package.

Inspect the existing .NET target and choose an appropriate publish strategy.

At minimum:

- Publish a `win-x64` build.
- Prefer a self-contained deployment if appropriate for a standalone user experience.
- Verify the actual published output and runtime requirements.
- Use Release configuration.
- Include necessary WPF resources and native dependencies.
- Preserve the correct SQLite and EF Core behavior.
- Ensure required migrations and database initialization work on first launch.
- Exclude development-only files and secrets.
- Keep the local database and logs outside the installation directory.
- Do not bundle personal databases or logs.

Evaluate single-file publishing separately. Do not enable trimming or Native AOT blindly, because WPF, reflection, EF Core, Roslyn, and third-party dependencies may require additional support.

Prefer a reliable multi-file publish over a broken single-file build.

Provide a documented build or packaging script that can be run repeatedly.

Where practical, create:

1. A self-contained publish directory.
2. A distributable ZIP archive.
3. A Windows installer only if it can be implemented and tested without compromising reliability.

If an installer is added, support an appropriate installation directory, shortcuts, clean uninstall behavior, and preservation of user data.

Do not require administrator privileges unnecessarily.

Do not overwrite or remove the user's database during an application update or uninstall.

Document the difference between installing the application and storing user data.

# 13. First-Run Experience and Updates

Ensure a new installation can start without an existing developer environment.

On first launch:

- Create the application data directory.
- Initialize or migrate the local database safely.
- Initialize default settings.
- Create the log directory if possible.
- Show useful startup errors if initialization fails.
- Avoid showing fake project data.

Test an upgrade from a previous database version.

Do not implement an automatic updater that downloads or executes unverified packages.

If update support is out of scope, document a safe manual upgrade process.

# 14. Release Metadata

Provide appropriate release metadata and documentation:

- Application name and version.
- Product description.
- Supported Windows architecture.
- .NET runtime requirements, if applicable.
- Release build identifier where useful.
- Third-party dependency and license information.
- Known limitations.
- Installation instructions.
- Upgrade instructions.
- Uninstall and data-retention behavior.

Use a consistent versioning strategy compatible with the existing project.

Do not invent a company identity, code-signing certificate, digital signature, or security certification.

If code signing is unavailable, document that limitation.

# 15. Optional Local AI Assistance

Implement AI assistance only after core reliability, testing, packaging, and security work is complete.

If the application is not stable, document AI as deferred and finish the required release work instead.

The AI feature should help developers understand already-generated analysis results. It must not replace the Roslyn analysis engine or fabricate facts.

Potential features:

- Explain a selected finding in plain language.
- Summarize the dependency neighborhood of a selected symbol.
- Explain a selected compiler diagnostic.
- Suggest possible remediation steps.
- Summarize an analysis report.

Create an abstraction such as ICodeAssistantService with a provider interface so the core application remains independent of any particular AI vendor.

Prefer a local model endpoint when available. A compatible local HTTP inference endpoint may be supported through explicit user configuration.

Do not assume a particular model server is installed.

Requirements:

- AI must be optional and disabled until configured.
- The rest of ForgeDesk must work without AI.
- Do not require a cloud API key.
- Do not silently send source code, file paths, logs, or analysis results to a remote provider.
- Clearly display when a request would leave the computer.
- Require explicit opt-in before any remote processing.
- Minimize the data sent to the model.
- Prefer selected findings and minimal relevant context over entire source files.
- Do not include secrets or unrelated files in prompts.
- Validate and bound request size.
- Support timeouts and cancellation.
- Handle unavailable endpoints and invalid model responses.
- Treat AI output as untrusted text.
- Never execute AI-generated code automatically.
- Never allow AI output to run shell commands or mutate the repository.
- Clearly label explanations and suggestions as AI-generated.
- Do not present suggestions as confirmed compiler diagnostics.
- Allow the user to copy suggestions without applying them automatically.

Add tests using a fake or deterministic provider. Do not require an actual model or internet access for the normal test suite.

If local inference cannot be tested in the environment, document the limitation and the manual verification steps.

# 16. Dependency and License Audit

Review NuGet dependencies across the solution.

Identify:

- Direct dependencies.
- Unnecessary or duplicated packages.
- Outdated packages where safely verifiable.
- Known vulnerable package versions where supported by available tooling.
- Platform-specific dependencies.
- License information.

Use appropriate built-in .NET tooling, including package vulnerability checks where supported by the configured SDK and package sources.

Do not update every package indiscriminately. Apply upgrades only when compatible and verified by the test suite.

Document third-party licenses and notices required for redistribution.

Do not claim a clean vulnerability audit if the scan could not run or package sources were unavailable.

# 17. Build, Test, and Release Automation

Create or improve reproducible developer and release commands.

Provide commands or scripts for:

- Restore.
- Release build.
- Unit tests.
- Integration tests.
- Full test suite.
- Database migration validation.
- Windows publish.
- Package creation.
- Basic release validation.

Ensure scripts fail when an essential step fails.

Avoid scripts that silently ignore errors or report success after failed commands.

Keep environment-specific settings explicit.

Do not make Linux-only success stand in for Windows WPF verification.

Use the existing CI configuration if available. If adding CI, choose an appropriate Windows runner for WPF builds and tests.

Do not expose secrets through CI logs or commit CI credentials.

# 18. Documentation

Produce or update:

- README.md with the actual project status and feature set.
- CONTRIBUTING.md with setup, conventions, and test instructions.
- SECURITY.md with a vulnerability-reporting process appropriate to the project.
- A user guide with installation and common workflows.
- A developer guide describing the architecture and project boundaries.
- A troubleshooting guide.
- Release and upgrade instructions.
- A report of known limitations.
- Third-party dependency and license notices.
- Phase 5 test and release validation results.

Do not claim that a security contact, support service, or release pipeline exists unless it has actually been configured.

Include screenshots only if they are generated from the actual running application.

Clearly distinguish implemented features from planned features.

# 19. Final Acceptance Criteria

Phase 5 is complete only when:

1. The existing solution builds in Release configuration on the supported Windows environment.
2. All Phase 1–4 tests pass.
3. New tests pass.
4. Security review findings are addressed or explicitly documented.
5. Performance measurements are recorded for representative operations.
6. Database migration and recovery behavior are tested.
7. WPF startup, shutdown, and key user workflows are verified on Windows.
8. The published `win-x64` output is generated and verified.
9. User data remains outside the installation directory.
10. Installation and upgrade instructions are complete.
11. Dependency and license information is documented.
12. No secrets, local databases, or personal logs are bundled.
13. Optional AI, if implemented, is isolated, opt-in, and nonessential.
14. Existing functionality remains intact.
15. The final release report states exactly what was tested and what remains unverified.

If a criterion cannot be satisfied because of a real environmental limitation, report it clearly rather than falsely claiming completion.

# 20. Required Implementation Workflow

Step A — Inspect the repository, establish the baseline, and review known issues.

Step B — Fix high-priority correctness and reliability defects.

Step C — Complete the security and privacy review.

Step D — Measure and address the most important performance bottlenecks.

Step E — Extend automated tests and preserve all existing tests.

Step F — Verify database migration, backup, and recovery behavior.

Step G — Perform Windows-specific manual verification.

Step H — Create and validate the reproducible Windows release package.

Step I — Complete user, developer, troubleshooting, and release documentation.

Step J — Audit third-party dependencies and licenses.

Step K — Implement optional local AI only if the core application is stable and the release objectives are satisfied.

Step L — Run the final build, tests, security checks, and packaging workflow.

Step M — Produce a final report including:

- Files changed.
- Architecture decisions.
- Defects fixed.
- Tests executed and actual results.
- Security review findings.
- Performance measurements.
- Database migration and recovery results.
- Exact publish and package commands.
- Actual artifact locations.
- Installation and upgrade instructions.
- Known limitations.
- Features deferred to future versions.

Do not claim a command passed unless it was actually executed and the result was observed.

Do not replace unfinished work with mock data, TODOs, pseudocode, or a superficial interface.

# Final Instruction

Implement Phase 5 directly in the existing ForgeDesk repository. Preserve the established architecture, prioritize correctness and security over new features, and produce a verifiable Windows release candidate.

The result should be a maintainable, testable, documented desktop application—not merely a project that compiles.