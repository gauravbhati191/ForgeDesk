using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Persisted status and counts for the semantic-analysis stage of a scan.</summary>
public sealed record AnalysisRunDto(
    Guid ScanId,
    AnalysisStatus Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorMessage,
    int FilesProcessed,
    int SymbolCount,
    int DependencyCount,
    int FindingCount,
    AnalysisBindingMode? BindingMode = null,
    string? BindingMessage = null)
{
    public static AnalysisRunDto From(
        Scan scan,
        AnalysisBindingMode? bindingMode = null,
        string? bindingMessage = null)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return new(
            scan.Id,
            scan.AnalysisStatus,
            scan.AnalysisStartedAtUtc,
            scan.AnalysisCompletedAtUtc,
            scan.AnalysisErrorMessage,
            scan.AnalysisFilesProcessed,
            scan.AnalysisSymbolCount,
            scan.AnalysisDependencyCount,
            scan.FindingCount,
            bindingMode,
            bindingMessage);
    }
}
