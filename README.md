<div align="center">

# ⚡ ForgeDesk

**A local-first, high-performance Windows developer workspace & C# project intelligence suite.**

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0_LTS-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/Platform-Windows_WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![SQLite](https://img.shields.io/badge/Database-SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white)](https://www.sqlite.org/)
[![License: MIT](https://img.shields.io/badge/License-MIT-success?style=for-the-badge)](LICENSE)

*Safe onboarding, metadata discovery, persistent scan lifecycle records, deterministic Roslyn analysis, and a modern fluent UI shell.*

---

[Repository](https://github.com/gauravbhati191/ForgeDesk) • [Architecture](docs/ARCHITECTURE.md) • [Developer guide](docs/DEVELOPER_GUIDE.md) • [Database](docs/DATABASE.md) • [User guide](docs/USER_GUIDE.md) • [Troubleshooting](docs/TROUBLESHOOTING.md) • [Development](docs/DEVELOPMENT.md) • [Security](SECURITY.md) • [Contributing](CONTRIBUTING.md) • [Release](docs/RELEASE.md) • [Phase 5 validation](docs/PHASE5_VALIDATION.md)

</div>

<br/>

## 🌟 Overview

**ForgeDesk** is a local-first desktop workspace for C# and .NET engineers. Built natively with **WPF** and **.NET 8**, it prioritizes privacy, deterministic analysis, and clean software architecture. The current application version is **1.0.0**.

- 🔒 **100% Local-First & Private**: Zero mandatory cloud services or telemetry. Your proprietary codebase stays strictly on your machine.
- 🚀 **Safe Onboarding**: Validates folder structures, inspects solution/project boundaries (`.sln`, `.slnx`, `.csproj`), and scans source metadata without storing source contents in the database.
- 💾 **Local Persistence**: Powered by **Entity Framework Core + SQLite** with automatic migrations and ACID transactions.
- 🎨 **Adaptive Theme Shell**: Native Windows fluent UI shell with instant Light/Dark mode switching and custom vector icons.
- 🧩 **Clean Architecture**: Decoupled Domain, Application, Infrastructure, Presentation, and App composition layers with strict dependency inversion.
- 🛡️ **Recovery and Release Hardening**: Consistent SQLite backups before pending migrations, bounded local logs, locked dependency restore, and a repeatable self-contained Windows ZIP package.

---

## 🏗️ Architecture & Solution Layout

```text
ForgeDesk/
├── 🖥️ ForgeDesk.App            # WPF host, Composition root, DI, Window Lifecycle, Native Folder Picker
├── 🎨 ForgeDesk.Presentation   # MVVM ViewModels, Custom Controls, Resource Dictionaries, Themes
├── ⚙️ ForgeDesk.Application    # Use Cases, Orchestration, Validation, DTOs, Project Workspace Service
├── 💎 ForgeDesk.Domain         # Core Entities, Value Objects, Domain Enums, Invariants
├── 🗄️ ForgeDesk.Infrastructure # EF Core SQLite, Migrations, Repositories, File System Scanners
├── 🔬 ForgeDesk.Analysis       # (Phase 2) MSBuild/Roslyn semantic analysis and quality rules
├── 🕸️ ForgeDesk.Graph          # (Phase 3) Persisted graph contracts, filtering, and layout
├── 🔗 Local Git + reports       # (Phase 4) Read-only repository context and safe exports
└── 🧪 ForgeDesk.Tests/         # Deterministic Unit & Integration Test Suites
    ├── ForgeDesk.UnitTests
    └── ForgeDesk.IntegrationTests
```

---

## ⚡ Quick Start

### 📋 Prerequisites

- **OS**: Windows 10 / 11 (for native WPF desktop shell)
- **SDK**: [.NET SDK 8.0.416+](https://dotnet.microsoft.com/download/dotnet/8.0) (Pinned via `global.json`)
- **Git**: Git 2.x on `PATH` for local repository inspection and Git integration tests
- **IDE**: Visual Studio 2022 (with *.NET Desktop Development* workload) or VS Code / JetBrains Rider

---

### 🚀 Running via Visual Studio

1. Open **`ForgeDesk.sln`** in Visual Studio.
2. In the **Solution Explorer**, right-click **`ForgeDesk.App`** and select **Set as Startup Project**.
   > ⚠️ *Note: Do not run `ForgeDesk.Domain` or other class libraries directly.*
3. Press **`F5`** or click the green **▶ Play** button (`ForgeDesk.App`).

---

### 💻 Running via CLI

From PowerShell or Terminal at the repository root:

```powershell
# 1. Restore locked dependencies
dotnet restore ForgeDesk.sln --locked-mode

# 2. Build the solution
dotnet build ForgeDesk.sln --configuration Release

# 3. Launch the desktop application
dotnet run --project ForgeDesk.App/ForgeDesk.App.csproj --configuration Release
```

### 🔎 Analysis, graph, and issue workspace

After onboarding or reopening a project from **Projects**, use **Code Explorer**,
**Dependencies**, **Code Health**, **Git Changes**, or **Reports** to work with the
latest persisted analysis
snapshot. **Start analysis** invokes the application analysis service through
the App adapter, reports progress, and persists real symbol, dependency, and
finding rows. **Dependencies** renders those persisted symbols and edges in a
native WPF canvas with search, symbol-kind/relationship/direction/depth and
isolated-node filters, selection details, pan/zoom, Fit, and Reset. **Code
Health** provides the persisted issue explorer with severity/rule/file search,
sorting, source preview, and symbol navigation. Findings and symbols display
validated relative source paths and line ranges.

Use **Refresh** to reread persisted data, or **Cancel** to stop an in-flight
analysis. Projects without analysis rows show an explicit empty state, while
incomplete scans show partial data and persistence failures show an error state.
See [the Phase 3 implementation notes](docs/PHASE3_IMPLEMENTATION.md) for the
graph semantics, source validation, and Windows manual verification procedure.

**Git Changes** reads the nearest local repository using the Git executable with
bounded, no-shell, read-only commands. It shows repository state, branch/HEAD
metadata, staged/unstaged/untracked/conflicted paths, and bounded recent commits.
Projects without Git continue to work and show an explicit not-a-repository state;
ForgeDesk never stages, commits, resets, checks out, pulls, or pushes files.

**Reports** builds from an explicitly selected persisted analysis run. It can
export safe, self-contained JSON, HTML, or findings CSV files through the native
save dialog, redacts absolute paths by default, includes limitations and optional
current Git context, and compares a selected older run with the current run using
stable finding fingerprints. Report-time Git context is not presented as a
historical commit association because the current schema does not persist one.
See [the Git core](docs/PHASE4_GIT_CORE.md) and [report notes](docs/PHASE4_REPORTS.md)
for limits, privacy behavior, and comparison semantics.

---

## 🧪 Testing

Deterministic test execution against isolated temporary SQLite databases. The
canonical repository check runs both suites through the locked build:

```powershell
# Run the canonical restore, build, and unit/integration checks
make test

# Run an individual suite after the locked build
dotnet test ForgeDesk.UnitTests/ForgeDesk.UnitTests.csproj --configuration Release --no-build --no-restore

# Run Integration Tests
dotnet test ForgeDesk.IntegrationTests/ForgeDesk.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

---

## 📦 Windows Deployment / Publishing

From the repository root, create the repeatable Release `win-x64` package in PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\package-release.ps1
```

The script restores with `--locked-mode`, publishes a self-contained multi-file build,
and explicitly disables single-file publishing, trimming, and Native AOT. It removes
stale packaging output, validates the executable/runtime/native SQLite files, rejects
databases, logs, secrets, and signing files, and creates:

- `artifacts/publish/win-x64/` — the unpacked publish directory
- `artifacts/ForgeDesk-win-x64.zip` — the distributable ZIP

`make package-release` performs the same Release `win-x64` publish and ZIP assembly on
systems with `dotnet` and `zip` available. Install or extract the package wherever
appropriate; ForgeDesk keeps its database and logs outside that directory under
`%LOCALAPPDATA%\ForgeDesk`.

The package is a multi-file ZIP, not an installer. Code signing, automatic updates, and
native Windows UI verification are not configured in this repository; see the
[Phase 5 validation report](docs/PHASE5_VALIDATION.md) for the exact tested and
unverified scenarios.

---

## 🗺️ Roadmap & Phase Progression

- [x] **Phase 1: Native Foundation & Onboarding**
  - WPF MVVM Shell with Theme Manager (Dark / Light)
  - Resilient SQLite schema and automatic migrations
  - Safe folder discovery, scan lifecycle, and metadata tracking
  - Recent projects history with graceful record management
- [x] **Phase 2: WPF analysis integration**
  - Persisted symbol, dependency, and finding counts
  - Findings list with relative path and line display
  - Empty, partial, error, progress, and cancellation states
- [x] **Phase 2: Roslyn Code Analysis Engine**
  - MSBuildWorkspace loading for `.sln`, `.slnx`, and `.csproj` inputs
  - Explicit syntax-only fallback when project-aware loading is unavailable
  - Deterministic symbols, in-scan semantic relationships, and compiler diagnostics
  - Configurable TODO/FIXME, long-method, excessive-parameter, and empty-catch rules
  - Persisted running, completed, failed, and cancelled analysis lifecycle states
- [x] **Phase 3: Interactive Architecture Graphs, Issues, and Source Preview**
  - Persisted dependency graph with deterministic layout, pan/zoom, selection, and filters
  - Symbol details, related finding navigation, and scan-scoped graph states
  - Finding search/filter/sort and safe read-only source preview with line highlighting
- [x] **Phase 4: Git Integration & Intelligent Reports**
  - [x] Read-only local Git core: nearest repository detection, status, worktrees, HEAD metadata, and bounded history
  - [x] Scan-scoped deterministic JSON, self-contained HTML, and CSV findings exports
  - [x] Read-only comparison of persisted analysis runs with stable finding fingerprints
  - [x] Native WPF Git Changes and Reports pages with export/comparison workflow
- [x] **Phase 5: Hardening and release candidate**
  - [x] SQLite online backup/recovery and migration safety checks
  - [x] Bounded local logging and measured bulk-result deletion improvement
  - [x] Vulnerability audit, locked dependency updates, and reproducible `win-x64` ZIP packaging
  - [x] Security, user, developer, troubleshooting, release, and validation documentation
  - [ ] Native Windows UI/accessibility/package-launch verification (requires Windows)
  - [ ] Optional local AI assistance (deferred; core application remains independent)

---

## 🤝 Contributing & License

Contributions, feedback, and issue submissions are welcome!

Read [CONTRIBUTING.md](CONTRIBUTING.md) before making a change. Security reports
should follow [SECURITY.md](SECURITY.md) rather than a public issue.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'feat: add amazing feature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

Distributed under the **MIT License**. See `LICENSE` for details.

---

<div align="center">
  <sub>Maintained by <a href="https://github.com/gauravbhati191">gauravbhati191</a> • Built with ❤️ for the .NET developer ecosystem.</sub>
</div>
