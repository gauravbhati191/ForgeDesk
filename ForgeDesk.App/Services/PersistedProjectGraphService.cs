using ForgeDesk.Application;
using ForgeDesk.Graph;
using ForgeDesk.Presentation.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.App.Services;

/// <summary>
/// Adapts the scoped application graph query service to the long-lived WPF
/// graph viewmodel. Every query uses a fresh scope and is project/scan scoped.
/// </summary>
public sealed class PersistedProjectGraphService : IProjectGraphService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PersistedProjectGraphService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task<GraphData?> LoadAsync(
        Guid projectId,
        GraphQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || options is null || options.ScanId == Guid.Empty)
        {
            return null;
        }

        var scopedOptions = options with { ProjectId = projectId };
        using var scope = _scopeFactory.CreateScope();
        var graphService = scope.ServiceProvider.GetRequiredService<IGraphQueryService>();
        return await graphService.GetAsync(scopedOptions, cancellationToken).ConfigureAwait(false);
    }
}
