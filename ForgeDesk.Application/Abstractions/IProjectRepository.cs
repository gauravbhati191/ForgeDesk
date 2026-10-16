using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Persistence operations for registered projects and recent-project state.</summary>
public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Project?> GetByNormalizedPathAsync(string normalizedRootPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> ListRecentAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Project project, CancellationToken cancellationToken = default);
    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
    Task RemoveFromRecentAsync(string normalizedRootPath, CancellationToken cancellationToken = default);
}
