namespace ForgeDesk.Application;

/// <summary>Options controlling safe source-tree discovery.</summary>
public sealed class FileDiscoveryOptions
{
    private static readonly string[] DefaultExcludedDirectories = [".git", "bin", "obj", ".vs", ".idea", "TestResults", "packages"];
    private static readonly string[] DefaultIncludedExtensions = [".cs", ".csproj", ".sln", ".slnx"];

    public IReadOnlySet<string> ExcludedDirectoryNames { get; init; }
        = new HashSet<string>(DefaultExcludedDirectories, StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string>? IncludedExtensions { get; init; }

    public bool ComputeContentHash { get; init; }

    public long MaxHashFileSizeBytes { get; init; } = 2 * 1024 * 1024;

    public static FileDiscoveryOptions SourceFilesAndProjectFiles => new()
    {
        IncludedExtensions = new HashSet<string>(DefaultIncludedExtensions, StringComparer.OrdinalIgnoreCase)
    };

    public static FileDiscoveryOptions SourceFilesOnly => new()
    {
        IncludedExtensions = new HashSet<string>([".cs"], StringComparer.OrdinalIgnoreCase)
    };
}
