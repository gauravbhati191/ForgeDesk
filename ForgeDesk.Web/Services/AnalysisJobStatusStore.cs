using System.Collections.Concurrent;
using ForgeDesk.Application;

namespace ForgeDesk.Web.Services;

public sealed record AnalysisJobSnapshot(
    Guid ProjectId,
    Guid ScanId,
    string State,
    string Phase,
    int FilesProcessed,
    int FilesTotal,
    int SymbolsFound,
    int DependenciesFound,
    int FindingsFound,
    string? CurrentPath,
    AnalysisBindingMode? BindingMode,
    string? BindingMessage,
    string? ErrorMessage,
    DateTimeOffset UpdatedAtUtc);

public sealed class AnalysisJobStatusStore
{
    private readonly ConcurrentDictionary<Guid, AnalysisJobSnapshot> _snapshots = new();

    public AnalysisJobSnapshot Queue(AnalysisJob job)
        => _snapshots.AddOrUpdate(
            job.ScanId,
            _ => NewQueued(job),
            (_, existing) => existing with
            {
                ProjectId = job.ProjectId,
                State = "Queued",
                Phase = "Queued",
                ErrorMessage = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

    public AnalysisJobSnapshot Start(AnalysisJob job)
        => _snapshots.AddOrUpdate(
            job.ScanId,
            _ => NewQueued(job) with
            {
                State = "Running",
                Phase = "Starting",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            },
            (_, existing) => existing with
            {
                State = "Running",
                Phase = "Starting",
                ErrorMessage = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

    public AnalysisJobSnapshot Report(AnalysisJob job, AnalysisProgress progress)
        => _snapshots.AddOrUpdate(
            job.ScanId,
            _ => new AnalysisJobSnapshot(
                job.ProjectId,
                job.ScanId,
                "Running",
                progress.Phase,
                progress.FilesProcessed,
                progress.FilesTotal,
                progress.SymbolsFound,
                progress.DependenciesFound,
                progress.FindingsFound,
                progress.CurrentPath,
                progress.BindingMode,
                progress.BindingMessage,
                null,
                DateTimeOffset.UtcNow),
            (_, existing) => existing with
            {
                State = "Running",
                Phase = progress.Phase,
                FilesProcessed = progress.FilesProcessed,
                FilesTotal = progress.FilesTotal,
                SymbolsFound = progress.SymbolsFound,
                DependenciesFound = progress.DependenciesFound,
                FindingsFound = progress.FindingsFound,
                CurrentPath = progress.CurrentPath,
                BindingMode = progress.BindingMode ?? existing.BindingMode,
                BindingMessage = progress.BindingMessage ?? existing.BindingMessage,
                ErrorMessage = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

    public AnalysisJobSnapshot Complete(AnalysisJob job, AnalysisRunDto result)
        => _snapshots.AddOrUpdate(
            job.ScanId,
            _ => new AnalysisJobSnapshot(
                job.ProjectId,
                job.ScanId,
                result.Status.ToString(),
                "Completed",
                result.FilesProcessed,
                result.FilesProcessed,
                result.SymbolCount,
                result.DependencyCount,
                result.FindingCount,
                null,
                result.BindingMode,
                result.BindingMessage,
                result.ErrorMessage,
                DateTimeOffset.UtcNow),
            (_, existing) => existing with
            {
                State = result.Status.ToString(),
                Phase = "Completed",
                FilesProcessed = result.FilesProcessed,
                FilesTotal = Math.Max(existing.FilesTotal, result.FilesProcessed),
                SymbolsFound = result.SymbolCount,
                DependenciesFound = result.DependencyCount,
                FindingsFound = result.FindingCount,
                CurrentPath = null,
                BindingMode = result.BindingMode ?? existing.BindingMode,
                BindingMessage = result.BindingMessage ?? existing.BindingMessage,
                ErrorMessage = result.ErrorMessage,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

    public AnalysisJobSnapshot Fail(AnalysisJob job, string message, string state = "Failed")
        => _snapshots.AddOrUpdate(
            job.ScanId,
            _ => new AnalysisJobSnapshot(
                job.ProjectId,
                job.ScanId,
                state,
                state,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                null,
                message,
                DateTimeOffset.UtcNow),
            (_, existing) => existing with
            {
                State = state,
                Phase = state,
                ErrorMessage = message,
                CurrentPath = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

    public bool TryGet(Guid scanId, out AnalysisJobSnapshot? snapshot)
        => _snapshots.TryGetValue(scanId, out snapshot);

    private static AnalysisJobSnapshot NewQueued(AnalysisJob job)
        => new(
            job.ProjectId,
            job.ScanId,
            "Queued",
            "Queued",
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);
}
