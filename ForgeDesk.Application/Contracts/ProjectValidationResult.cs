using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Validation outcome and supported-content classification.</summary>
public sealed record ProjectValidationResult(
    bool IsValid,
    string? NormalizedRootPath,
    ProjectType? ProjectType,
    int SupportedFileCount,
    int SupportedProjectFileCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static ProjectValidationResult Invalid(params string[] errors)
        => new(false, null, null, 0, 0, errors, Array.Empty<string>());

    public static ProjectValidationResult Valid(
        string normalizedRootPath,
        ProjectType projectType,
        int supportedFileCount,
        int supportedProjectFileCount,
        IReadOnlyList<string>? warnings = null)
        => new(true, normalizedRootPath, projectType, supportedFileCount, supportedProjectFileCount, Array.Empty<string>(), warnings ?? Array.Empty<string>());
}
