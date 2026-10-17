using ForgeDesk.Application;

namespace ForgeDesk.Presentation.Contracts;

/// <summary>
/// UI-facing boundary for read-only local Git inspection.
///
/// The presentation layer deliberately keeps the application Git service behind this
/// contract so the Git page can be tested with a deterministic reader without coupling
/// its viewmodel to infrastructure or WPF code-behind.
/// </summary>
public interface IGitRepositoryPresentationService
{
    Task<GitRepositorySnapshot> InspectAsync(
        string projectRoot,
        GitInspectionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<GitHistoryResult> GetHistoryAsync(
        string projectRoot,
        GitHistoryOptions? options = null,
        CancellationToken cancellationToken = default);
}
