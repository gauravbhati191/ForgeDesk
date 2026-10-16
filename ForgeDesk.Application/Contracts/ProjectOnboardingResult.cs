using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Persisted outcome of a project metadata discovery operation.</summary>
public sealed record ProjectOnboardingResult(
    ProjectDto Project,
    ScanDto Scan,
    int SourceFileCount,
    int SupportedProjectFileCount,
    IReadOnlyList<string> Warnings,
    bool ReusedExistingProject);
