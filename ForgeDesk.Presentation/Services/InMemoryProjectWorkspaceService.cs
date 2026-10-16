using System.IO;
using ForgeDesk.Domain;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;

namespace ForgeDesk.Presentation.Services;

/// <summary>Test-only fallback workspace adapter. The desktop app registers its database-backed adapter.</summary>
public sealed class InMemoryProjectWorkspaceService : IProjectWorkspaceService
{
    private readonly object _syncRoot = new();
    private readonly List<RecentProjectItem> _recentProjects = [];

    public Task<IReadOnlyList<RecentProjectItem>> LoadRecentProjectsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult<IReadOnlyList<RecentProjectItem>>(_recentProjects.OrderByDescending(item => item.LastOpened).ToArray());
        }
    }

    public Task<ProjectOpenResult> OpenProjectAsync(string folderPath, IProgress<ForgeDesk.Application.FileDiscoveryProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = string.IsNullOrWhiteSpace(folderPath) ? string.Empty : Path.GetFullPath(folderPath);
        if (string.IsNullOrWhiteSpace(fullPath) || !Directory.Exists(fullPath))
        {
            return Task.FromResult(new ProjectOpenResult(false, fullPath, Path.GetFileName(fullPath), Message: "The selected folder is not available."));
        }
        var info = new DirectoryInfo(fullPath);
        var item = new RecentProjectItem(info.FullName, info.Name, ProjectType.SourceFolder, DateTimeOffset.UtcNow, null, 0, 0, null, false, false);
        lock (_syncRoot)
        {
            _recentProjects.RemoveAll(project => string.Equals(project.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            _recentProjects.Insert(0, item);
        }
        return Task.FromResult(new ProjectOpenResult(true, item.Path, item.DisplayName, Message: "Project folder selected."));
    }

    public Task RemoveRecentProjectAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot) _recentProjects.RemoveAll(project => string.Equals(project.Path, folderPath, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
}
