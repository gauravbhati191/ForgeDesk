using ForgeDesk.Application;

namespace ForgeDesk.Presentation.Contracts;

/// <summary>Presentation boundary for selected-run reports and read-only comparisons.</summary>
public interface IProjectReportService
{
    Task<IReadOnlyList<AnalysisRunDto>> ListRunsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<AnalysisReportModel> BuildAsync(
        Guid projectId,
        Guid scanId,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<AnalysisReportExportResult> ExportAsync(
        Guid projectId,
        Guid scanId,
        AnalysisReportFormat format,
        string outputPath,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<AnalysisComparisonResult> CompareAsync(
        Guid projectId,
        Guid baselineScanId,
        Guid currentScanId,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default);
}
