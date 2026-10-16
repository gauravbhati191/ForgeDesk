namespace ForgeDesk.Application;

/// <summary>Metadata for one eligible file found under a project root.</summary>
public sealed record DiscoveredFile(
    string NormalizedPath,
    string RelativePath,
    string FileName,
    string Extension,
    long FileSizeBytes,
    DateTimeOffset LastModifiedAtUtc,
    string Language,
    string? ContentHash = null)
{
    public string FullPath => NormalizedPath;
    public long Length => FileSizeBytes;
    public DateTimeOffset LastModifiedAt => LastModifiedAtUtc;
}
