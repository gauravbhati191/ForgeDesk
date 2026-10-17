# ForgeDesk troubleshooting

## Startup fails before the window appears

1. Check `%LOCALAPPDATA%\ForgeDesk\logs\forgedesk.log` and the rotated `.1`, `.2`, or `.3` files.
2. Confirm that the Windows account can create and write `%LOCALAPPDATA%\ForgeDesk`.
3. Confirm that another ForgeDesk process is not holding an incompatible SQLite lock.
4. Do not delete the database first. Copy or use the verified backup under `%LOCALAPPDATA%\ForgeDesk\backups\` and then retry.

If the database is corrupt or a migration fails, preserve the original database and logs. Move only a verified copy aside if you need to test a fresh initialization. A fresh database does not recover historical projects or scans.

## The project cannot be added

- Select a directory that still exists and is readable.
- Check that the Windows account has access to the folder and its descendants.
- Confirm that the path is not a reparse point whose final target is outside the selected root.
- Close tools that are replacing or deleting the folder while discovery runs.
- Inspect the onboarding error and the discovery summary for skipped inaccessible entries.

ForgeDesk does not grant itself access to an ACL-protected directory and does not follow an external link merely because it is inside the selected folder.

## Analysis is partial or falls back to syntax-only mode

Syntax-only fallback is expected when a valid `.sln`, `.slnx`, or `.csproj` cannot be loaded by `MSBuildWorkspace`, when references cannot be resolved, or when project files are malformed. In that mode:

- declarations and local quality rules can still be produced;
- semantic dependency edges may be missing;
- compiler diagnostics may be limited to syntax diagnostics.

Review the binding message, fix the project/reference issue in a copy of the project, and run analysis again. ForgeDesk does not run a build or automatically repair project files.

## Analysis is slow or the window appears busy

The analysis and graph operations are asynchronous and report progress, but Roslyn project loading and a very large graph can still require substantial CPU and memory. Try a smaller representative project, close duplicate analyses, and use graph search/depth limits. Cancel before switching projects if a long run is still active.

The current measured persistence optimization reduces deletion time for a 1,500-symbol/1,499-edge/1,500-finding temporary SQLite workload; it is not a promise about every repository size.

## Git Changes shows no repository or an unreadable state

- Confirm that Git 2.x is installed and available on `PATH`.
- Check that the selected project is inside the intended repository or worktree.
- Verify that `.git` metadata is readable and not malformed.
- Check whether a security product or ACL blocks the Git executable.

ForgeDesk only performs bounded read-only commands. It will not repair Git metadata or use a remote as a fallback.

## Report export fails

- Choose a directory that already exists and is writable.
- Use the format extension selected by the report page (`.json`, `.html`, or `.csv`).
- Close an existing file with the same name or choose a new path; overwrite is intentionally protected.
- Avoid `..`, control characters, reserved Windows names, trailing dots/spaces, and invalid filename characters.

If cancellation occurs during an export, the temporary file is not presented as a completed report. Retry after checking available disk space.

## The package does not start on Windows

- Extract the complete ZIP; do not run the executable from inside an archive.
- Keep all files in the publish directory together, including `e_sqlite3.dll` and the .NET runtime files.
- Use a supported 64-bit Windows installation and a user-writable extraction directory.
- Check Windows Event Viewer and `%LOCALAPPDATA%\ForgeDesk\logs\forgedesk.log` when the process exits immediately.

The current repository does not ship a code signature or installer. Windows launch must be verified on the target machine; a successful Linux publish does not prove native WPF startup.

## The database is locked or migration backup fails

Close other ForgeDesk instances and wait for an active analysis/export to stop. Keep the original database untouched while troubleshooting. Migration backups are created with SQLite's online backup API and a unique destination; a failure should leave the active database unchanged and should be reported in the startup log.

Do not copy only the main `.db` file while SQLite is actively using WAL mode. Use the built-in backup path or close the application first.

## Collecting diagnostics safely

When asking for help, provide:

- ForgeDesk version/commit and Windows version;
- the exact workflow and sanitized error message;
- whether the issue reproduces with a copy of the project;
- relevant log lines after removing source contents, secrets, usernames, and private absolute paths.

Do not attach the database, source tree, full report, or raw logs publicly without reviewing them.
