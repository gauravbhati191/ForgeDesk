namespace ForgeDesk.Presentation.Models;

/// <summary>Read-only dependency metadata loaded from one persisted analysis scan.</summary>
public sealed record AnalysisDependencyItem(
    Guid Id,
    Guid ScanId,
    Guid? SourceSymbolId,
    Guid? TargetSymbolId,
    string RelationshipType,
    bool IsResolved,
    string? SourceName = null,
    string? TargetName = null);
