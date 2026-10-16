using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ForgeDesk.Infrastructure;

/// <summary>Design-time factory used by EF migration tooling.</summary>
public sealed class ForgeDeskDbContextFactory : IDesignTimeDbContextFactory<ForgeDeskDbContext>
{
    public ForgeDeskDbContext CreateDbContext(string[] args)
    {
        var path = Environment.GetEnvironmentVariable("FORGEDESK_DATABASE_PATH") ?? args.FirstOrDefault(argument => argument.StartsWith("--database=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var provider = new DatabasePathProvider(string.IsNullOrWhiteSpace(path) ? Path.Combine(Path.GetTempPath(), "forgedesk-design.db") : path);
        provider.EnsureDirectoryExists();
        var options = new DbContextOptionsBuilder<ForgeDeskDbContext>()
            .UseSqlite(provider.ConnectionString, sqlite => sqlite.MigrationsAssembly(typeof(ForgeDeskDbContext).Assembly.GetName().Name!))
            .Options;
        return new ForgeDeskDbContext(options);
    }
}
