using ForgeDesk.Domain;

namespace ForgeDesk.Presentation.Models;

/// <summary>Persisted counts, findings, and discovery/analysis state for one project's latest scan.</summary>
public sealed record ProjectAnalysisSnapshot(
    Guid ProjectId,
    string ProjectName,
    string RootPath,
    Guid? ScanId,
    ScanStatus? ScanStatus,
    int FilesDiscovered,
    int FilesProcessed,
    int SymbolCount,
    int DependencyCount,
    int FindingCount,
    IReadOnlyList<AnalysisFindingItem> Findings,
    string? ScanErrorMessage = null,
    AnalysisStatus? AnalysisStageStatus = null,
    string? AnalysisErrorMessage = null)
{
    public bool HasPersistedResults => SymbolCount > 0 || DependencyCount > 0 || FindingCount > 0;

    public bool IsPartial => ScanStatus is not null
        && (ScanStatus != ForgeDesk.Domain.ScanStatus.Completed || FilesProcessed < FilesDiscovered);

    public string ScanStatusLabel => ScanStatus?.ToString() ?? "No scan";

    public string AnalysisStatusLabel => AnalysisStageStatus?.ToString() ?? "Not started";
}
