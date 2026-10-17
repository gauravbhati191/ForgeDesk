# ForgeDesk known limitations

This document distinguishes current behavior from release work that still needs platform verification.

## Platform and packaging

- ForgeDesk is a Windows WPF application. Linux can run the solution build and local test suites but cannot launch the native UI.
- Only `win-x64` packaging is automated. ARM64, x86, macOS, and Linux desktop packages are not supported by this release workflow.
- The package is a self-contained multi-file ZIP. There is no installer, shortcut manager, automatic updater, or uninstall registration.
- No code-signing certificate or signature verification workflow is configured.
- High-DPI, accessibility, keyboard navigation, long-path behavior, native dialog behavior, and Windows ACL scenarios require a Windows test pass.

## Analysis

- Project-aware loading depends on Roslyn `MSBuildWorkspace`, available SDK/build assets, compatible project files, and resolvable references.
- When project loading fails, ForgeDesk uses syntax-only fallback. Semantic dependency edges and full compiler diagnostics can be incomplete in that mode.
- ForgeDesk does not build or execute the analyzed project, but MSBuild project evaluation is not a security sandbox. Analyze untrusted repositories in an appropriately restricted account or isolated environment.
- Very large solutions can use substantial CPU, memory, and disk space. Graph neighborhoods and report queries are bounded, but the current WPF visual graph is not a substitute for a specialized large-graph renderer.
- The current analysis schema stores findings and symbols but does not persist a source-control commit association for each run.

## Database and recovery

- SQLite is local-file storage. File-system permissions and the Windows user account are the privacy boundary; there is no database password.
- Automatic migration backups are created only for the configured file-backed database when pending migrations are detected. Memory/custom mismatched connection strings are intentionally skipped.
- Backup and restore should be performed with ForgeDesk writes quiesced. Restart the app after restoration so no existing tracked context can retain stale state.
- Backups are never automatically pruned. Users need an explicit retention policy for long-lived installations.

## Git and reports

- Git inspection requires a local Git executable on `PATH` and readable repository metadata.
- History and status output are bounded and may omit older commits or unusually large path sets.
- Reports can contain project names, relative paths, symbol identities, messages, branch names, and commit IDs. Redaction of absolute roots is not anonymization of all metadata.
- Current Git context is captured at report time and is not proof that the selected scan ran at that commit.

## AI and network behavior

- Optional local/remote AI assistance is deferred. No AI endpoint, cloud sync, telemetry, or hidden network request is part of the current release.

## Verification gaps

The final validation record must state exactly which Windows machine, package launch, migration upgrade, accessibility, and long-running UI scenarios were executed. A Linux build/test pass must not be presented as evidence for those native behaviors.
