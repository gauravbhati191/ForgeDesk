using ForgeDesk.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Web.Services;

public sealed class AnalysisWorker : BackgroundService
{
    private readonly AnalysisJobQueue _queue;
    private readonly AnalysisJobStatusStore _statusStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalysisWorker> _logger;

    public AnalysisWorker(
        AnalysisJobQueue queue,
        AnalysisJobStatusStore statusStore,
        IServiceScopeFactory scopeFactory,
        ILogger<AnalysisWorker> logger)
    {
        _queue = queue;
        _statusStore = statusStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(job, stoppingToken);
        }
    }

    private async Task ProcessAsync(AnalysisJob job, CancellationToken stoppingToken)
    {
        _statusStore.Start(job);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var analysis = scope.ServiceProvider.GetRequiredService<IAnalysisService>();
            var progress = new Progress<AnalysisProgress>(value => _statusStore.Report(job, value));
            var result = await analysis.AnalyzeAsync(job.ScanId, progress: progress, cancellationToken: stoppingToken);
            _statusStore.Complete(job, result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _statusStore.Fail(job, "Analysis was cancelled because the web host is shutting down.", "Cancelled");
        }
        catch (OperationCanceledException)
        {
            _statusStore.Fail(job, "Analysis was cancelled.", "Cancelled");
        }
        catch (Exception exception)
        {
            _statusStore.Fail(job, exception.Message);
            _logger.LogError(exception, "Web analysis job failed for scan {ScanId}.", job.ScanId);
        }
    }
}
