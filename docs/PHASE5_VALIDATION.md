# ForgeDesk Phase 5 validation report

This report records the Phase 5 implementation evidence and the remaining platform gaps. It is not a claim of formal production certification.

## Implemented changes

- Added SQLite online backup and restore through `IDatabaseBackupService` and `SqliteDatabaseBackupService`.
- Added automatic unique backup creation before pending migrations on an existing configured file-backed database.
- Added integrity checks, no-overwrite backup reservation, restoration safeguards, and migration/restore integration tests.
- Added bounded local log rotation, UTF-8 entry bounds, retention tests, and recursion/permission-failure handling.
- Replaced analysis-result delete materialization with transactional EF Core `ExecuteDeleteAsync`, with tracker cleanup and rollback tests.
- Updated EF Core/`dotnet-ef` and the resolved JSON package graph after the vulnerability audit.
- Added Release metadata (`1.0.0`) and reproducible self-contained `win-x64` multi-file publish/ZIP workflows.
- Added user, developer, security, troubleshooting, release, performance, limitations, contribution, and third-party notice documentation.
- Did not add cloud synchronization, telemetry, mandatory hosted AI, an installer, an automatic updater, or a code-signing claim.

## Commands and observed results

Environment for Linux checks: Debian container, project-local .NET SDK `8.0.416`, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, with local Git available.

| Check | Result |
|---|---|
| `make test` | Passed: 44 unit tests, 28 integration tests; Release build 0 warnings/0 errors |
| `make migrate-fresh` | Passed; applied `InitialCreate` and `AddAnalysisState` to ignored `artifacts/forgedesk-fresh.db` |
| `dotnet list ForgeDesk.sln package --vulnerable --include-transitive` | All nine projects reported no vulnerable packages at the final audit |
| `git diff --check` | Passed for the implementation changes |
| `make package-release` | Passed; self-contained `win-x64` publish and ZIP were generated and checked |

The package workflow verified the presence of `ForgeDesk.App.exe`, dependency/runtime metadata, and `e_sqlite3.dll`, and rejected database, log, environment, secret, and signing files from the publish/archive contents. Artifacts are intentionally ignored and are not committed.

The observed package artifacts from the final Linux publish were:

- `artifacts/publish/win-x64/`: 600 files, 198,476,449 bytes;
- `artifacts/ForgeDesk-win-x64.zip`: 81,177,699 bytes, 613 ZIP entries.

## Database evidence

`DatabaseBackupIntegrationTests` passed coverage for unique backup creation, read-back, restoration, preservation of pre-backup data, backup before pending migration, missing/corrupt/invalid paths, cancellation, and dependency-injection registration. The existing migration files were preserved.

`AnalysisRepositoryPersistenceTests` passed coverage for replacement read-back, foreign-key-linked rows, transaction rollback, clear behavior, and change-tracker cleanup.

## Security evidence

Automated coverage includes traversal and rooted-path rejection, reparse-point discovery outside the project root, HTML injection escaping, CSV quoting, safe export names, read-only Git process behavior, and backup path/integrity safeguards. The review is documented in [`SECURITY.md`](../SECURITY.md).

Remaining security limitations are the local Windows account/filesystem boundary, absence of code signing, the non-sandbox nature of SDK/MSBuild project evaluation, and the need to review reports/logs before sharing.

## Performance evidence

For an isolated temporary SQLite workload of 1,500 symbols, 1,499 dependencies, and 1,500 findings, the clear median decreased from 213 ms to 28 ms after the bulk-delete change (approximately 87% lower). Complete replacement measured 404 ms before and 435 ms after; no replacement speed gain is claimed. Details and scaling limits are in [`PERFORMANCE.md`](PERFORMANCE.md).

## Windows verification status

Not verified in the current Linux environment:

- native WPF startup/shutdown and dispatcher behavior;
- project onboarding through the Windows folder picker;
- themes, focus, keyboard navigation, accessibility names, high-DPI resizing, and modal dialogs;
- cancellation and close-window behavior during a live Windows analysis;
- first launch and migration against a real `%LOCALAPPDATA%` database;
- package launch, uninstall/data-retention behavior, Windows Git executable discovery, and long-path/Unicode UI cases;
- code signing and installer behavior (no signing or installer is configured).

These are explicit release-candidate follow-ups, not passing criteria inferred from the Linux build.

## Deferred work

Optional AI assistance remains deferred because it is not required for the core local-first workflow and cannot be responsibly added without a separately verified trust/opt-in model. Hosted synchronization, telemetry, automatic updates, and an installer are also outside this release.
