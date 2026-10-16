using ForgeDesk.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.IntegrationTests;

internal sealed class SqliteTestHarness : IAsyncDisposable
{
    private readonly string _directory;

    private SqliteTestHarness(string directory, ForgeDeskDbContext context)
    {
        _directory = directory;
        Context = context;
    }

    public ForgeDeskDbContext Context { get; }
    public string DatabasePath => System.IO.Path.Combine(_directory, "forgedesk.db");

    public static async Task<SqliteTestHarness> CreateAsync()
    {
        var directory = Directory.CreateTempSubdirectory("forgedesk-sqlite-").FullName;
        var databasePath = System.IO.Path.Combine(directory, "forgedesk.db");
        var options = new DbContextOptionsBuilder<ForgeDeskDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var context = new ForgeDeskDbContext(options);
        await context.Database.MigrateAsync();
        return new SqliteTestHarness(directory, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
