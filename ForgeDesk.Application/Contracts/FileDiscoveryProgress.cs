namespace ForgeDesk.Application;

/// <summary>Progress reported while enumerating a project tree.</summary>
public sealed record FileDiscoveryProgress(int FilesDiscovered, int DirectoriesVisited, string? CurrentPath);
