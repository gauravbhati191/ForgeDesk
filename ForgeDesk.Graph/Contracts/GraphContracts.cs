using ForgeDesk.Domain;

namespace ForgeDesk.Graph;

/// <summary>Stable node identity for a future dependency graph.</summary>
public sealed record GraphNode(string Id, string Label, DependencyKind Kind);

/// <summary>Directed edge between two dependency-graph nodes.</summary>
public sealed record GraphEdge(string FromId, string ToId, DependencyKind Kind);

/// <summary>Immutable graph read model reserved for future graph construction.</summary>
public sealed record DependencyGraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

/// <summary>
/// Extension point for future dependency graph construction. Phase 1 deliberately
/// contains contracts only and no fabricated graph implementation.
/// </summary>
public interface IDependencyGraphBuilder
{
    Task<DependencyGraph> BuildAsync(IReadOnlyCollection<Dependency> dependencies, CancellationToken cancellationToken = default);
}
