using System.Text;
using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>
/// Builds scan-scoped reports from persisted rows and atomically writes deterministic exports.
/// The service does not read global rows and never includes source contents.
/// </summary>
public sealed class AnalysisReportService : IAnalysisReportService
{
    private readonly IAnalysisReportDataSource _dataSource;
    private readonly IGitRepositoryService? _gitRepositoryService;

    public AnalysisReportService(
        IAnalysisReportDataSource dataSource,
        IGitRepositoryService? gitRepositoryService = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _gitRepositoryService = gitRepositoryService;
    }

    public async Task<AnalysisReportModel> BuildAsync(
        Guid projectId,
        Guid scanId,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project identifier is required.", nameof(projectId));
        }

        if (scanId == Guid.Empty)
        {
            throw new ArgumentException("A scan identifier is required.", nameof(scanId));
        }

        var reportOptions = options ?? new AnalysisReportOptions();
        reportOptions.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var data = await _dataSource
            .GetAsync(projectId, scanId, reportOptions.Query, cancellationToken)
            .ConfigureAwait(false);
        if (data is null || data.Project.Id != projectId || data.Scan.Id != scanId || data.Scan.ProjectId != projectId)
        {
            throw new EntityNotFoundException("Analysis run", scanId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var limitations = new SortedSet<string>(StringComparer.Ordinal);
        var sourceFileById = new Dictionary<Guid, SourceFile>(data.SourceFiles.Count);
        var sourceFiles = new List<AnalysisReportSourceFile>(data.SourceFiles.Count);
        foreach (var sourceFile in data.SourceFiles.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            sourceFileById[sourceFile.Id] = sourceFile;
            sourceFiles.Add(new AnalysisReportSourceFile(
                sourceFile.RelativePath,
                sourceFile.FileName,
                sourceFile.Extension,
                sourceFile.Language,
                sourceFile.FileSizeBytes,
                sourceFile.LastModifiedAtUtc,
                sourceFile.DiscoveryStatus.ToString(),
                sourceFile.ContentHash,
                sourceFile.LineCount));
        }

        var symbolById = new Dictionary<Guid, Symbol>(data.Symbols.Count);
        var symbols = new List<AnalysisReportSymbol>(data.Symbols.Count);
        foreach (var symbol in data.Symbols
                     .OrderBy(item => item.SymbolKey, StringComparer.Ordinal)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            symbolById[symbol.Id] = symbol;
            symbols.Add(new AnalysisReportSymbol(
                symbol.SymbolKey,
                symbol.Name,
                symbol.Kind.ToString(),
                sourceFileById.TryGetValue(symbol.SourceFileId, out var sourceFile) ? sourceFile.RelativePath : null,
                symbol.StartLine,
                symbol.EndLine));
        }

        var dependencies = new List<AnalysisReportDependency>(data.Dependencies.Count);
        foreach (var dependency in data.Dependencies
                     .OrderBy(item => item.RelationshipType, StringComparer.Ordinal)
                     .ThenBy(item => item.SourceSymbolId is { } sourceId && symbolById.TryGetValue(sourceId, out var source) ? source.SymbolKey : string.Empty, StringComparer.Ordinal)
                     .ThenBy(item => item.TargetSymbolId is { } targetId && symbolById.TryGetValue(targetId, out var target) ? target.SymbolKey : string.Empty, StringComparer.Ordinal)
                     .ThenBy(item => item.IsResolved))
        {
            cancellationToken.ThrowIfCancellationRequested();
            dependencies.Add(new AnalysisReportDependency(
                dependency.SourceSymbolId is { } sourceId && symbolById.TryGetValue(sourceId, out var sourceSymbol)
                    ? sourceSymbol.SymbolKey
                    : null,
                dependency.TargetSymbolId is { } targetId && symbolById.TryGetValue(targetId, out var targetSymbol)
                    ? targetSymbol.SymbolKey
                    : null,
                dependency.RelationshipType,
                dependency.IsResolved));
        }

        var findings = new List<AnalysisReportFinding>(data.Findings.Count);
        foreach (var finding in data.Findings
                     .OrderBy(item => item.RuleId, StringComparer.Ordinal)
                     .ThenBy(item => item.SourceFileId is { } sourceId && sourceFileById.TryGetValue(sourceId, out var source) ? source.RelativePath : string.Empty, StringComparer.Ordinal)
                     .ThenBy(item => item.StartLine)
                     .ThenBy(item => item.EndLine)
                     .ThenBy(item => item.Message, StringComparer.Ordinal)
                     .ThenBy(item => item.Severity)
                     .ThenBy(item => item.SymbolId is { } symbolId && symbolById.TryGetValue(symbolId, out var symbol) ? symbol.SymbolKey : string.Empty, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reportFinding = new AnalysisReportFinding(
                finding.RuleId,
                finding.Severity.ToString(),
                finding.Message,
                finding.SourceFileId is { } sourceFileId && sourceFileById.TryGetValue(sourceFileId, out var sourceFile)
                    ? sourceFile.RelativePath
                    : null,
                finding.StartLine,
                finding.EndLine,
                finding.SymbolId is { } symbolId && symbolById.TryGetValue(symbolId, out var symbol)
                    ? symbol.SymbolKey
                    : null,
                finding.SymbolId is { } namedSymbolId && symbolById.TryGetValue(namedSymbolId, out var namedSymbol)
                    ? namedSymbol.Name
                    : null,
                finding.CreatedAtUtc,
                string.Empty);
            findings.Add(reportFinding with { Fingerprint = AnalysisFindingFingerprint.Create(reportFinding) });
        }

        var analysisAvailable = data.Scan.AnalysisStatus != AnalysisStatus.NotStarted;
        var countsAreComplete = data.Scan.Status == ScanStatus.Completed
            && data.Scan.AnalysisStatus == AnalysisStatus.Completed
            && !data.SourceFilesTruncated
            && !data.SymbolsTruncated
            && !data.DependenciesTruncated
            && !data.FindingsTruncated;
        var groupingsAreComplete = analysisAvailable
            && !data.FindingsTruncated
            && !data.SourceFilesTruncated
            && !data.SymbolsTruncated;

        AddLifecycleLimitations(data, limitations);
        AddTruncationLimitations(data, reportOptions.Query, limitations);
        AddCountLimitations(data, limitations);
        if (!reportOptions.IncludeAbsolutePaths)
        {
            limitations.Add("Absolute project and repository root paths are redacted by default.");
        }

        var summary = new AnalysisReportSummary(
            data.SourceFileCount,
            analysisAvailable ? data.Scan.AnalysisFilesProcessed : null,
            analysisAvailable ? data.SymbolCount : null,
            analysisAvailable ? data.DependencyCount : null,
            analysisAvailable ? data.FindingCount : null,
            countsAreComplete,
            groupingsAreComplete,
            BuildFindingGroups(findings, finding => finding.Severity),
            BuildFindingGroups(findings, finding => finding.RuleId),
            BuildFindingGroups(findings, finding => finding.RelativePath ?? "[not available]"),
            analysisAvailable && !data.DependenciesTruncated
                ? new AnalysisReportDependencyStatistics(
                    data.DependencyCount,
                    data.Dependencies.Count(item => item.IsResolved),
                    data.Dependencies.Count(item => !item.IsResolved),
                    BuildDependencyGroups(dependencies))
                : null);

        var report = new AnalysisReportModel(
            AnalysisReportSchema.Version,
            AnalysisReportSchema.ApplicationVersion,
            AnalysisReportSchema.FingerprintVersion,
            new AnalysisReportProject(
                data.Project.Id,
                data.Project.Name,
                data.Project.Description,
                data.Project.ProjectType.ToString(),
                reportOptions.IncludeAbsolutePaths ? data.Project.RootPath : null,
                !reportOptions.IncludeAbsolutePaths,
                SafeDirectoryName(data.Project.RootPath),
                data.Project.CreatedAtUtc,
                data.Project.UpdatedAtUtc,
                data.Project.Status.ToString()),
            new AnalysisReportScan(
                data.Scan.Id,
                data.Scan.ProjectId,
                data.Scan.ToolVersion,
                data.Scan.QueuedAtUtc,
                data.Scan.Status.ToString(),
                data.Scan.StartedAtUtc,
                data.Scan.CompletedAtUtc,
                data.Scan.FilesDiscovered,
                data.Scan.FilesProcessed,
                data.Scan.ErrorMessage,
                data.Scan.AnalysisStatus.ToString(),
                data.Scan.AnalysisStartedAtUtc,
                data.Scan.AnalysisCompletedAtUtc,
                analysisAvailable ? data.Scan.AnalysisFilesProcessed : null,
                data.Scan.AnalysisErrorMessage),
            summary,
            sourceFiles,
            symbols,
            dependencies,
            findings,
            limitations.ToArray(),
            GitContext: null);

        if (reportOptions.IncludeGitContext && _gitRepositoryService is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var git = await _gitRepositoryService
                    .InspectAsync(
                        data.Project.RootPath,
                        new GitInspectionOptions
                        {
                            IncludeHistory = false,
                            MaxChangedFiles = 2_000,
                            MaxRecentCommits = 0
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                report = report with
                {
                    GitContext = ToGitContext(git, reportOptions.IncludeAbsolutePaths),
                    Limitations = AddGitLimitations(report.Limitations, git)
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                limitations.Add("The optional Git context could not be read: " + exception.Message);
                report = report with { Limitations = limitations.ToArray() };
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return report;
    }

    public async Task<AnalysisReportExportResult> ExportAsync(
        Guid projectId,
        Guid scanId,
        AnalysisReportFormat format,
        string outputPath,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reportOptions = options ?? new AnalysisReportOptions();
        reportOptions.Validate();
        var destination = AnalysisReportFileNames.ValidateOutputPath(outputPath, format);
        if (!reportOptions.AllowOverwrite && File.Exists(destination))
        {
            throw new AnalysisReportExportException("The selected report destination already exists. Choose another file or explicitly enable overwrite.");
        }

        var report = await BuildAsync(projectId, scanId, reportOptions, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var content = format switch
        {
            AnalysisReportFormat.Json => AnalysisReportSerializer.ToJson(report),
            AnalysisReportFormat.Html => AnalysisReportSerializer.ToHtml(report),
            AnalysisReportFormat.Csv => AnalysisReportSerializer.ToCsv(report),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The report format is not supported.")
        };
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var bytes = encoding.GetBytes(content);
        if (bytes.LongLength > reportOptions.MaxOutputBytes)
        {
            throw new AnalysisReportExportException($"The generated report is {bytes.LongLength} bytes, exceeding the configured {reportOptions.MaxOutputBytes} byte limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destination, overwrite: reportOptions.AllowOverwrite);
            temporaryPath = string.Empty;
            return new AnalysisReportExportResult(destination, format, bytes.LongLength, report);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AnalysisReportExportException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new AnalysisReportExportException("The report could not be written to the selected destination.", exception);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Preserve the original export/cancellation result.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the original export/cancellation result.
                }
            }
        }
    }

    private static IReadOnlyDictionary<string, int> BuildFindingGroups(
        IEnumerable<AnalysisReportFinding> findings,
        Func<AnalysisReportFinding, string> keySelector)
    {
        var groups = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in findings.GroupBy(keySelector, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            groups[group.Key] = group.Count();
        }

        return groups;
    }

    private static IReadOnlyDictionary<string, int> BuildDependencyGroups(IEnumerable<AnalysisReportDependency> dependencies)
    {
        var groups = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in dependencies.GroupBy(item => item.RelationshipType, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            groups[group.Key] = group.Count();
        }

        return groups;
    }

    private static void AddLifecycleLimitations(AnalysisReportData data, ISet<string> limitations)
    {
        if (data.Scan.Status != ScanStatus.Completed)
        {
            limitations.Add($"The discovery scan is {data.Scan.Status}; source-file metadata may be incomplete.");
        }

        if (data.Scan.AnalysisStatus != AnalysisStatus.Completed)
        {
            limitations.Add($"The selected analysis run is {data.Scan.AnalysisStatus}; its rows are not a completed analysis snapshot.");
        }
    }

    private static void AddTruncationLimitations(
        AnalysisReportData data,
        AnalysisReportQueryOptions options,
        ISet<string> limitations)
    {
        if (data.SourceFilesTruncated)
        {
            limitations.Add($"Source-file rows were bounded at {options.MaxSourceFiles}; the source-file list is incomplete.");
        }

        if (data.SymbolsTruncated)
        {
            limitations.Add($"Symbol rows were bounded at {options.MaxSymbols}; the symbol list is incomplete.");
        }

        if (data.DependenciesTruncated)
        {
            limitations.Add($"Dependency rows were bounded at {options.MaxDependencies}; the dependency list is incomplete.");
        }

        if (data.FindingsTruncated)
        {
            limitations.Add($"Finding rows were bounded at {options.MaxFindings}; the finding list and groupings are incomplete.");
        }
    }

    private static void AddCountLimitations(AnalysisReportData data, ISet<string> limitations)
    {
        if (data.Scan.AnalysisStatus == AnalysisStatus.Completed
            && (data.Scan.AnalysisSymbolCount != data.SymbolCount
                || data.Scan.AnalysisDependencyCount != data.DependencyCount
                || data.Scan.FindingCount != data.FindingCount))
        {
            limitations.Add("Persisted analysis row counts differ from lifecycle counters; report totals use the selected rows.");
        }

        if (data.Scan.Status == ScanStatus.Completed && data.Scan.FilesDiscovered != data.SourceFileCount)
        {
            limitations.Add("Persisted source-file rows differ from the discovery counter; the report uses persisted source-file rows.");
        }
    }

    private static IReadOnlyList<string> AddGitLimitations(
        IReadOnlyList<string> existing,
        GitRepositorySnapshot git)
    {
        var limitations = new SortedSet<string>(existing, StringComparer.Ordinal);
        if (git.State == GitRepositoryState.NotRepository)
        {
            limitations.Add("No local Git repository was detected for the selected project root.");
        }
        else if (git.State == GitRepositoryState.Unreadable)
        {
            limitations.Add("Git metadata was found but could not be read safely.");
        }
        else
        {
            limitations.Add("Git context is read at report time; the persisted analysis run does not store an associated commit snapshot.");
        }

        if (git.ChangesTruncated)
        {
            limitations.Add("The Git working-tree change list was bounded and may be incomplete.");
        }

        return limitations.ToArray();
    }

    private static AnalysisReportGitContext ToGitContext(GitRepositorySnapshot git, bool includeAbsolutePaths)
    {
        var root = git.RepositoryRootPath;
        var changedFileCount = git.Changes.Count;
        return new AnalysisReportGitContext(
            git.State,
            includeAbsolutePaths ? root : null,
            !includeAbsolutePaths && root is not null,
            SafeDirectoryName(root),
            git.Branch?.Name,
            git.Branch?.IsDetached ?? false,
            git.HeadCommit?.Id,
            git.HeadCommit?.Message,
            git.HeadCommit is null
                ? null
                : string.IsNullOrWhiteSpace(git.HeadCommit.AuthorEmail)
                    ? git.HeadCommit.AuthorName
                    : $"{git.HeadCommit.AuthorName} <{git.HeadCommit.AuthorEmail}>",
            git.HeadCommit?.AuthorDateUtc,
            git.IsDirty,
            changedFileCount,
            git.StagedChanges.Count,
            git.UnstagedChanges.Count,
            git.UntrackedChanges.Count,
            git.ErrorMessage,
            IsAssociatedWithAnalysisRun: false,
            AssociationNote: "Captured at report generation; no Git metadata is persisted on this analysis run.");
    }

    private static string? SafeDirectoryName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return new DirectoryInfo(path).Name;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
