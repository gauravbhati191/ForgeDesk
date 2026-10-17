namespace ForgeDesk.Graph;

/// <summary>Loads a scan snapshot, builds the real graph, and applies temporary layout coordinates.</summary>
public sealed class GraphDataProvider : IGraphDataProvider
{
    private readonly IGraphQueryRepository _repository;
    private readonly GraphDataBuilder _builder;
    private readonly IGraphLayoutService _layoutService;

    public GraphDataProvider(
        IGraphQueryRepository repository,
        GraphDataBuilder? builder = null,
        IGraphLayoutService? layoutService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _builder = builder ?? new GraphDataBuilder();
        _layoutService = layoutService ?? new DeterministicGraphLayoutService();
    }

    public async Task<GraphData> GetAsync(
        GraphQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await _repository
            .GetSnapshotAsync(options.ScanId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return GraphData.Missing(options.ScanId, "The requested scan was not found.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var graph = _builder.Build(snapshot, options, cancellationToken);
        return _layoutService.Layout(graph, cancellationToken: cancellationToken);
    }

    public Task<GraphData> GetGraphAsync(
        GraphQueryOptions options,
        CancellationToken cancellationToken = default)
        => GetAsync(options, cancellationToken);

    public Task<GraphData> LoadAsync(
        GraphQueryOptions options,
        CancellationToken cancellationToken = default)
        => GetAsync(options, cancellationToken);
}
