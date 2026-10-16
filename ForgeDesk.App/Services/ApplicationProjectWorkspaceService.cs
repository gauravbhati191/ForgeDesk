using System.IO;
using ForgeDesk.Application;
using ForgeDesk.Domain;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.App.Services;

/// <summary>Bridges the WPF workspace commands to the database-backed application workflow.</summary>
public sealed class ApplicationProjectWorkspaceService : IProjectWorkspaceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApplicationProjectWorkspaceService> _logger;

    public ApplicationProjectWorkspaceService(IServiceScopeFactory scopeFactory, ILogger<ApplicationProjectWorkspaceService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<RecentProjectItem>> LoadRecentProjectsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var projects = await scope.ServiceProvider.GetRequiredService<IProjectRepository>().ListRecentAsync(cancellationToken).ConfigureAwait(false);
        return projects.Select(ToRecentProject).ToArray();
    }

    public async Task<ProjectOpenResult> OpenProjectAsync(string folderPath, IProgress<FileDiscoveryProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return new ProjectOpenResult(false, folderPath, string.Empty, Message: "Choose a project folder to continue.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folderPath.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new ProjectOpenResult(false, folderPath, string.Empty, Message: "The selected folder path is invalid.");
        }

        var displayName = new DirectoryInfo(fullPath).Name;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var onboarding = scope.ServiceProvider.GetRequiredService<IProjectOnboardingService>();
            var result = await onboarding.OnboardAsync(
                new ProjectOnboardingRequest(displayName, fullPath),
                progress,
                cancellationToken).ConfigureAwait(false);
            var warningText = result.Warnings.Count == 0 ? null : string.Join(" ", result.Warnings);
            return new ProjectOpenResult(
                true,
                result.Project.RootPath,
                result.Project.Name,
                result.Project,
                result.Scan,
                result.SourceFileCount,
                result.SupportedProjectFileCount,
                result.Warnings,
                warningText ?? (result.ReusedExistingProject ? "Project reopened and discovery refreshed." : "Project registered and discovery completed."));
        }
        catch (ProjectValidationException exception)
        {
            _logger.LogWarning("Project validation failed for {ProjectPath}: {Message}", fullPath, exception.Message);
            return new ProjectOpenResult(false, fullPath, displayName, Message: exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Project onboarding failed for {ProjectPath}.", fullPath);
            return new ProjectOpenResult(false, fullPath, displayName, Message: "Project onboarding failed. Check the log for details.");
        }
    }

    public async Task RemoveRecentProjectAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var normalized = PathNormalization.Normalize(folderPath);
        await scope.ServiceProvider.GetRequiredService<IProjectRepository>().RemoveFromRecentAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    private static RecentProjectItem ToRecentProject(Project project)
    {
        var latestScan = project.Scans.OrderByDescending(scan => scan.QueuedAtUtc).FirstOrDefault();
        var isMissing = !Directory.Exists(project.RootPath);
        return new RecentProjectItem(
            project.RootPath,
            project.Name,
            project.ProjectType,
            project.LastOpenedAtUtc,
            project.LastScannedAtUtc,
            latestScan?.FilesDiscovered is null ? 0 : latestScan.FilesDiscovered,
            latestScan is null ? 0 : latestScan.SourceFiles.Count(file => file.Extension is ".cs" or ".csproj" or ".sln" or ".slnx"),
            latestScan?.Status,
            project.IsFavorite,
            isMissing,
            isMissing ? "The folder no longer exists. Remove it from recent projects or restore the folder." : null);
    }
}
