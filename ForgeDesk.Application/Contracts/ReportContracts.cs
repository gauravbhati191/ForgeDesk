using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Versioned wire contract identifiers used by ForgeDesk analysis reports.</summary>
public static class AnalysisReportSchema
{
    /// <summary>Schema version for the JSON, HTML, and CSV report representations.</summary>
    public const string Version = "1.0";

    /// <summary>Application/report producer version recorded in an export.</summary>
    public const string ApplicationVersion = "4.0";

    /// <summary>Version of the stable finding fingerprint algorithm.</summary>
    public const string FingerprintVersion = "1";
}

/// <summary>Supported analysis report representations.</summary>
public enum AnalysisReportFormat
{
    Json,
    Html,
    Csv
}

/// <summary>Bounded query limits used while reading one persisted analysis run.</summary>
public sealed record AnalysisReportQueryOptions
{
    public int MaxSourceFiles { get; init; } = 100_000;
    public int MaxSymbols { get; init; } = 100_000;
    public int MaxDependencies { get; init; } = 100_000;
    public int MaxFindings { get; init; } = 100_000;

    public void Validate()
    {
        ValidateLimit(MaxSourceFiles, nameof(MaxSourceFiles));
        ValidateLimit(MaxSymbols, nameof(MaxSymbols));
        ValidateLimit(MaxDependencies, nameof(MaxDependencies));
        ValidateLimit(MaxFindings, nameof(MaxFindings));
    }

    private static void ValidateLimit(int value, string parameterName)
    {
        if (value is < 0 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Report row limits must be between 0 and 100000.");
        }
    }
}

/// <summary>Privacy, boundedness, and overwrite options for report generation/export.</summary>
public sealed record AnalysisReportOptions
{
    /// <summary>Absolute project and repository roots are omitted unless this is explicitly enabled.</summary>
    public bool IncludeAbsolutePaths { get; init; }

    /// <summary>When available, include a read-only snapshot of the current local Git context.</summary>
    public bool IncludeGitContext { get; init; } = true;

    /// <summary>Limits applied to the persisted rows read for this report.</summary>
    public AnalysisReportQueryOptions Query { get; init; } = new();

    /// <summary>Maximum UTF-8 payload size written by an export.</summary>
    public long MaxOutputBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Whether an existing destination may be replaced.</summary>
    public bool AllowOverwrite { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Query);
        Query.Validate();
        if (MaxOutputBytes is <= 0 or > 256 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOutputBytes), "MaxOutputBytes must be greater than zero and no larger than 256 MiB.");
        }
    }
}

/// <summary>Materialized, scan-scoped data returned by the infrastructure report query adapter.</summary>
public sealed record AnalysisReportData(
    Project Project,
    Scan Scan,
    IReadOnlyList<SourceFile> SourceFiles,
    IReadOnlyList<Symbol> Symbols,
    IReadOnlyList<Dependency> Dependencies,
    IReadOnlyList<Finding> Findings,
    int SourceFileCount,
    int SymbolCount,
    int DependencyCount,
    int FindingCount,
    bool SourceFilesTruncated = false,
    bool SymbolsTruncated = false,
    bool DependenciesTruncated = false,
    bool FindingsTruncated = false);

/// <summary>Project metadata included in a report. Absolute paths are private by default.</summary>
public sealed record AnalysisReportProject(
    Guid ProjectId,
    string Name,
    string? Description,
    string ProjectType,
    string? RootPath,
    bool RootPathRedacted,
    string? RootDirectoryName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Status);

/// <summary>Discovery and semantic-analysis lifecycle metadata for the selected scan.</summary>
public sealed record AnalysisReportScan(
    Guid ScanId,
    Guid ProjectId,
    string ToolVersion,
    DateTimeOffset QueuedAtUtc,
    string DiscoveryStatus,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int FilesDiscovered,
    int FilesProcessed,
    string? DiscoveryError,
    string AnalysisStatus,
    DateTimeOffset? AnalysisStartedAtUtc,
    DateTimeOffset? AnalysisCompletedAtUtc,
    int? AnalysisFilesProcessed,
    string? AnalysisError);

/// <summary>One source-file metadata row in a report; source contents are never exported.</summary>
public sealed record AnalysisReportSourceFile(
    string RelativePath,
    string FileName,
    string Extension,
    string Language,
    long FileSizeBytes,
    DateTimeOffset LastModifiedAtUtc,
    string DiscoveryStatus,
    string? ContentHash,
    int? LineCount);

/// <summary>One persisted Roslyn symbol represented without database identity.</summary>
public sealed record AnalysisReportSymbol(
    string SymbolKey,
    string Name,
    string Kind,
    string? RelativePath,
    int? StartLine,
    int? EndLine);

/// <summary>One persisted supported dependency represented by stable symbol identities.</summary>
public sealed record AnalysisReportDependency(
    string? SourceSymbolKey,
    string? TargetSymbolKey,
    string RelationshipType,
    bool IsResolved);

/// <summary>One persisted finding with a stable, non-database fingerprint.</summary>
public sealed record AnalysisReportFinding(
    string RuleId,
    string Severity,
    string Message,
    string? RelativePath,
    int? StartLine,
    int? EndLine,
    string? SymbolKey,
    string? SymbolName,
    DateTimeOffset CreatedAtUtc,
    string Fingerprint);

