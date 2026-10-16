using ForgeDesk.Application;
using ForgeDesk.Presentation.Models;

namespace ForgeDesk.Presentation.Contracts;

/// <summary>UI-facing boundary for database-backed project and recent-project actions.</summary>
public interface IProjectWorkspaceService
{
    Task<IReadOnlyList<RecentProjectItem>> LoadRecentProjectsAsync(CancellationToken cancellationToken = default);

    Task<ProjectOpenResult> OpenProjectAsync(
        string folderPath,
        IProgress<FileDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task RemoveRecentProjectAsync(string folderPath, CancellationToken cancellationToken = default);
}
