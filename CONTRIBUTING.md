# Contributing to ForgeDesk

ForgeDesk is a local-first Windows WPF application for inspecting C# projects. Contributions should preserve the existing layered architecture, local-only defaults, and the ability to reproduce builds from a clean checkout.

## Before making a change

1. Install the .NET SDK selected by `global.json` (`8.0.416`) and Git 2.x.
2. Clone the repository and run:

   ```bash
   dotnet restore ForgeDesk.sln --locked-mode
   make test
   ```

3. On Windows, launch `ForgeDesk.App` when the change affects WPF behavior. Linux can compile the Windows target but cannot provide a native WPF smoke test.
4. Inspect `git status` before editing. Preserve unrelated local changes and never add local databases, logs, IDE state, credentials, or generated publish output.

## Repository conventions

- Keep dependency direction consistent with `docs/ARCHITECTURE.md`.
- Put domain rules in `ForgeDesk.Domain`, use-case contracts/orchestration in `ForgeDesk.Application`, implementations in `ForgeDesk.Infrastructure`, Roslyn behavior in `ForgeDesk.Analysis`, graph behavior in `ForgeDesk.Graph`, and WPF-specific composition in `ForgeDesk.App`/`ForgeDesk.Presentation`.
- Keep `DbContext` and repository scopes short-lived. Do not store a scoped context in a singleton viewmodel.
- Treat repositories being analyzed as untrusted input. Do not execute source files, project scripts, Git hooks, or arbitrary commands.
- Keep timestamps in UTC, use checked-in EF migrations, and preserve existing migrations once they may have reached a user database.
- Avoid logging source contents, credentials, tokens, or unnecessary absolute paths.
- Keep remote services, telemetry, cloud synchronization, and hosted AI out of the default application path.
- Prefer deterministic output and cancellation-aware asynchronous work.

## Tests and checks

`make test` is the canonical offline check. It performs locked restore, a Release solution build, and both test suites:

```bash
make test
make test-unit
make test-integration
make migrate-fresh
```

Tests must use temporary folders, isolated SQLite databases, and temporary Git repositories. They must not modify a developer's `%LOCALAPPDATA%\ForgeDesk` database or an external project.

For a schema change, add a new migration and test both a fresh database and an older migration state. Never use destructive migration commands against a real user database.

For a release check on Windows:

```powershell
.\scripts\package-release.ps1
```

The script produces an ignored self-contained multi-file publish and ZIP under `artifacts/`.

## Pull requests

- Use a focused branch and a conventional commit prefix such as `feat:`, `fix:`, `perf:`, `test:`, `docs:`, or `build:`.
- Explain the user-visible behavior, changed files, tests run, and any Windows-only or environment-specific gap.
- Include screenshots only when they were captured from the actual WPF application.
- Do not claim that native Windows UI, accessibility, signing, installer behavior, or package launch was tested unless it was actually tested on Windows.
- Keep generated files and secrets out of commits. Run `git diff --check` before submitting.

## Reporting security issues

Do not disclose an exploitable issue in a public issue or pull request. Follow the private reporting instructions in [`SECURITY.md`](SECURITY.md). If the repository does not expose a private GitHub reporting channel, contact the project maintainer through the repository's established private channel and include only the minimum reproducible detail.
