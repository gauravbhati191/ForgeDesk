namespace ForgeDesk.Presentation.Models;

/// <summary>One line in a read-only source preview.</summary>
public sealed record SourcePreviewLine(int Number, string Text, bool IsSelected);

/// <summary>Result of safe source resolution and preview loading.</summary>
public sealed record SourcePreviewResult(
    bool Succeeded,
    string? RelativePath,
    string? FullPath,
    int? SelectedLine,
    IReadOnlyList<SourcePreviewLine> Lines,
    int TotalLineCount,
    bool IsTruncated,
    string? ErrorMessage = null)
{
    public static SourcePreviewResult Failure(string message)
        => new(false, null, null, null, Array.Empty<SourcePreviewLine>(), 0, false, message);
}
