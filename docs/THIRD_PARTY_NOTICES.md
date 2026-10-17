# ForgeDesk third-party notices

ForgeDesk is distributed under the MIT license in [`LICENSE`](../LICENSE). The following direct and important transitive dependencies are used by the current solution. Their own license terms continue to apply to redistributed binaries.

## Runtime and application libraries

| Package/family | Version in the locked restore | Purpose | License/source |
|---|---:|---|---|
| .NET 8 / WPF | SDK `8.0.416` | Runtime, Windows desktop UI, base libraries | MIT and Microsoft .NET license terms; [dotnet](https://dotnet.microsoft.com/) |
| CommunityToolkit.Mvvm | 8.3.2 | MVVM source generators and observable commands | MIT; [NuGet](https://www.nuget.org/packages/CommunityToolkit.Mvvm) |
| Microsoft.Extensions.* | 8.0.x | Hosting, dependency injection, logging, configuration | MIT; [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Hosting) |
| Entity Framework Core SQLite/Design | 8.0.32 | SQLite persistence, migrations, design-time tooling | MIT; [EF Core](https://github.com/dotnet/efcore) |
| Microsoft.Data.Sqlite / SQLitePCLRaw | 8.0.32 / 2.1.13 resolved through EF Core | SQLite access and native SQLite packaging | Microsoft.Data.Sqlite: MIT; SQLitePCLRaw: Apache-2.0; [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) |
| Microsoft.CodeAnalysis / Roslyn Workspaces | 4.5.0 | C# syntax, semantic analysis, and project loading | MIT; [Roslyn](https://github.com/dotnet/roslyn) |
| Microsoft.Build.Locator | 1.7.8 | Locating MSBuild assets for Roslyn project loading | MIT; [NuGet](https://www.nuget.org/packages/Microsoft.Build.Locator) |
| System.Text.Json | 9.0.0 (explicit compatibility pin) | Safe JSON serialization for the application/design-time dependency graph | MIT; [dotnet/runtime](https://github.com/dotnet/runtime) |

## Test-only packages

| Package/family | Version | Purpose | License/source |
|---|---:|---|---|
| xUnit | 2.9.2 | Unit and integration test framework | Apache-2.0; [xUnit](https://xunit.net/) |
| Microsoft.NET.Test.Sdk | 17.11.1 | .NET test host integration | MIT; [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk) |
| xunit.runner.visualstudio | 2.8.2 | Visual Studio test adapter | Apache-2.0; [NuGet](https://www.nuget.org/packages/xunit.runner.visualstudio) |

The complete resolved graph, including package hashes, is recorded in each project's `packages.lock.json`. Use the repository's package tooling to inspect the exact graph for a release rather than relying only on this summary.

## Vulnerability audit

The Phase 5 audit command was:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./.dotnet/dotnet list ForgeDesk.sln package --vulnerable --include-transitive
```

At the final audit, all nine projects reported no vulnerable packages using the configured NuGet source. Earlier in the work, the audit identified vulnerable `SQLitePCLRaw.lib.e_sqlite3` and `System.Text.Json` resolutions. EF Core was updated to `8.0.32`, the local `dotnet-ef` tool to `8.0.32`, and a safe `System.Text.Json` 9.0.0 package was pinned where the older Roslyn/design-time graph otherwise selected 6.0.0. The full locked restore and test suite passed after the change.

An audit is time-sensitive. Run it again before publishing because advisory databases, package feeds, and package graphs can change.

## Native components and notices

The self-contained Windows publish includes the native SQLite library delivered by the SQLitePCLRaw bundle. Do not remove its license/notice requirements when redistributing the package. If a future release adds an installer or additional native component, update this file with that component's license and source before packaging.