/// <summary>Supported dependency totals and relationship groupings.</summary>
public sealed record AnalysisReportDependencyStatistics(
    int TotalCount,
    int ResolvedCount,
    int UnresolvedCount,
    IReadOnlyDictionary<string, int> ByRelationshipType);

/// <summary>Counted report metrics. Null analysis counts mean that analysis has not produced a snapshot.</summary>
public sealed record AnalysisReportSummary(
    int SourceFileCount,
    int? AnalyzedSourceFileCount,
    int? SymbolCount,
    int? DependencyCount,
    int? FindingCount,
    bool CountsAreComplete,
    bool GroupingsAreComplete,
    IReadOnlyDictionary<string, int> FindingsBySeverity,
    IReadOnlyDictionary<string, int> FindingsByRule,
    IReadOnlyDictionary<string, int> FindingsByFile,
    AnalysisReportDependencyStatistics? DependencyStatistics);

/// <summary>Optional local Git context. It is explicitly marked as current context unless captured by a future run model.</summary>
public sealed record AnalysisReportGitContext(
    GitRepositoryState State,
    string? RepositoryRootPath,
    bool RepositoryRootPathRedacted,
    string? RepositoryRootDirectoryName,
    string? BranchName,
    bool IsDetached,
    string? HeadCommitId,
    string? HeadCommitMessage,
    string? HeadCommitAuthor,
    DateTimeOffset? HeadCommitDateUtc,
    bool IsDirty,
    int ChangedFileCount,
    int StagedChangeCount,
    int UnstagedChangeCount,
    int UntrackedFileCount,
    string? ErrorMessage,
    bool IsAssociatedWithAnalysisRun,
    string AssociationNote);

/// <summary>Complete deterministic report model for one explicitly selected persisted scan.</summary>
public sealed record AnalysisReportModel(
    string SchemaVersion,
    string ApplicationVersion,
    string FingerprintVersion,
    AnalysisReportProject Project,
    AnalysisReportScan Scan,
    AnalysisReportSummary Summary,
    IReadOnlyList<AnalysisReportSourceFile> SourceFiles,
    IReadOnlyList<AnalysisReportSymbol> Symbols,
    IReadOnlyList<AnalysisReportDependency> Dependencies,
    IReadOnlyList<AnalysisReportFinding> Findings,
    IReadOnlyList<string> Limitations,
    AnalysisReportGitContext? GitContext);

/// <summary>Result returned after an export has been atomically written.</summary>
public sealed record AnalysisReportExportResult(
    string OutputPath,
    AnalysisReportFormat Format,
    long BytesWritten,
    AnalysisReportModel Report);

/// <summary>Fingerprint-based change classification for persisted findings.</summary>
public enum AnalysisFindingChangeKind
{
    Unchanged,
    New,
    Removed,
    Changed
}

/// <summary>One comparison item. Database-generated IDs are intentionally absent.</summary>
public sealed record AnalysisFindingChange(
    AnalysisFindingChangeKind Kind,
    string? BaselineFingerprint,
    string? CurrentFingerprint,
    AnalysisReportFinding? BaselineFinding,
    AnalysisReportFinding? CurrentFinding,
    IReadOnlyList<string> Differences,
    bool IsReliable,
    string? Note);

/// <summary>Baseline/current count pair used for severity, rule, and file deltas.</summary>
public sealed record AnalysisComparisonCountDelta(int Baseline, int Current)
{
    public int Delta => Current - Baseline;
}

/// <summary>Read-only comparison of two persisted scan snapshots for the same project.</summary>
public sealed record AnalysisComparisonResult(
    string SchemaVersion,
    string FingerprintVersion,
    Guid ProjectId,
    string ProjectName,
    Guid BaselineScanId,
    Guid CurrentScanId,
    string BaselineAnalysisStatus,
    string CurrentAnalysisStatus,
    int? BaselineFindingCount,
    int? CurrentFindingCount,
    int UnchangedCount,
    int NewCount,
    int RemovedCount,
    int ChangedCount,
    IReadOnlyDictionary<string, AnalysisComparisonCountDelta> FindingsBySeverity,
    IReadOnlyDictionary<string, AnalysisComparisonCountDelta> FindingsByRule,
    IReadOnlyDictionary<string, AnalysisComparisonCountDelta> FindingsByFile,
    IReadOnlyList<AnalysisFindingChange> Changes,
    IReadOnlyList<string> Limitations,
    bool IsReliable)
{
    public IReadOnlyList<AnalysisFindingChange> UnchangedFindings
        => Changes.Where(change => change.Kind == AnalysisFindingChangeKind.Unchanged).ToArray();

    public IReadOnlyList<AnalysisFindingChange> NewFindings
        => Changes.Where(change => change.Kind == AnalysisFindingChangeKind.New).ToArray();

    public IReadOnlyList<AnalysisFindingChange> RemovedFindings
        => Changes.Where(change => change.Kind == AnalysisFindingChangeKind.Removed).ToArray();

    public IReadOnlyList<AnalysisFindingChange> ChangedFindings
        => Changes.Where(change => change.Kind == AnalysisFindingChangeKind.Changed).ToArray();
}

/// <summary>Export failures are actionable IO failures and never silently replace an unrelated file.</summary>
public sealed class AnalysisReportExportException : IOException
{
    public AnalysisReportExportException(string message)
        : base(message)
    {
    }

    public AnalysisReportExportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
