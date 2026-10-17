namespace ForgeDesk.Application;

/// <summary>Reads one selected persisted analysis run and renders safe report formats.</summary>
public interface IAnalysisReportService
{
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
}

/// <summary>Infrastructure boundary for a bounded, scan-scoped report data query.</summary>
public interface IAnalysisReportDataSource
{
    Task<AnalysisReportData?> GetAsync(
        Guid projectId,
        Guid scanId,
        AnalysisReportQueryOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>Read-only comparison boundary for two persisted analysis scans.</summary>
public interface IAnalysisComparisonService
{
    Task<AnalysisComparisonResult> CompareAsync(
        Guid projectId,
        Guid baselineScanId,
        Guid currentScanId,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default);
}
