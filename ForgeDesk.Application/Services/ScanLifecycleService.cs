using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Application service that enforces and persists scan state transitions.</summary>
public sealed class ScanLifecycleService : IScanLifecycleService
{
    private readonly IProjectRepository _projects;
    private readonly IScanRepository _scans;
    private readonly IClock _clock;

    public ScanLifecycleService(IProjectRepository projects, IScanRepository scans, IClock? clock = null)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _scans = scans ?? throw new ArgumentNullException(nameof(scans));
        _clock = clock ?? new SystemClock();
    }

    public async Task<ScanDto> QueueAsync(Guid projectId, string toolVersion, CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException("Project", projectId);
        if (project.Status != ProjectStatus.Active)
        {
            throw new ProjectValidationException(["An archived project cannot be scanned."]);
        }

        var scan = Scan.CreatePending(projectId, toolVersion, _clock.UtcNow);
        await _scans.AddAsync(scan, cancellationToken).ConfigureAwait(false);
        return ScanDto.From(scan);
    }

    public Task<ScanDto> StartAsync(Guid scanId, CancellationToken cancellationToken = default)
        => TransitionAsync(scanId, scan => scan.Start(_clock.UtcNow), cancellationToken);

    public Task<ScanDto> CompleteAsync(Guid scanId, int filesDiscovered, int filesProcessed, CancellationToken cancellationToken = default)
        => TransitionAsync(scanId, scan => scan.Complete(filesDiscovered, filesProcessed, _clock.UtcNow), cancellationToken);

    public Task<ScanDto> FailAsync(Guid scanId, string errorMessage, CancellationToken cancellationToken = default)
        => TransitionAsync(scanId, scan => scan.Fail(errorMessage, _clock.UtcNow), cancellationToken);

    public Task<ScanDto> RequestCancellationAsync(Guid scanId, CancellationToken cancellationToken = default)
        => TransitionAsync(scanId, scan => scan.RequestCancellation(), cancellationToken);

    public Task<ScanDto> CancelAsync(Guid scanId, CancellationToken cancellationToken = default)
        => TransitionAsync(scanId, scan => scan.Cancel(_clock.UtcNow), cancellationToken);

    private async Task<ScanDto> TransitionAsync(Guid scanId, Action<Scan> transition, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scan = await _scans.GetByIdAsync(scanId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException("Scan", scanId);
        transition(scan);
        await _scans.UpdateAsync(scan, cancellationToken).ConfigureAwait(false);
        return ScanDto.From(scan);
    }
}
