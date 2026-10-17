using ForgeDesk.Application;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.Presentation.Contracts;

/// <summary>
/// Adapts the application Git service to the presentation boundary without adding
/// repository commands or mutation capabilities to the WPF layer.
/// </summary>
public sealed class GitRepositoryPresentationService : IGitRepositoryPresentationService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GitRepositoryPresentationService(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    public async Task<GitRepositorySnapshot> InspectAsync(
        string projectRoot,
        GitInspectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IGitRepositoryService>()
            .InspectAsync(projectRoot, options, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GitHistoryResult> GetHistoryAsync(
        string projectRoot,
        GitHistoryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IGitRepositoryService>()
            .GetHistoryAsync(projectRoot, options, cancellationToken)
            .ConfigureAwait(false);
    }
}
