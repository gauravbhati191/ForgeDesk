using ForgeDesk.Application;
using ForgeDesk.Infrastructure;
using ForgeDesk.Presentation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.App.Services;

/// <summary>
/// Adapts the scoped report and comparison services to the long-lived WPF report page.
/// Each operation gets a fresh scope and therefore never retains an EF context.
/// </summary>
public sealed class PersistedProjectReportService : IProjectReportService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PersistedProjectReportService(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    public async Task<IReadOnlyList<AnalysisRunDto>> ListRunsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            return Array.Empty<AnalysisRunDto>();
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ForgeDeskDbContext>();
        var scans = await dbContext.Scans
            .AsNoTracking()
            .Where(scan => scan.ProjectId == projectId)
            .OrderByDescending(scan => scan.AnalysisCompletedAtUtc)
            .ThenByDescending(scan => scan.QueuedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return scans.Select(scan => AnalysisRunDto.From(scan)).ToArray();
    }

    public Task<AnalysisReportModel> BuildAsync(
        Guid projectId,
        Guid scanId,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default)
        => WithReportServiceAsync(
            service => service.BuildAsync(projectId, scanId, options, cancellationToken));

    public Task<AnalysisReportExportResult> ExportAsync(
        Guid projectId,
        Guid scanId,
        AnalysisReportFormat format,
        string outputPath,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default)
        => WithReportServiceAsync(
            service => service.ExportAsync(projectId, scanId, format, outputPath, options, cancellationToken));

    public Task<AnalysisComparisonResult> CompareAsync(
        Guid projectId,
        Guid baselineScanId,
        Guid currentScanId,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default)
        => WithComparisonServiceAsync(
            service => service.CompareAsync(projectId, baselineScanId, currentScanId, options, cancellationToken));

    private async Task<TResult> WithReportServiceAsync<TResult>(
        Func<IAnalysisReportService, Task<TResult>> operation)
    {
        using var scope = _scopeFactory.CreateScope();
        return await operation(scope.ServiceProvider.GetRequiredService<IAnalysisReportService>())
            .ConfigureAwait(false);
    }

    private async Task<TResult> WithComparisonServiceAsync<TResult>(
        Func<IAnalysisComparisonService, Task<TResult>> operation)
    {
        using var scope = _scopeFactory.CreateScope();
        return await operation(scope.ServiceProvider.GetRequiredService<IAnalysisComparisonService>())
            .ConfigureAwait(false);
    }
}
