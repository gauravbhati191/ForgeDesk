using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Orchestrates safe source loading, Roslyn analysis, lifecycle state, and idempotent persistence.</summary>
public sealed class AnalysisService : IAnalysisService
{
    private readonly IAnalysisRepository _repository;
    private readonly IAnalysisEngine _engine;
    private readonly IClock _clock;

    public AnalysisService(IAnalysisRepository repository, IAnalysisEngine engine, IClock? clock = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _clock = clock ?? new SystemClock();
    }

    public async Task<AnalysisRunDto> AnalyzeAsync(
        Guid scanId,
        AnalysisQualityOptions? qualityOptions = null,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = await _repository.GetContextAsync(scanId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException("Scan", scanId);

        var scan = context.Scan;
        if (scan.Status != ScanStatus.Completed)
        {
            throw new ProjectValidationException(["Only a completed discovery scan can be analyzed."]);
        }

        scan.StartAnalysis(_clock.UtcNow);
        // Persist Running even when cancellation was requested immediately after queueing;
        // the terminal cancellation state is written by the catch block below.
        await _repository.SaveStatusAsync(scan, CancellationToken.None).ConfigureAwait(false);

        var resultsReplaced = false;
        try
        {
            // Clear the previous result set before reading or binding anything. The scan remains
            // Running until a full replacement is persisted and then becomes Completed, so a
            // failed or cancelled retry cannot leave rows that look like the previous success.
            await _repository.ClearResultsAsync(scan.Id, CancellationToken.None).ConfigureAwait(false);

            var sourceFiles = context.SourceFiles
                .Where(file => file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    && file.Language.Equals("C#", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();

            var inputs = new List<AnalysisFileInput>(sourceFiles.Length);
            for (var index = 0; index < sourceFiles.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceFile = sourceFiles[index];
                var path = ResolveSourcePath(context.Project.RootPath, sourceFile.RelativePath);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"The discovered source file no longer exists: {sourceFile.RelativePath}", path);
                }

                VerifyPhysicalBoundary(context.Project.RootPath, path, sourceFile.RelativePath);

                var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                inputs.Add(new AnalysisFileInput(sourceFile.Id, sourceFile.RelativePath, path, content));
                scan.RecordAnalysisProgress(index + 1);
                progress?.Report(new AnalysisProgress(
                    "Reading",
                    index + 1,
                    sourceFiles.Length,
                    0,
                    0,
                    0,
                    sourceFile.RelativePath));
            }

            var request = new AnalysisRequest(
                scan.Id,
                context.Project.Id,
                context.Project.RootPath,
                inputs,
                qualityOptions ?? new AnalysisQualityOptions());
            var result = await _engine.AnalyzeAsync(request, progress, cancellationToken).ConfigureAwait(false);

            var symbols = result.Symbols
                .OrderBy(symbol => symbol.SymbolKey, StringComparer.Ordinal)
                .GroupBy(symbol => symbol.SymbolKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .Select(symbol => Symbol.Create(
                    scan.Id,
                    symbol.SourceFileId,
                    symbol.SymbolKey,
                    symbol.Name,
                    symbol.Kind,
                    symbol.StartLine,
                    symbol.EndLine))
                .ToArray();
            var symbolsByKey = symbols.ToDictionary(symbol => symbol.SymbolKey, StringComparer.Ordinal);

            var dependencies = result.Dependencies
                .OrderBy(dependency => dependency.SourceSymbolKey, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.TargetSymbolKey, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.RelationshipType, StringComparer.Ordinal)
                .GroupBy(
                    dependency => (dependency.SourceSymbolKey, dependency.TargetSymbolKey, dependency.RelationshipType))
                .Select(group => group.First())
                .Where(dependency => symbolsByKey.ContainsKey(dependency.SourceSymbolKey))
                .Select(dependency => Dependency.Create(
                    scan.Id,
                    symbolsByKey[dependency.SourceSymbolKey].Id,
                    dependency.TargetSymbolKey is not null && symbolsByKey.TryGetValue(dependency.TargetSymbolKey, out var target)
                        ? target.Id
                        : null,
                    dependency.RelationshipType,
                    dependency.IsResolved && dependency.TargetSymbolKey is not null && symbolsByKey.ContainsKey(dependency.TargetSymbolKey)))
                .ToArray();

            var findings = result.Findings
                .OrderBy(finding => finding.SourceFileId)
                .ThenBy(finding => finding.StartLine)
                .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
                .ThenBy(finding => finding.Message, StringComparer.Ordinal)
                .Select(finding => Finding.Create(
                    scan.Id,
                    finding.RuleId,
                    finding.Severity,
                    finding.Message,
                    finding.SourceFileId,
                    finding.SymbolKey is not null && symbolsByKey.TryGetValue(finding.SymbolKey, out var symbol)
                        ? symbol.Id
                        : null,
                    finding.StartLine,
                    finding.EndLine,
                    _clock.UtcNow))
                .ToArray();

            cancellationToken.ThrowIfCancellationRequested();
            await _repository.ReplaceResultsAsync(scan.Id, symbols, dependencies, findings, cancellationToken).ConfigureAwait(false);
            resultsReplaced = true;
            cancellationToken.ThrowIfCancellationRequested();
            scan.CompleteAnalysis(sourceFiles.Length, symbols.Length, dependencies.Length, findings.Length, _clock.UtcNow);
            await _repository.SaveStatusAsync(scan, CancellationToken.None).ConfigureAwait(false);
            progress?.Report(new AnalysisProgress(
                "Completed",
                sourceFiles.Length,
                sourceFiles.Length,
                symbols.Length,
                dependencies.Length,
                findings.Length));
            return AnalysisRunDto.From(scan, result.BindingMode, result.BindingMessage);
        }
        catch (OperationCanceledException)
        {
            if (resultsReplaced)
            {
                await TryClearResultsAsync(scan.Id).ConfigureAwait(false);
            }

            if (scan.AnalysisStatus == AnalysisStatus.Running)
            {
                scan.CancelAnalysis(_clock.UtcNow);
                await _repository.SaveStatusAsync(scan, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception exception)
        {
            if (resultsReplaced)
            {
                await TryClearResultsAsync(scan.Id).ConfigureAwait(false);
            }

            if (scan.AnalysisStatus == AnalysisStatus.Running)
            {
                scan.FailAnalysis(exception.Message, _clock.UtcNow);
                await _repository.SaveStatusAsync(scan, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task TryClearResultsAsync(Guid scanId)
    {
        try
        {
            await _repository.ClearResultsAsync(scanId, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original analysis failure/cancellation; the terminal status below
            // remains the authoritative indication that result rows are not a successful run.
        }
    }

    private static string ResolveSourcePath(string rootPath, string relativePath)
    {
        var normalizedRoot = PathNormalization.Normalize(rootPath);
        var candidate = PathNormalization.Normalize(Path.Combine(normalizedRoot, relativePath));
        if (!PathNormalization.IsWithin(candidate, normalizedRoot))
        {
            throw new ProjectValidationException(["A discovered source file escaped the registered project root."]);
        }

        return candidate;
    }

    private static void VerifyPhysicalBoundary(string rootPath, string candidatePath, string relativePath)
    {
        try
        {
            var root = new DirectoryInfo(PathNormalization.Normalize(rootPath));
            var physicalRoot = root.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? root.FullName;
            var file = new FileInfo(candidatePath);
            var physicalFile = file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName;
            if (!PathNormalization.IsWithin(physicalFile, physicalRoot))
            {
                throw new ProjectValidationException([$"The discovered source file escaped the registered project root: {relativePath}."]);
            }
        }
        catch (ProjectValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProjectValidationException([$"The discovered source file could not be safely resolved: {relativePath}."]);
        }
    }

}
