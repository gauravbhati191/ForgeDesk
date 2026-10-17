using ForgeDesk.Domain;

namespace ForgeDesk.Graph;

/// <summary>Builds the original dependency-only graph shape for compatibility callers.</summary>
public sealed class DependencyGraphBuilder : IDependencyGraphBuilder
{
    public Task<DependencyGraph> BuildAsync(
        IReadOnlyCollection<Dependency> dependencies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        cancellationToken.ThrowIfCancellationRequested();

        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new Dictionary<string, GraphEdge>(StringComparer.Ordinal);
        foreach (var dependency in dependencies
            .OrderBy(item => item.SourceSymbolId)
            .ThenBy(item => item.TargetSymbolId)
            .ThenBy(item => item.RelationshipType, StringComparer.Ordinal)
            .ThenBy(item => item.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dependency.SourceSymbolId is not Guid sourceId || dependency.TargetSymbolId is not Guid targetId)
            {
                continue;
            }

            var from = sourceId.ToString("D");
            var to = targetId.ToString("D");
            if (StringComparer.Ordinal.Equals(from, to))
            {
                continue;
            }

            nodes.TryAdd(from, new GraphNode(from, from, SymbolKind.Unknown));
            nodes.TryAdd(to, new GraphNode(to, to, SymbolKind.Unknown));
            var relationship = dependency.RelationshipType.Trim();
            var key = $"{from}\u001f{to}\u001f{relationship.ToUpperInvariant()}";
            edges.TryAdd(
                key,
                new GraphEdge(
                    from,
                    to,
                    relationship,
                    Enum.TryParse<DependencyKind>(relationship, true, out var kind) ? kind : DependencyKind.Unknown,
                    dependency.Id,
                    dependency.ScanId,
                    dependency.IsResolved));
        }

        return Task.FromResult(new DependencyGraph(
            nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray(),
            edges.Values.OrderBy(edge => edge.FromId, StringComparer.Ordinal).ThenBy(edge => edge.ToId, StringComparer.Ordinal).ToArray()));
    }
}
