namespace ForgeDesk.Infrastructure;

/// <summary>Resolves and prepares the per-user application database location.</summary>
public interface IDatabasePathProvider
{
    string DatabasePath { get; }
    string ConnectionString { get; }
    void EnsureDirectoryExists();
}
