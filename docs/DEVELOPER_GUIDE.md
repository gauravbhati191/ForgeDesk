# ForgeDesk developer guide

## Project boundaries

The solution is a local desktop application with explicit dependency direction:

```text
ForgeDesk.App          WPF composition root and lifecycle
ForgeDesk.Presentation WPF views, viewmodels, controls, and UI contracts
ForgeDesk.Application  use cases, DTOs, orchestration, and ports
ForgeDesk.Domain       entities, invariants, and path rules
ForgeDesk.Infrastructure EF Core, SQLite, filesystem, Git, and adapters
ForgeDesk.Analysis      Roslyn engine and analysis rules
ForgeDesk.Graph         graph contracts, filtering, and deterministic layout
ForgeDesk.UnitTests     isolated unit tests
ForgeDesk.IntegrationTests real SQLite/Git/persistence tests in temp fixtures
```

`ForgeDesk.Domain` has no UI, EF Core, or infrastructure dependency. The WPF composition root is the only place that assembles concrete services. See [`ARCHITECTURE.md`](ARCHITECTURE.md) for the detailed flow and [`DEVELOPMENT.md`](DEVELOPMENT.md) for the basic tool commands.

## Lifetimes and scopes

- WPF viewmodels and presentation services that own shared UI state are long-lived.
- `ForgeDeskDbContext`, repositories, analysis engines, Git readers, graph queries, and report queries are scoped.
- App adapters create a short-lived scope for each database-backed operation and dispose it after the operation completes.
- A single EF context must not be used concurrently. Start separate scopes for independent operations.

When adding an async command, guard duplicate execution, propagate its cancellation token, distinguish cancellation from failure, and ensure stale results cannot overwrite a newer project/scan selection.

## Persistence and migrations

`ForgeDeskDbContext` applies checked-in migrations from `ForgeDesk.Infrastructure/Persistence/Migrations`. The production path is `Database.MigrateAsync`; `EnsureCreated` is not used.

For a model change:

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations add DescriptiveName \
  --project ForgeDesk.Infrastructure/ForgeDesk.Infrastructure.csproj \
  --startup-project ForgeDesk.Infrastructure/ForgeDesk.Infrastructure.csproj \
  --output-dir Persistence/Migrations
make migrate-fresh
make test
```

Never edit or replace an already checked-in migration to simulate a fresh schema. Add a new migration, test the old schema upgrade, and test a fresh database. Migration backups use SQLite's online backup API; do not add a raw file copy of a live database as a shortcut.

## Filesystem and security boundaries

Use `PathNormalization` and the existing discovery/path-validation services. Validate both lexical and physical containment. Reparse points can escape a root even when their textual path looks nested. Do not follow external links, execute project files, or run arbitrary shell commands.

Report serializers must encode HTML, quote CSV, use bounded queries, redact absolute paths by default, and write through the existing safe export path. Git commands must remain no-shell and read-only.

## Analysis and graph flow

1. Onboarding discovers metadata and persists a completed discovery scan.
2. `AnalysisService` revalidates every discovered source path, reads source text, and invokes `IAnalysisEngine`.
3. Roslyn returns deterministic symbol/dependency/finding drafts.
4. `AnalysisRepository` replaces scan-owned result rows in a transaction and records terminal analysis status.
5. Graph and report services read one scan with no-tracking, bounded queries.

The graph model is a temporary read model. Do not persist view coordinates or mix scans. A selected graph node, finding, or source preview must remain scoped to the active project and scan.

## Logging

The desktop app uses a local `FileLoggerProvider`. It caps the active file at 5 MiB by default, retains three numbered backups, bounds oversized entries, and swallows logging failures. Add operation IDs or safe metadata when useful, but do not log source contents, credentials, full exception payloads containing secrets, or unnecessary user paths.

## Release and optional features

Use `scripts/package-release.ps1` or `make package-release` for the release candidate. Keep publish output multi-file and do not enable trimming/AOT without a separate compatibility investigation.

Local/remote AI assistance is intentionally deferred. If it is revisited, introduce a provider abstraction, explicit opt-in, request size/cancellation bounds, no automatic code execution, and deterministic fake-provider tests. The rest of ForgeDesk must remain fully functional without it.
