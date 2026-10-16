using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Complete Phase 1 project onboarding workflow: validate, discover, and persist metadata.</summary>
public sealed class ProjectOnboardingService : IProjectOnboardingService
{
    public const string DefaultToolVersion = "ForgeDesk-Phase1";

    private readonly IProjectRepository _projects;
    private readonly IProjectValidationService _validator;
    private readonly IFileDiscoveryService _discovery;
    private readonly IProjectOnboardingStore _store;
    private readonly IClock _clock;
    private readonly string _toolVersion;

    public ProjectOnboardingService(
        IProjectRepository projects,
        IProjectValidationService validator,
        IFileDiscoveryService discovery,
        IProjectOnboardingStore store,
        IClock? clock = null,
        string? toolVersion = null)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? new SystemClock();
        _toolVersion = string.IsNullOrWhiteSpace(toolVersion) ? DefaultToolVersion : toolVersion.Trim();
    }

    public async Task<ProjectOnboardingResult> OnboardAsync(
        ProjectOnboardingRequest request,
        IProgress<FileDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = await _validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new ProjectValidationException(validation.Errors);
        }

        var now = _clock.UtcNow;
        var existing = await _projects.GetByNormalizedPathAsync(validation.NormalizedRootPath!, cancellationToken).ConfigureAwait(false);
        var project = existing ?? new Project(
            Guid.NewGuid(),
            request.Name,
            Path.GetFullPath(request.RootPath.Trim()),
            validation.ProjectType!.Value,
            now,
            request.Description);

        project.MarkOpened(now);
        var scan = Scan.CreatePending(project.Id, _toolVersion, now);
        await _store.BeginAsync(project, scan, cancellationToken).ConfigureAwait(false);

        try
        {
            scan.Start(now);
            await _store.SaveRunningAsync(scan, cancellationToken).ConfigureAwait(false);

            var discovery = await _discovery.DiscoverAsync(
                project.RootPath,
                FileDiscoveryOptions.SourceFilesAndProjectFiles,
                progress,
                cancellationToken).ConfigureAwait(false);

            scan.RecordDiscovered(discovery.Files.Count);
            var sourceFiles = new List<SourceFile>(discovery.Files.Count);
            var processed = 0;
            foreach (var discoveredFile in discovery.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceFiles.Add(SourceFile.Create(
                    scan.Id,
                    discoveredFile.RelativePath,
                    discoveredFile.FileName,
                    discoveredFile.Extension,
                    discoveredFile.FileSizeBytes,
                    discoveredFile.LastModifiedAtUtc,
                    discoveredFile.Language,
                    DiscoveryStatus.Discovered,
                    discoveredFile.ContentHash,
                    discoveredFile.NormalizedPath));
                processed++;
                scan.RecordProcessed(processed);
            }

            scan.Complete(discovery.Files.Count, processed, _clock.UtcNow);
            project.MarkScanned(_clock.UtcNow);
            await _store.CompleteAsync(project, scan, sourceFiles, cancellationToken).ConfigureAwait(false);

            return new ProjectOnboardingResult(
                ProjectDto.From(project),
                ScanDto.From(scan),
                sourceFiles.Count(file => file.Extension == ".cs"),
                sourceFiles.Count(file => file.Extension is ".sln" or ".slnx" or ".csproj"),
                BuildWarnings(discovery),
                existing is not null);
        }
        catch (OperationCanceledException)
        {
            if (scan.Status is ScanStatus.Pending or ScanStatus.Running)
            {
                scan.Cancel(_clock.UtcNow);
                await _store.SaveCancelledAsync(scan, CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        catch (Exception exception)
        {
            if (scan.Status is ScanStatus.Pending or ScanStatus.Running)
            {
                scan.Fail(exception.Message, _clock.UtcNow);
                await _store.SaveFailedAsync(scan, CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
    }

    private static IReadOnlyList<string> BuildWarnings(FileDiscoveryResult discovery)
    {
        var warnings = new List<string>();
        if (discovery.SkippedPaths.Count > 0)
        {
            warnings.Add($"{discovery.SkippedPaths.Count} path(s) were skipped because they were inaccessible, generated, or outside the selected root.");
        }
        if (discovery.DuplicatePaths.Count > 0)
        {
            warnings.Add($"{discovery.DuplicatePaths.Count} duplicate or linked path(s) were skipped.");
        }
        return warnings;
    }
}
