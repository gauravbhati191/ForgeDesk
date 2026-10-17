# Phase 4 implementation report

Phase 4 extends ForgeDesk with local repository awareness, professional
analysis exports, read-only run comparison, and native WPF workflow pages. The
features remain optional: a project without Git or without analysis history
continues to work with explicit empty/not-available states.

## Delivered flow

```text
Projects / AnalysisViewModel
        |
        +--> GitChangesViewModel
        |       -> IGitRepositoryPresentationService
        |       -> IGitRepositoryService
        |       -> local git executable (read-only)
        |
        +--> ReportsViewModel
                -> IProjectReportService (short-lived App scope)
                -> IAnalysisReportService / IAnalysisComparisonService
                -> AnalysisReportDataSource (bounded, no-tracking SQLite reads)
                -> native report save dialog
```

The WPF viewmodels are long-lived singletons. App adapters create a fresh scope
for every Git, report, history, or comparison operation, so scoped services and
EF contexts are never retained by the shell.

## Git Changes page

The page reads the nearest `.git` directory or worktree metadata file at the
selected project root or its parents. It reports:

- repository root, branch/detached HEAD, full HEAD ID, subject, author, and UTC date;
- clean, dirty, empty, not-a-repository, unreadable, loading, error, and cancelled states;
- staged, unstaged, untracked, conflicted, renamed, deleted, added, modified,
  copied, type-changed, and other paths relative to the repository root; and
- bounded recent commits with parent IDs and selected-commit changed files.

All Git commands are invoked through `ProcessStartInfo.ArgumentList`, never a
shell. The adapter disables prompts and optional locks, uses finite output and
time limits, and performs no remote or mutating command. ForgeDesk does not
stage, commit, reset, checkout, merge, pull, push, or delete repository files.
See [PHASE4_GIT_CORE.md](PHASE4_GIT_CORE.md) for parser and deployment details.

## Reports page

Reports are always tied to an explicit `projectId` and `scanId`. The page can:

1. load the selected persisted run and show source-file, symbol, dependency,
   finding, severity, rule, and Git-context summaries;
2. export JSON, self-contained HTML, or findings CSV through the native save dialog;
3. show incomplete-run and bounded-query limitations instead of turning missing
   data into zeroes; and
4. compare an older selected run with the current run without changing SQLite
   or the Git repository.

Absolute project and repository roots are redacted by default. The HTML report
contains inline CSS only, has no remote resources or executable scripts, and
HTML-encodes repository-derived strings. JSON has stable schema and fingerprint
versions; CSV uses RFC-style field escaping. Existing destinations are rejected
unless the caller explicitly opts into overwrite.

The current database schema does not persist Git metadata on an analysis run.
Reports therefore label Git data as captured at report generation time and do
not claim that it represents the historical analyzed commit.

## Comparison semantics

Finding fingerprints use rule ID, normalized relative path, symbol identity,
location, and normalized message. Database-generated finding IDs are not used.
Exact fingerprints match first; conservative fallback keys identify line/path
changes only when metadata is sufficiently unambiguous. Duplicate findings,
missing path/line/symbol metadata, incomplete runs, truncation, and different
tool versions produce visible limitations and can make the comparison
unreliable. A finding is never called fixed solely because a line moved.

## Verification

The canonical check is:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 make test
```

The checked-in tests use temporary SQLite databases, directories, and Git
repositories. They do not use network access or mutate the developer's actual
repository. Docker installs Git in the Linux verification image; Windows users
need Git 2.x available on `PATH` for the local Git page and integration tests.

## Manual Windows verification

1. Open a project inside a normal Git repository, a nested worktree, and a
   folder with no repository. Confirm the Git states and repository root are
   accurate.
2. Create staged, unstaged, untracked, renamed, deleted, and conflicted paths in
   a disposable repository. Refresh and verify grouped relative paths.
3. Select a recent commit and verify its UTC date, parents, and bounded files.
4. Run analysis, open Reports, build the current report, export JSON/HTML/CSV,
   reopen the files, and confirm no source content or absolute root is included
   by default.
5. Create a second analysis run, select it as baseline, compare, and review
   new/removed/changed/unchanged findings plus limitations.
6. Try an existing export destination, cancel a long operation, switch themes,
   resize the pages, and inspect keyboard/accessibility labels.

WPF rendering, native save dialogs, and Windows Git installation were not
executed in the Linux verification container.

## Phase 5 handoff

Phase 5 may add persisted Git association at analysis start through a new
additive migration if an immutable historical snapshot is required. It should
not infer a historical commit from current Git state, and it should preserve
the read-only Git boundary unless explicitly adding separately confirmed
mutation workflows.
