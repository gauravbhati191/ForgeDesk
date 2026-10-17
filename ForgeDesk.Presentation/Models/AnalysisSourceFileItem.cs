namespace ForgeDesk.Presentation.Models;

/// <summary>Source-file metadata used for safe navigation without storing source text.</summary>
public sealed record AnalysisSourceFileItem(
    Guid Id,
    Guid ScanId,
    string RelativePath,
    string FileName,
    string Extension,
    string Language,
    long FileSizeBytes,
    DateTimeOffset LastModifiedAtUtc);
