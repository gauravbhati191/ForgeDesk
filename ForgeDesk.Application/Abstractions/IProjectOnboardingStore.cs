using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Transactional persistence boundary for the project onboarding workflow.</summary>
public interface IProjectOnboardingStore
{
    Task BeginAsync(Project project, Scan scan, CancellationToken cancellationToken = default);
    Task SaveRunningAsync(Scan scan, CancellationToken cancellationToken = default);
    Task CompleteAsync(Project project, Scan scan, IReadOnlyCollection<SourceFile> sourceFiles, CancellationToken cancellationToken = default);
    Task SaveFailedAsync(Scan scan, CancellationToken cancellationToken = default);
    Task SaveCancelledAsync(Scan scan, CancellationToken cancellationToken = default);
}
