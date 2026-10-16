<div align="center">

# ⚡ ForgeDesk

**A local-first, high-performance Windows developer workspace & C# project intelligence suite.**

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0_LTS-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/Platform-Windows_WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![SQLite](https://img.shields.io/badge/Database-SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white)](https://www.sqlite.org/)
[![License: MIT](https://img.shields.io/badge/License-MIT-success?style=for-the-badge)](LICENSE)

*Safe onboarding, metadata discovery, persistent scan lifecycle records, and modern fluent UI shell.*

---

[Repository](https://github.com/gauravbhati191/ForgeDesk) • [Architecture](docs/ARCHITECTURE.md) • [Database](docs/DATABASE.md) • [Development](docs/DEVELOPMENT.md) • [Implementation](docs/PHASE1_IMPLEMENTATION.md)

</div>

<br/>

## 🌟 Overview

**ForgeDesk** is an enterprise-grade desktop workspace crafted for C# and .NET engineers. Built natively with **WPF** and **.NET 8**, it prioritizes speed, local privacy, and clean software architecture.

- 🔒 **100% Local-First & Private**: Zero mandatory cloud services or telemetry. Your proprietary codebase stays strictly on your machine.
- 🚀 **Lightning Fast Safe Onboarding**: Validates folder structures, inspects solution/project boundaries (`.sln`, `.slnx`, `.csproj`), and scans source trees without touching disk contents.
- 💾 **Local Persistence**: Powered by **Entity Framework Core + SQLite** with automatic migrations and ACID transactions.
- 🎨 **Adaptive Theme Shell**: Native Windows fluent UI shell with instant Light/Dark mode switching and custom vector icons.
- 🧩 **Clean Architecture**: Decoupled Domain, Application, Infrastructure, Presentation, and App composition layers with strict dependency inversion.

---

## 🏗️ Architecture & Solution Layout

```text
ForgeDesk/
├── 🖥️ ForgeDesk.App            # WPF host, Composition root, DI, Window Lifecycle, Native Folder Picker
├── 🎨 ForgeDesk.Presentation   # MVVM ViewModels, Custom Controls, Resource Dictionaries, Themes
├── ⚙️ ForgeDesk.Application    # Use Cases, Orchestration, Validation, DTOs, Project Workspace Service
├── 💎 ForgeDesk.Domain         # Core Entities, Value Objects, Domain Enums, Invariants
├── 🗄️ ForgeDesk.Infrastructure # EF Core SQLite, Migrations, Repositories, File System Scanners
├── 🔬 ForgeDesk.Analysis       # (Phase 2) Roslyn Code Analysis Contracts
├── 🕸️ ForgeDesk.Graph          # (Phase 2) Dependency & Type Graph Contracts
└── 🧪 ForgeDesk.Tests/         # Deterministic Unit & Integration Test Suites
    ├── ForgeDesk.UnitTests
    └── ForgeDesk.IntegrationTests
```

---

## ⚡ Quick Start

### 📋 Prerequisites

- **OS**: Windows 10 / 11 (for native WPF desktop shell)
- **SDK**: [.NET SDK 8.0.416+](https://dotnet.microsoft.com/download/dotnet/8.0) (Pinned via `global.json`)
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
dotnet restore ForgeDesk.sln

# 2. Build the solution
dotnet build ForgeDesk.sln --configuration Release

# 3. Launch the desktop application
dotnet run --project ForgeDesk.App/ForgeDesk.App.csproj --configuration Release
```

---

## 🧪 Testing

Deterministic test execution against isolated temporary SQLite databases:

```powershell
# Run Unit Tests
dotnet test ForgeDesk.UnitTests/ForgeDesk.UnitTests.csproj --configuration Release

# Run Integration Tests
dotnet test ForgeDesk.IntegrationTests/ForgeDesk.IntegrationTests.csproj --configuration Release
```

*Or via `make` (if installed):*
```bash
make test
```

---

## 📦 Windows Deployment / Publishing

Create an optimized, self-contained or framework-dependent Windows release package:

```powershell
dotnet publish ForgeDesk.App/ForgeDesk.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output artifacts/publish/win-x64
```

Database files are cleanly stored in `%LOCALAPPDATA%\ForgeDesk\forgedesk.db`, keeping binaries isolated from application state.

---

## 🗺️ Roadmap & Phase Progression

- [x] **Phase 1: Native Foundation & Onboarding** *(Current)*
  - WPF MVVM Shell with Theme Manager (Dark / Light)
  - Resilient SQLite schema and automatic migrations
  - Safe folder discovery, scan lifecycle, and metadata tracking
  - Recent projects history with graceful record management
- [ ] **Phase 2: Roslyn Code Analysis Engine**
  - Syntax and symbol diagnostics
  - Complexity, maintainability, and quality metrics
- [ ] **Phase 3: Interactive Architecture Graphs**
  - Dependency visualizer and coupling matrices
- [ ] **Phase 4: Git Integration & Intelligent Reports**

---

## 🤝 Contributing & License

Contributions, feedback, and issue submissions are welcome!

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
