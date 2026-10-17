# Phase 4 read-only Git core

ForgeDesk exposes local Git repository context through the Application boundary
and a native WPF read-only Git Changes page without a remote service or a Git library
package. The implementation is intentionally limited to repository inspection;
it does not stage, commit, reset, checkout, merge, pull, push, or delete files.

## Architecture

```text
IGitRepositoryService
    -> GitRepositoryService
         -> IGitRepositoryReader
              -> GitProcessRepository
                   -> GitCommandRunner -> local git executable
```

`ForgeDesk.Application` owns the public Git contracts and the nearest-repository
workflow. `ForgeDesk.Infrastructure` owns process execution and Git output
parsing. `ForgeDesk.Presentation` adds a scoped presentation adapter and a
cancellable Git Changes viewmodel; `ForgeDesk.App` registers the adapter and
maps the page into the existing navigation shell.

The adapter uses `ProcessStartInfo.ArgumentList` for every argument, disables
terminal prompts, disables optional Git locks, and does not invoke a shell. It
uses only local read commands:

- `rev-parse --show-toplevel`, `rev-parse --verify --quiet HEAD`
- `symbolic-ref --quiet --short HEAD`
- `status --porcelain=v1 -z --untracked-files=all`
- `show -s`, `log`, and `diff-tree` for commit metadata and bounded history

No remote, credential, or network command is issued.

## Repository detection and states

`GitRepositoryService.InspectAsync(projectRoot)` checks the selected directory
and then each parent for a `.git` directory **or a `.git` file**. The latter is
the metadata form used by linked worktrees. The first marker wins, so a nested
project cannot accidentally select a more distant unrelated repository. Git
then supplies the canonical worktree root for the returned
`RepositoryRootPath`.

The result uses an explicit `GitRepositoryState`:

| State | Meaning |
| --- | --- |
| `NotRepository` | No `.git` marker exists in the project path or parents. This is a normal optional state. |
| `Clean` | Git status was read successfully and no paths were reported. This includes a repository with no commits yet. |
| `Dirty` | Git status was read successfully and one or more paths were reported. |
| `Unreadable` | A marker exists, but Git metadata, the executable, permissions, malformed output, timeout, or configured bounds prevented a safe read. |

Unreadable errors are intentionally short and actionable. Git stderr and
repository content are not written to logs or returned as diagnostic text.

## Status model

`GitFileChange.RelativePath` and `OriginalRelativePath` (for renames/copies)
are slash-normalized paths relative to the repository root. Each entry retains
the porcelain index and worktree status characters and exposes flags for:

- staged changes;
- unstaged changes;
- untracked files;
- conflicts (`U`, `AA`, or `DD` combinations); and
- added, modified, deleted, renamed, copied, and type-changed paths.

The same path can be both staged and unstaged, for example after staging one
version and editing the file again. `GitRepositorySnapshot` provides filtered
views (`StagedChanges`, `UnstagedChanges`, `UntrackedChanges`, and
`ConflictedChanges`) without performing another Git read.

## HEAD and history

When `HEAD` resolves, the snapshot reports the full object ID, subject, author
name/email, UTC author date, and parent object IDs. An initial repository with
no commit has a branch name but no fabricated HEAD metadata. Detached HEADs
have `Branch.Name == null` and `Branch.IsDetached == true` when an object is
available.

Recent history is bounded by `GitInspectionOptions.MaxRecentCommits` (25 by
default). The adapter asks Git for one additional ID to mark the result as
truncated, and each commit includes parent IDs and changed paths from
`diff-tree` when available.
`MaxChangedFilesPerCommit` bounds each commit's changed-path list. A separate
`GetHistoryAsync` call uses the same bounded read path and reports the actual
clean/dirty state.

Working-tree paths are bounded by `MaxChangedFiles`. Each subprocess has a
finite timeout and combined stdout/stderr byte budget. These limits keep a
large or malformed repository from causing an unbounded read; a limit breach
produces `Unreadable` rather than a false complete result.

Dates are parsed as Git's offset-aware ISO date and converted to UTC. No remote
is required, and repositories without remotes are valid.

## Dependency and deployment choice

LibGit2Sharp was not added. The repository already has a locked .NET 8 restore,
and a process adapter avoids introducing native package compatibility and
licensing decisions while still providing real Git semantics for worktrees,
porcelain status, and history. The only runtime dependency is Git itself. The
verification `Dockerfile` installs the Debian `git` package; Windows users
should install a supported Git distribution and ensure `git` is on `PATH`.
No NuGet lockfile changes are required.

## Tests

The deterministic tests create disposable repositories and invoke Git with
argument lists. They cover:

- nearest parent detection and `.git` worktree files;
- no-commit and clean repositories;
- staged, unstaged, staged-plus-unstaged, untracked, renamed, and conflicted paths;
- bounded history, parent IDs, changed files, and changed-file limits;
- repositories with no remotes;
- missing repositories and malformed metadata; and
- cancellation before repository access.

Run the canonical offline checks from the repository root:

```bash
make test
```

The focused checks are `make test-unit` and `make test-integration`. The Docker
verification image installs Git, restores with the checked-in lockfiles, builds
the solution, and runs both suites without network access during test
execution.

## WPF usage and manual verification

Open **Git Changes** after selecting a project. Confirm the page distinguishes
not-a-repository, clean, dirty, unreadable, and empty repositories; shows
repo-relative paths; exposes staged/unstaged/untracked/conflict groups; and
updates after pressing Refresh. Select a recent commit and confirm its bounded
changed-file list and UTC date. Cancel a refresh on a large repository and
confirm no mutation occurs. Switch light/dark themes and resize the page.

## Known limitations and Phase 5 handoff

- Status is based on Git porcelain output and does not include a diff viewer.
- Commit changed-file details are bounded and may be unavailable in a damaged
  object database; the result reports the read failure instead of inventing
  paths.
- Git executable discovery is the host `PATH`; a future host integration can
  provide an explicit executable path through `GitProcessRepository`.
- No analysis-run Git metadata is persisted yet. Phase 5 can associate a
  manually requested analysis with an immutable snapshot if the analysis data
  model is extended through a new migration, while preserving the optional
  behavior for projects without a repository.

Future presentation work should keep these states visible, refresh explicitly,
and preserve cancellation and bounded limits. It must not add Git mutation
commands behind a read-only status action.
