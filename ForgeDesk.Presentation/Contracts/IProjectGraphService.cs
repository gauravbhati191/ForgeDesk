using ForgeDesk.Graph;

namespace ForgeDesk.Presentation.Contracts;

/// <summary>Loads graph facts for a project through the application graph boundary.</summary>
public interface IProjectGraphService
{
    Task<GraphData?> LoadAsync(
        Guid projectId,
        GraphQueryOptions options,
        CancellationToken cancellationToken = default);
}
