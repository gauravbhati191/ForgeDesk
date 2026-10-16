using ForgeDesk.Domain;

namespace ForgeDesk.Analysis;

/// <summary>Input contract reserved for a future language-aware analysis pipeline.</summary>
public sealed record AnalysisRequest(Guid ScanId, IReadOnlyList<AnalysisFileInput> Files);

/// <summary>Minimal source input required by a future analyzer implementation.</summary>
public sealed record AnalysisFileInput(Guid SourceFileId, string NormalizedPath, string Content);

/// <summary>Analyzer-produced symbol information before persistence.</summary>
public sealed record AnalysisSymbolDraft(string Name, SymbolKind Kind, int Line, int Column, string? FullyQualifiedName = null);

/// <summary>Analyzer-produced finding information before persistence.</summary>
public sealed record AnalysisFindingDraft(string RuleId, FindingSeverity Severity, string Message, Guid? SourceFileId = null, int? Line = null, int? Column = null);

/// <summary>Output contract reserved for future analyzer implementations.</summary>
public sealed record AnalysisResult(IReadOnlyList<AnalysisSymbolDraft> Symbols, IReadOnlyList<AnalysisFindingDraft> Findings);

/// <summary>
/// Extension point for future language analyzers. Phase 1 deliberately provides no
/// implementation or claim of static-analysis support.
/// </summary>
public interface IAnalysisEngine
{
    Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken = default);
}
