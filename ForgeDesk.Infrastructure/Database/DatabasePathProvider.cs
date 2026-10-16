using Microsoft.Data.Sqlite;

namespace ForgeDesk.Infrastructure;

/// <summary>Default database path provider for local-first ForgeDesk data.</summary>
public sealed class DatabasePathProvider : IDatabasePathProvider
{
    private readonly string _databasePath;

    public DatabasePathProvider(string? databasePath = null)
    {
        var defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForgeDesk");
        _databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? Path.Combine(defaultDirectory, "forgedesk.db")
            : Path.GetFullPath(databasePath);
    }

    public string DatabasePath => _databasePath;

    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true
    }.ToString();

    public void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
