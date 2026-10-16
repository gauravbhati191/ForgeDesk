namespace ForgeDesk.Infrastructure;

/// <summary>Local persistence configuration. Tests can provide an isolated SQLite path.</summary>
public sealed class ForgeDeskInfrastructureOptions
{
    public string? DatabasePath { get; set; }
    public string? ConnectionString { get; set; }
}
