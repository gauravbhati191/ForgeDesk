using ForgeDesk.Domain;

namespace ForgeDesk.Analysis;

/// <summary>Compatibility input contract for callers from the Phase 1 analysis boundary.</summary>
public sealed record AnalysisRequest(Guid ScanId, IReadOnlyList<AnalysisFileInput> Files);

/// <summary>Minimal source input accepted by the compatibility analyzer boundary.</summary>
public sealed record AnalysisFileInput(Guid SourceFileId, string NormalizedPath, string Content);

/// <summary>Compatibility symbol information before persistence.</summary>
public sealed record AnalysisSymbolDraft(string Name, SymbolKind Kind, int Line, int Column, string? FullyQualifiedName = null);

/// <summary>Compatibility finding information before persistence.</summary>
public sealed record AnalysisFindingDraft(string RuleId, FindingSeverity Severity, string Message, Guid? SourceFileId = null, int? Line = null, int? Column = null);

/// <summary>Compatibility output for callers that predate dependency extraction.</summary>
public sealed record AnalysisResult(IReadOnlyList<AnalysisSymbolDraft> Symbols, IReadOnlyList<AnalysisFindingDraft> Findings);

/// <summary>Compatibility extension point implemented by the Roslyn analysis engine.</summary>
public interface IAnalysisEngine
{
    Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken = default);
}
