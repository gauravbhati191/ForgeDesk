namespace ForgeDesk.Application;

/// <summary>Validates a selected folder and identifies its supported C# project type.</summary>
public interface IProjectValidationService
{
    Task<ProjectValidationResult> ValidateAsync(ProjectOnboardingRequest request, CancellationToken cancellationToken = default);
}
