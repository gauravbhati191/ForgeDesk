using ForgeDesk.Domain;

namespace ForgeDesk.Web.Services;

public static class WebQueryHelpers
{
    public static object ProjectView(Project project, Scan? latestScan = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new
        {
            id = project.Id,
            name = project.Name,
            rootPath = project.RootPath,
            projectType = project.ProjectType.ToString(),
            status = project.Status.ToString(),
            description = project.Description,
            isFavorite = project.IsFavorite,
            lastOpenedAtUtc = project.LastOpenedAtUtc,
            lastScannedAtUtc = project.LastScannedAtUtc,
            updatedAtUtc = project.UpdatedAtUtc,
            latestScan = latestScan is null ? null : ScanView(latestScan)
        };
    }

    public static object ScanView(Scan scan, AnalysisJobSnapshot? job = null)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var persistedStatus = scan.AnalysisStatus.ToString();
        var persistedStatusIsTerminal = IsTerminalAnalysisStatus(scan.AnalysisStatus);
        var jobIsAuthoritative = job is not null && !persistedStatusIsTerminal;
        var analysisStatus = persistedStatusIsTerminal
            ? persistedStatus
            : job?.State ?? persistedStatus;
        var analysisPhase = jobIsAuthoritative
            ? job!.Phase
            : analysisStatus;

        return new
        {
            id = scan.Id,
            projectId = scan.ProjectId,
            queuedAtUtc = scan.QueuedAtUtc,
            startedAtUtc = scan.StartedAtUtc,
            completedAtUtc = scan.CompletedAtUtc,
            status = scan.Status.ToString(),
            filesDiscovered = scan.FilesDiscovered,
            filesProcessed = scan.FilesProcessed,
            errorMessage = scan.ErrorMessage,
            toolVersion = scan.ToolVersion,
            analysis = new
            {
                status = analysisStatus,
                phase = analysisPhase,
                startedAtUtc = scan.AnalysisStartedAtUtc,
                completedAtUtc = scan.AnalysisCompletedAtUtc,
                filesProcessed = jobIsAuthoritative ? job!.FilesProcessed : scan.AnalysisFilesProcessed,
                filesTotal = jobIsAuthoritative ? job!.FilesTotal : scan.FilesDiscovered,
                symbolCount = jobIsAuthoritative ? job!.SymbolsFound : scan.AnalysisSymbolCount,
                dependencyCount = jobIsAuthoritative ? job!.DependenciesFound : scan.AnalysisDependencyCount,
                findingCount = jobIsAuthoritative ? job!.FindingsFound : scan.FindingCount,
                currentPath = jobIsAuthoritative ? job!.CurrentPath : null,
                bindingMode = job?.BindingMode?.ToString(),
                bindingMessage = job?.BindingMessage,
                errorMessage = scan.AnalysisErrorMessage ?? job?.ErrorMessage,
                updatedAtUtc = job?.UpdatedAtUtc
            }
        };
    }

    private static bool IsTerminalAnalysisStatus(AnalysisStatus status)
        => status is AnalysisStatus.Completed or AnalysisStatus.Failed or AnalysisStatus.Cancelled;
}
