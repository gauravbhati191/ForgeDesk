using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>One discovered C# source file supplied to the semantic analysis engine.</summary>
public sealed record AnalysisFileInput(
    Guid SourceFileId,
    string RelativePath,
    string NormalizedPath,
    string Content);

/// <summary>Configurable, deterministic quality-rule settings.</summary>
public sealed record AnalysisQualityOptions(
    int MaxMethodLines = 50,
    bool EnableTodoCommentRule = true,
    bool EnableLongMethodRule = true,
    bool IncludeCompilerDiagnostics = false,
    IReadOnlySet<string>? EnabledRuleIds = null,
    int MaxParameters = 5,
    bool EnableEmptyCatchRule = true,
    bool EnableExcessiveParametersRule = true)
{
    public bool IsRuleEnabled(string ruleId)
        => EnabledRuleIds is null || EnabledRuleIds.Any(item => StringComparer.Ordinal.Equals(item, ruleId));
}

/// <summary>Stable IDs for the built-in deterministic quality rules.</summary>
public static class AnalysisRuleIds
{
    public const string TodoOrFixme = "FD001";
    public const string LongMethod = "FD002";
    public const string ExcessiveParameters = "FD003";
    public const string EmptyCatch = "FD004";
}

/// <summary>Describes whether analysis used a project-aware MSBuild workspace or a source fallback.</summary>
public enum AnalysisBindingMode
{
    MsBuildSemantic = 0,
    SyntaxOnlyFallback = 1
}

/// <summary>Input for one semantic analysis pass.</summary>
public sealed record AnalysisRequest(
    Guid ScanId,
    Guid ProjectId,
    string ProjectRootPath,
    IReadOnlyList<AnalysisFileInput> Files,
    AnalysisQualityOptions? QualityOptions = null);

/// <summary>One symbol extracted from a source declaration by Roslyn.</summary>
public sealed record AnalysisSymbolResult(
    Guid SourceFileId,
    string SymbolKey,
    string Name,
    SymbolKind Kind,
    int? StartLine,
    int? EndLine);

/// <summary>One supported semantic relationship between extracted symbols.</summary>
public sealed record AnalysisDependencyResult(
    string SourceSymbolKey,
    string? TargetSymbolKey,
    string RelationshipType,
    bool IsResolved);

/// <summary>One deterministic quality-rule or compiler diagnostic result.</summary>
public sealed record AnalysisFindingResult(
    string RuleId,
    FindingSeverity Severity,
    string Message,
    Guid? SourceFileId = null,
    string? SymbolKey = null,
    int? StartLine = null,
    int? EndLine = null);

/// <summary>All records produced by one analysis pass before persistence identifiers are assigned.</summary>
public sealed record AnalysisResult(
    IReadOnlyList<AnalysisSymbolResult> Symbols,
    IReadOnlyList<AnalysisDependencyResult> Dependencies,
    IReadOnlyList<AnalysisFindingResult> Findings,
    AnalysisBindingMode BindingMode = AnalysisBindingMode.SyntaxOnlyFallback,
    string? BindingMessage = null);

/// <summary>Progress reported while analysis parses, binds, and evaluates source files.</summary>
public sealed record AnalysisProgress(
    string Phase,
    int FilesProcessed,
    int FilesTotal,
    int SymbolsFound,
    int DependenciesFound,
    int FindingsFound,
    string? CurrentPath = null,
    AnalysisBindingMode? BindingMode = null,
    string? BindingMessage = null);

/// <summary>Context loaded from persistence for an analysis run.</summary>
public sealed record AnalysisContext(
    Scan Scan,
    Project Project,
    IReadOnlyList<SourceFile> SourceFiles);

/// <summary>Application boundary implemented by the Roslyn analysis adapter.</summary>
public interface IAnalysisEngine
{
    Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken = default);

    Task<AnalysisResult> AnalyzeAsync(
        AnalysisRequest request,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken = default);
}

/// <summary>Persistence boundary for analysis state and scan-owned results.</summary>
public interface IAnalysisRepository
{
    Task<AnalysisContext?> GetContextAsync(Guid scanId, CancellationToken cancellationToken = default);

    Task SaveStatusAsync(Scan scan, CancellationToken cancellationToken = default);

    /// <summary>Removes prior result rows before a new run so a failed retry cannot expose stale success data.</summary>
    Task ClearResultsAsync(Guid scanId, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces all analysis rows for a scan, making retries idempotent.</summary>
    Task ReplaceResultsAsync(
        Guid scanId,
        IReadOnlyCollection<Symbol> symbols,
        IReadOnlyCollection<Dependency> dependencies,
        IReadOnlyCollection<Finding> findings,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs semantic analysis for a completed discovery scan and persists its result.</summary>
public interface IAnalysisService
{
    Task<AnalysisRunDto> AnalyzeAsync(
        Guid scanId,
        AnalysisQualityOptions? qualityOptions = null,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
