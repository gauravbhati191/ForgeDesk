using ForgeDesk.Domain;

namespace ForgeDesk.Presentation.Models;

/// <summary>Read-only finding row shown by the analysis pages.</summary>
public sealed record AnalysisFindingItem(
    Guid Id,
    string RuleId,
    FindingSeverity Severity,
    string Message,
    string? RelativePath,
    int? StartLine,
    int? EndLine,
    Guid? SourceFileId = null,
    Guid? SymbolId = null,
    string? SymbolName = null,
    string? SymbolKey = null,
    Guid? ScanId = null)
{
    public string SeverityLabel => Severity.ToString();

    public string SummaryLabel => $"{RuleId}  •  {SeverityLabel}";

    public string SymbolLabel => string.IsNullOrWhiteSpace(SymbolName)
        ? "No associated symbol"
        : SymbolName;

    public string AnalysisRunLabel => ScanId is Guid scanId
        ? $"Scan {scanId:D}"
        : "Scan unavailable";

    /// <summary>
    /// Displays the persisted source-file relative path and line range. The UI does
    /// not manufacture a command or absolute path for navigation.
    /// </summary>
    public string LocationLabel
    {
        get
        {
            var path = string.IsNullOrWhiteSpace(RelativePath)
                ? "Location unavailable"
                : RelativePath.Replace('\\', '/');

            if (StartLine is null)
            {
                return path;
            }

            return EndLine is not null && EndLine != StartLine
                ? $"{path}:{StartLine}-{EndLine}"
                : $"{path}:{StartLine}";
        }
    }
}
