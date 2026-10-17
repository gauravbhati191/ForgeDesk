using ForgeDesk.Domain;

namespace ForgeDesk.Presentation.Models;

/// <summary>One positioned node in the WPF graph projection.</summary>
public sealed record DependencyGraphNodeItem(
    Guid Id,
    string DisplayName,
    string SymbolKey,
    SymbolKind Kind,
    string? RelativePath,
    int? StartLine,
    int? EndLine,
    double X,
    double Y,
    bool IsSelected,
    bool IsNeighbor,
    bool IsIsolated)
{
    public string KindLabel => Kind.ToString();

    public string LocationLabel
    {
        get
        {
            var path = string.IsNullOrWhiteSpace(RelativePath)
                ? "Location unavailable"
                : RelativePath.Replace('\\', '/');
            return StartLine is null
                ? path
                : EndLine is not null && EndLine != StartLine
                    ? $"{path}:{StartLine}-{EndLine}"
                    : $"{path}:{StartLine}";
        }
    }
}

/// <summary>One directed edge in the WPF graph projection.</summary>
public sealed record DependencyGraphEdgeItem(
    Guid Id,
    Guid FromId,
    Guid ToId,
    string RelationshipType,
    bool IsResolved,
    bool IsHighlighted,
    string FromName,
    string ToName);

/// <summary>Deterministic layout and filtered graph data for one selected scan.</summary>
public sealed record DependencyGraphProjection(
    IReadOnlyList<DependencyGraphNodeItem> Nodes,
    IReadOnlyList<DependencyGraphEdgeItem> Edges,
    int TotalNodeCount,
    int TotalEdgeCount,
    int PersistedUnresolvedEdgeCount,
    bool IsPartial,
    string? StateMessage = null)
{
    public int VisibleNodeCount => Nodes.Count;

    public int VisibleEdgeCount => Edges.Count;

    public bool HasNodes => Nodes.Count > 0;

    public bool HasEdges => Edges.Count > 0;
}
