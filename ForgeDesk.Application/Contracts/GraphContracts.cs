using ForgeDesk.Graph;

namespace ForgeDesk.Application;

/// <summary>A versioned graph request used by asynchronous callers to reject stale results.</summary>
public sealed record GraphQueryRequest
{
    public GraphQueryRequest(GraphQueryOptions options, long requestVersion = 0)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        RequestVersion = requestVersion;
    }

    public GraphQueryRequest(
        Guid scanId,
        GraphFilterOptions? filters = null,
        GraphSelection? selection = null,
        long requestVersion = 0)
        : this(new GraphQueryOptions(scanId, filters, selection), requestVersion)
    {
    }

    public GraphQueryOptions Options { get; }
    public long RequestVersion { get; }
}

/// <summary>Graph query response carrying both data quality and request freshness metadata.</summary>
public sealed record GraphQueryResult(
    GraphData Data,
    long RequestVersion,
    bool IsStaleRequest)
{
    public bool IsCurrent => !IsStaleRequest;
    public GraphDataState State => Data.State;
}

/// <summary>Application boundary for graph data retrieval and stale-result detection.</summary>
public interface IGraphQueryService
{
    Task<GraphQueryResult> QueryAsync(
        GraphQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<GraphData> GetAsync(
        GraphQueryOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>Vocabulary alias for hosts that call the application boundary a graph service.</summary>
public interface IGraphService : IGraphQueryService
{
}
