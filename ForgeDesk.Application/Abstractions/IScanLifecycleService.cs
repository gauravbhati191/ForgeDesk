namespace ForgeDesk.Application;

/// <summary>Persists valid scan lifecycle transitions outside the UI.</summary>
public interface IScanLifecycleService
{
    Task<ScanDto> QueueAsync(Guid projectId, string toolVersion, CancellationToken cancellationToken = default);
    Task<ScanDto> StartAsync(Guid scanId, CancellationToken cancellationToken = default);
    Task<ScanDto> CompleteAsync(Guid scanId, int filesDiscovered, int filesProcessed, CancellationToken cancellationToken = default);
    Task<ScanDto> FailAsync(Guid scanId, string errorMessage, CancellationToken cancellationToken = default);
    Task<ScanDto> RequestCancellationAsync(Guid scanId, CancellationToken cancellationToken = default);
    Task<ScanDto> CancelAsync(Guid scanId, CancellationToken cancellationToken = default);
}
