# ForgeDesk security

## Security posture

ForgeDesk is designed to be local-first. The application does not require a cloud account, does not send project data to a hosted service by default, and does not include telemetry. Project source, Git metadata, reports, the SQLite database, and logs remain on the local machine unless the user deliberately copies or shares them.

This is an engineering posture, not a security certification. ForgeDesk has not undergone a formal penetration test or independent security assessment.

## Reporting a vulnerability

Please do not put credentials, private source code, or an exploitable proof of concept in a public issue.

1. If the repository's GitHub **Security** tab offers **Report a vulnerability** or private security advisories, use that channel.
2. If private reporting is not enabled, contact the repository maintainer through the private contact channel associated with the repository owner. Do not use a public issue for the initial report.
3. Include the affected version/commit, operating system, reproducible steps, impact, and a suggested mitigation when known. Redact tokens, source contents, personal paths, and customer data.

There is no dedicated security email address or guaranteed response-time commitment configured in this repository. The maintainer should acknowledge reports when practical and coordinate disclosure after a fix or mitigation is available.

## Implemented safeguards

### Files and paths

- Project roots and relative source paths are normalized before use.
- Discovery checks lexical containment and resolves reparse-point targets so links outside the selected physical root are skipped.
- Analysis rechecks source-file existence and physical containment before reading a file.
- Report output names reject traversal segments, control characters, unsafe Windows names, and mismatched extensions.
- Generated reports redact absolute project and repository roots by default.

### Repository inspection

- Git integration uses bounded, cancellable, no-shell subprocesses.
- It performs read-only status, HEAD, and bounded history inspection. ForgeDesk does not stage, commit, reset, checkout, merge, pull, push, run hooks, or contact remotes.
- Repository contents are treated as untrusted input.

### Reports and serialization

- HTML values are encoded before insertion into the self-contained report.
- JSON uses the standard encoder and CSV fields are quoted when required.
- Exports use safe filenames, atomic temporary writes, and overwrite protection.
- Report reads are project/scan scoped, no-tracking, and bounded.

### Database and logging

- SQLite foreign keys and EF Core migrations are used for local persistence.
- Existing databases with pending migrations receive a consistent SQLite online backup before migration when the configured database is a file-backed ForgeDesk database. Backups use unique names and are never automatically deleted or overwritten.
- Backup files are integrity-checked with SQLite `quick_check`; restoration rejects the active database path and verifies the restored destination.
- Logs stay local, rotate at a bounded size, retain a small numbered history, and fail silently rather than recursively crashing the application.

### Analysis and optional services

- ForgeDesk never executes analyzed C# source or generated code.
- No AI provider is enabled or implemented in the current release. There is no hidden remote request path.
- Roslyn/MSBuild project loading is a design-time analysis operation, not a build or deployment operation. Untrusted repositories should still be handled in a restricted Windows account or isolated environment because project files and SDK tooling are not a security sandbox.

## Security review limitations

- The current application has no installer, automatic updater, code-signing certificate, or signature verification. Verify the release ZIP through a trusted distribution channel and inspect its hash when one is published.
- WPF startup, high-DPI behavior, Windows ACLs, native dialogs, and package launch require manual Windows verification; the Linux build environment cannot prove them.
- Local files and the Windows user account remain the primary privacy boundary. A malicious or compromised project, Git executable, SDK, or user account can defeat application-level safeguards.
- SQLite backup/restore should be performed with ForgeDesk writes quiesced. After restoring, restart the app so no existing tracked context can retain stale entities.

## Safe handling guidance

- Do not attach the ForgeDesk database, logs, or reports to a public issue without reviewing them for project paths and proprietary metadata.
- Use a copy of a project for security experiments.
- Keep `%LOCALAPPDATA%\ForgeDesk` protected by normal Windows account permissions.
- Keep third-party dependencies and the .NET SDK updated; run the documented vulnerability check before a release.

## Manual verification checklist

On a disposable Windows test account and a copy of a project, verify the following before a release:

1. Start the extracted package as a standard user and confirm that no administrator prompt or installation-directory database appears.
2. Add a project containing a reparse point to an external directory and confirm that discovery skips the external target.
3. Add a project containing HTML/CSV metacharacters in names, comments, and findings; inspect exports for escaped/quoted output.
4. Use a local Git repository with modified, untracked, and conflicted paths and confirm that Git Changes does not stage or modify anything.
5. Make a copy of a database, exercise a migration from the prior schema, and confirm that a unique verified backup is created before migration.
6. Deny access to a test directory, cancel a long operation, and close the window during a disposable operation; confirm the UI reports a failure/cancellation rather than success.
7. Review logs and reports for credentials, source contents, and unnecessary absolute paths before sharing them.

These checks require Windows and are not claimed as complete by the Linux build/test evidence in `docs/PHASE5_VALIDATION.md`.
