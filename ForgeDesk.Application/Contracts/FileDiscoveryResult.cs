namespace ForgeDesk.Application;

/// <summary>Result of a cancellable metadata discovery pass.</summary>
public sealed record FileDiscoveryResult(
    IReadOnlyList<DiscoveredFile> Files,
    IReadOnlyList<string> SkippedPaths,
    IReadOnlyList<string> DuplicatePaths,
    int DirectoriesVisited)
{
    public static FileDiscoveryResult Empty { get; } = new(Array.Empty<DiscoveredFile>(), Array.Empty<string>(), Array.Empty<string>(), 0);
}
