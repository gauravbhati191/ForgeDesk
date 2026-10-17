using ForgeDesk.Domain;
using ForgeDesk.Infrastructure;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ApplicationAnalysisProgress = ForgeDesk.Application.AnalysisProgress;
using ApplicationAnalysisService = ForgeDesk.Application.IAnalysisService;

namespace ForgeDesk.App.Services;

/// <summary>
/// Starts the application analysis service and reads its persisted result without
/// keeping either the application scope or EF context alive in WPF viewmodels.
/// </summary>
public sealed class PersistedProjectAnalysisService : IProjectAnalysisService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PersistedProjectAnalysisService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task<ProjectAnalysisSnapshot?> LoadAsync(
        Guid projectId,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ForgeDeskDbContext>();

        progress?.Report(new AnalysisProgress("Loading the selected project...", 0, 4));
        var project = await dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        progress?.Report(new AnalysisProgress("Loading the latest persisted scan...", 1, 4));
        var scan = await dbContext.Scans
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var latestScan = scan.OrderByDescending(item => item.QueuedAtUtc).FirstOrDefault();
        if (latestScan is null)
        {
            progress?.Report(new AnalysisProgress("No scan is available for this project.", 4, 4));
            return EmptySnapshot(project);
        }

        return await LoadSnapshotAsync(
                dbContext,
                project,
                latestScan.Id,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProjectAnalysisSnapshot?> AnalyzeAsync(
        Guid projectId,
        Guid scanId,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || scanId == Guid.Empty)
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ForgeDeskDbContext>();
        var scan = await dbContext.Scans
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == scanId && item.ProjectId == projectId,
                cancellationToken)
            .ConfigureAwait(false);
        if (scan is null)
        {
            return null;
        }

        var analysisService = scope.ServiceProvider.GetRequiredService<ApplicationAnalysisService>();
        var applicationProgress = progress is null
            ? null
            : new Progress<ApplicationAnalysisProgress>(update => progress.Report(MapProgress(update)));

        try
        {
            await analysisService
                .AnalyzeAsync(
                    scanId,
                    qualityOptions: null,
                    progress: applicationProgress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // AnalysisService persists Cancelled before rethrowing. Read that
            // terminal state with a non-cancelled token so the UI can show it.
            var cancelledSnapshot = await TryLoadTerminalSnapshotAsync(dbContext, projectId, scanId).ConfigureAwait(false);
            if (cancelledSnapshot?.AnalysisStageStatus == AnalysisStatus.Cancelled)
            {
                return cancelledSnapshot;
            }

            throw;
        }
        catch
        {
            // AnalysisService persists Failed before rethrowing. Preserve the
            // persisted failure in the UI when the status write succeeded.
            var failedSnapshot = await TryLoadTerminalSnapshotAsync(dbContext, projectId, scanId).ConfigureAwait(false);
            if (failedSnapshot?.AnalysisStageStatus is AnalysisStatus.Failed or AnalysisStatus.Cancelled or AnalysisStatus.Running)
            {
                return failedSnapshot;
            }

            throw;
        }

        progress?.Report(new AnalysisProgress("Loading persisted analysis results...", 1, 1));
        return await LoadSnapshotByScanIdAsync(
                dbContext,
                projectId,
                scanId,
                progress: null,
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task<ProjectAnalysisSnapshot?> TryLoadTerminalSnapshotAsync(
        ForgeDeskDbContext dbContext,
        Guid projectId,
        Guid scanId)
    {
        try
        {
            return await LoadSnapshotByScanIdAsync(
                    dbContext,
                    projectId,
                    scanId,
                    progress: null,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ProjectAnalysisSnapshot?> LoadSnapshotByScanIdAsync(
        ForgeDeskDbContext dbContext,
        Guid projectId,
        Guid scanId,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        var scan = await dbContext.Scans
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == scanId && item.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (scan is null)
        {
            return null;
        }

        return await LoadSnapshotAsync(dbContext, project, scan, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProjectAnalysisSnapshot> LoadSnapshotAsync(
        ForgeDeskDbContext dbContext,
        Project project,
        Guid scanId,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var scan = await dbContext.Scans
            .AsNoTracking()
            .SingleAsync(item => item.Id == scanId && item.ProjectId == project.Id, cancellationToken)
            .ConfigureAwait(false);
        return await LoadSnapshotAsync(dbContext, project, scan, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProjectAnalysisSnapshot> LoadSnapshotAsync(
        ForgeDeskDbContext dbContext,
        Project project,
        Scan scan,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new AnalysisProgress("Counting persisted symbols...", 1, 4));
        var sourceFiles = await dbContext.SourceFiles
            .AsNoTracking()
            .Where(sourceFile => sourceFile.ScanId == scan.Id)
            .OrderBy(sourceFile => sourceFile.RelativePath)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var symbols = await dbContext.Symbols
            .AsNoTracking()
            .Include(symbol => symbol.SourceFile)
            .Where(symbol => symbol.ScanId == scan.Id)
            .OrderBy(symbol => symbol.SymbolKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new AnalysisProgress("Counting persisted dependencies...", 2, 4));
        var dependencies = await dbContext.Dependencies
            .AsNoTracking()
            .Include(dependency => dependency.SourceSymbol)
            .Include(dependency => dependency.TargetSymbol)
            .Where(dependency => dependency.ScanId == scan.Id)
            .OrderBy(dependency => dependency.RelationshipType)
            .ThenBy(dependency => dependency.SourceSymbolId)
            .ThenBy(dependency => dependency.TargetSymbolId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new AnalysisProgress("Loading persisted findings...", 3, 4));
        var findings = await dbContext.Findings
            .AsNoTracking()
            .Include(finding => finding.SourceFile)
            .Include(finding => finding.Symbol)
            .Where(finding => finding.ScanId == scan.Id)
            .OrderBy(finding => finding.Severity)
            .ThenBy(finding => finding.RuleId)
            .ThenBy(finding => finding.StartLine)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sourceFileItems = sourceFiles
            .Select(sourceFile => new AnalysisSourceFileItem(
                sourceFile.Id,
                sourceFile.ScanId,
                sourceFile.RelativePath,
                sourceFile.FileName,
                sourceFile.Extension,
                sourceFile.Language,
                sourceFile.FileSizeBytes,
                sourceFile.LastModifiedAtUtc))
            .ToArray();

        var sourceFilesById = sourceFiles.ToDictionary(sourceFile => sourceFile.Id);

        var symbolItems = symbols
            .Select(symbol => new AnalysisSymbolItem(
                symbol.Id,
                symbol.ScanId,
                symbol.SourceFileId,
                symbol.SymbolKey,
                symbol.Name,
                symbol.Kind,
                symbol.SourceFile?.RelativePath ?? sourceFilesById.GetValueOrDefault(symbol.SourceFileId)?.RelativePath,
                symbol.StartLine,
                symbol.EndLine))
            .ToArray();

        var dependencyItems = dependencies
            .Select(dependency => new AnalysisDependencyItem(
                dependency.Id,
                dependency.ScanId,
                dependency.SourceSymbolId,
                dependency.TargetSymbolId,
                dependency.RelationshipType,
                dependency.IsResolved,
                dependency.SourceSymbol?.Name,
                dependency.TargetSymbol?.Name))
            .ToArray();

        var findingItems = findings
            .Select(finding => new AnalysisFindingItem(
                finding.Id,
                finding.RuleId,
                finding.Severity,
                finding.Message,
                finding.SourceFile?.RelativePath,
                finding.StartLine,
                finding.EndLine,
                finding.SourceFileId,
                finding.SymbolId,
                finding.Symbol?.Name,
                finding.Symbol?.SymbolKey,
                finding.ScanId))
            .ToArray();

        progress?.Report(new AnalysisProgress("Persisted analysis loaded.", 4, 4, symbolItems.Length, dependencyItems.Length, findingItems.Length));
        return new ProjectAnalysisSnapshot(
            project.Id,
            project.Name,
            project.RootPath,
            scan.Id,
            scan.Status,
            scan.FilesDiscovered,
            scan.FilesProcessed,
            symbolItems.Length,
            dependencyItems.Length,
            findingItems.Length,
            findingItems,
            scan.ErrorMessage,
            scan.AnalysisStatus,
            scan.AnalysisErrorMessage,
            symbolItems,
            dependencyItems,
            sourceFileItems);
    }

    private static ProjectAnalysisSnapshot EmptySnapshot(Project project)
        => new(
            project.Id,
            project.Name,
            project.RootPath,
            null,
            null,
            0,
            0,
            0,
            0,
            0,
            Array.Empty<AnalysisFindingItem>(),
            AnalysisStageStatus: null,
            AnalysisErrorMessage: null);

    private static AnalysisProgress MapProgress(ApplicationAnalysisProgress update)
        => new(
            update.Phase,
            update.FilesProcessed,
            update.FilesTotal,
            update.SymbolsFound,
            update.DependenciesFound,
            update.FindingsFound,
            update.CurrentPath);
}
