namespace ForgeDesk.Graph;

/// <summary>
/// Places visible nodes in a stable row-major grid. The graph layer deliberately keeps this
/// layout in memory; coordinates are presentation state and are never persisted with analysis.
/// </summary>
public sealed class DeterministicGraphLayoutService : IGraphLayoutService
{
    public GraphData Layout(
        GraphData graph,
        GraphLayoutOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        cancellationToken.ThrowIfCancellationRequested();

        var layoutOptions = options ?? new GraphLayoutOptions();
        layoutOptions.Validate();
        if (graph.Nodes.Count == 0)
        {
            return graph;
        }

        var ordered = graph.Nodes
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ThenBy(node => node.Label, StringComparer.Ordinal)
            .ToArray();
        var columns = layoutOptions.Columns
            ?? Math.Max(1, (int)Math.Ceiling(Math.Sqrt(ordered.Length)));

        var positions = new Dictionary<string, GraphPoint>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = index % columns;
            var row = index / columns;
            positions[ordered[index].Id] = new(
                layoutOptions.OriginX + column * layoutOptions.HorizontalSpacing,
                layoutOptions.OriginY + row * layoutOptions.VerticalSpacing);
        }

        var nodes = graph.Nodes
            .Select(node =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var point = positions[node.Id];
                return node with { X = point.X, Y = point.Y };
            })
            .ToArray();

        return graph with { Nodes = nodes };
    }

    public Task<GraphData> LayoutAsync(
        GraphData graph,
        GraphLayoutOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Layout(graph, options, cancellationToken));
}
