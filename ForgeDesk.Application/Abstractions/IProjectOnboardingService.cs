namespace ForgeDesk.Application;

/// <summary>Validates, discovers, registers, and persists a local project.</summary>
public interface IProjectOnboardingService
{
    Task<ProjectOnboardingResult> OnboardAsync(
        ProjectOnboardingRequest request,
        IProgress<FileDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
