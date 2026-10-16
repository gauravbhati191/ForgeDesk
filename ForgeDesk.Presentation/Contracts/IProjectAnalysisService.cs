using ForgeDesk.Presentation.Models;

namespace ForgeDesk.Presentation.Contracts;

/// <summary>Starts and reads persisted analysis results for an onboarded project.</summary>
public interface IProjectAnalysisService
{
    Task<ProjectAnalysisSnapshot?> LoadAsync(
        Guid projectId,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ProjectAnalysisSnapshot?> AnalyzeAsync(
        Guid projectId,
        Guid scanId,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
