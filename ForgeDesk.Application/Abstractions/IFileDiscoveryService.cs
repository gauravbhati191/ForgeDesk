namespace ForgeDesk.Application;

/// <summary>Discovers eligible files beneath a project root while enforcing filesystem safety boundaries.</summary>
public interface IFileDiscoveryService
{
    Task<FileDiscoveryResult> DiscoverAsync(
        string rootPath,
        FileDiscoveryOptions? options = null,
        IProgress<FileDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
