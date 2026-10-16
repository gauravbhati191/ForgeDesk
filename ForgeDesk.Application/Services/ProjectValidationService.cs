using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Validates a selected directory using the same safe discovery boundary as onboarding.</summary>
public sealed class ProjectValidationService : IProjectValidationService
{
    private readonly IFileDiscoveryService _discovery;

    public ProjectValidationService(IFileDiscoveryService discovery)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
    }

    public async Task<ProjectValidationResult> ValidateAsync(ProjectOnboardingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ProjectValidationResult.Invalid("A project name is required.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = PathNormalization.Normalize(request.RootPath);
        }
        catch (Exception exception) when (exception is DomainInvariantException or ArgumentException or NotSupportedException)
        {
            return ProjectValidationResult.Invalid(exception.Message);
        }

        if (!Directory.Exists(normalizedPath))
        {
            return new ProjectValidationResult(false, normalizedPath, null, 0, 0, ["The selected folder does not exist."], Array.Empty<string>());
        }

        try
        {
            if ((File.GetAttributes(normalizedPath) & FileAttributes.Directory) == 0)
            {
                return new ProjectValidationResult(false, normalizedPath, null, 0, 0, ["The selected path is not a directory."], Array.Empty<string>());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new ProjectValidationResult(false, normalizedPath, null, 0, 0, [$"The selected folder cannot be accessed: {exception.Message}"], Array.Empty<string>());
        }

        FileDiscoveryResult result;
        try
        {
            result = await _discovery.DiscoverAsync(normalizedPath, FileDiscoveryOptions.SourceFilesAndProjectFiles, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new ProjectValidationResult(false, normalizedPath, null, 0, 0, [$"The selected folder cannot be accessed: {exception.Message}"], Array.Empty<string>());
        }
        catch (DirectoryNotFoundException exception)
        {
            return new ProjectValidationResult(false, normalizedPath, null, 0, 0, [exception.Message], Array.Empty<string>());
        }

        var supportedProjectFiles = result.Files.Count(file => IsProjectFile(file.Extension));
        if (result.Files.Count == 0)
        {
            return new ProjectValidationResult(false, normalizedPath, null, 0, supportedProjectFiles, ["No supported C# project or source files were found in the selected folder."], BuildWarnings(result));
        }

        var type = result.Files.Any(file => file.Extension is ".sln" or ".slnx")
            ? ProjectType.Solution
            : result.Files.Any(file => file.Extension == ".csproj")
                ? ProjectType.CSharpProject
                : ProjectType.SourceFolder;

        return ProjectValidationResult.Valid(normalizedPath, type, result.Files.Count, supportedProjectFiles, BuildWarnings(result));
    }

    private static bool IsProjectFile(string extension) => extension is ".sln" or ".slnx" or ".csproj";

    private static IReadOnlyList<string> BuildWarnings(FileDiscoveryResult result)
    {
        var warnings = new List<string>();
        if (result.SkippedPaths.Count > 0)
        {
            warnings.Add($"{result.SkippedPaths.Count} path(s) could not be read or were safely excluded.");
        }
        if (result.DuplicatePaths.Count > 0)
        {
            warnings.Add($"{result.DuplicatePaths.Count} duplicate or linked path(s) were skipped.");
        }
        return warnings;
    }
}
