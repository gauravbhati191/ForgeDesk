namespace ForgeDesk.Domain;

/// <summary>Metadata for a file discovered during a scan. File contents are never stored here.</summary>
public sealed class SourceFile
{
    private SourceFile()
    {
        RelativePath = string.Empty;
        FileName = string.Empty;
        Extension = string.Empty;
        Language = string.Empty;
        NormalizedPath = string.Empty;
    }

    private SourceFile(
        Guid id,
        Guid scanId,
        string relativePath,
        string fileName,
        string extension,
        long fileSizeBytes,
        DateTimeOffset lastModifiedAtUtc,
        string language,
        DiscoveryStatus discoveryStatus,
        string? contentHash,
        string? normalizedPath)
    {
        if (id == Guid.Empty || scanId == Guid.Empty)
        {
            throw new DomainInvariantException("A source-file and scan identifier are required.");
        }

        if (fileSizeBytes < 0)
        {
            throw new DomainInvariantException("A source-file size cannot be negative.");
        }

        Id = id;
        ScanId = scanId;
        RelativePath = PathNormalization.NormalizeRelative(relativePath);
        FileName = ValidateText(fileName, "A file name is required.", 512);
        Extension = NormalizeExtension(extension);
        FileSizeBytes = fileSizeBytes;
        LastModifiedAtUtc = lastModifiedAtUtc;
        Language = ValidateText(language, "A file language is required.", 128);
        DiscoveryStatus = discoveryStatus;
        ContentHash = string.IsNullOrWhiteSpace(contentHash) ? null : contentHash.Trim();
        NormalizedPath = string.IsNullOrWhiteSpace(normalizedPath)
            ? RelativePath.ToUpperInvariant()
            : PathNormalization.Normalize(normalizedPath);
    }

    public static SourceFile Create(
        Guid scanId,
        string relativePath,
        string fileName,
        string extension,
        long fileSizeBytes,
        DateTimeOffset lastModifiedAtUtc,
        string language,
        DiscoveryStatus discoveryStatus = DiscoveryStatus.Discovered,
        string? contentHash = null,
        string? normalizedPath = null)
        => new(Guid.NewGuid(), scanId, relativePath, fileName, extension, fileSizeBytes, lastModifiedAtUtc, language, discoveryStatus, contentHash, normalizedPath);

    public Guid Id { get; private set; }
    public Guid ScanId { get; private set; }
    public string RelativePath { get; private set; }
    public string FileName { get; private set; }
    public string Extension { get; private set; }
    public string? ContentHash { get; private set; }
    public long FileSizeBytes { get; private set; }
    public DateTimeOffset LastModifiedAtUtc { get; private set; }
    public string Language { get; private set; }
    public DiscoveryStatus DiscoveryStatus { get; private set; }
    public string NormalizedPath { get; private set; }

    // Compatibility aliases used by early test fixtures.
    public long Size => FileSizeBytes;
    public DateTimeOffset LastModifiedAt => LastModifiedAtUtc;
    public int? LineCount { get; private set; }

    public Scan? Scan { get; private set; }
    public ICollection<Symbol> Symbols { get; private set; } = new List<Symbol>();
    public ICollection<Finding> Findings { get; private set; } = new List<Finding>();

    private static string ValidateText(string text, string message, int maxLength)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new DomainInvariantException(message);
        }

        if (value.Length > maxLength)
        {
            throw new DomainInvariantException($"{message.TrimEnd('.')} cannot exceed {maxLength} characters.");
        }

        return value;
    }

    private static string NormalizeExtension(string extension)
    {
        var value = ValidateText(extension, "A file extension is required.", 32);
        return value.StartsWith(".", StringComparison.Ordinal) ? value.ToLowerInvariant() : "." + value.ToLowerInvariant();
    }
}
