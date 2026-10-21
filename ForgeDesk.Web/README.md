# ForgeDesk.Web

`ForgeDesk.Web` is the browser companion for the existing ForgeDesk WPF
desktop application. It is an ASP.NET Core .NET 8 host written in C# and keeps
the desktop projects independent.

## Run locally

From the repository root:

```powershell
dotnet run --project ForgeDesk.Web/ForgeDesk.Web.csproj --configuration Release
```

Then open <http://localhost:5000>.

The web host initializes the existing EF Core SQLite database, serves the
dashboard from `wwwroot`, and exposes the application services through JSON
endpoints. It does not reference `ForgeDesk.App` or `ForgeDesk.Presentation`.

## Configuration

The default database location comes from the shared infrastructure path
provider. Set `ForgeDesk__DatabasePath` to use a custom database file:

```powershell
$env:ForgeDesk__DatabasePath = "C:\\Data\\forgedesk-web.db"
dotnet run --project ForgeDesk.Web/ForgeDesk.Web.csproj
```

Project paths entered in the browser are paths on the machine running this
web host. The included host is intended for local development and should not
be exposed publicly without adding authentication and a server-side path
authorization policy.
