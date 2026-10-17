using ForgeDesk.Graph;

namespace ForgeDesk.Application;

/// <summary>
/// Coordinates graph reads for application callers. Request versions are independent from
/// persisted analysis state: an older, slower response is marked stale rather than replacing a
/// newer caller selection.
/// </summary>
public sealed class GraphQueryService : IGraphQueryService, IGraphService
{
    private readonly IGraphDataProvider _provider;
    private long _latestRequestVersion;

    public GraphQueryService(IGraphDataProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<GraphQueryResult> QueryAsync(
        GraphQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestVersion = request.RequestVersion;
        if (requestVersion <= 0)
        {
            requestVersion = Interlocked.Increment(ref _latestRequestVersion);
        }
        else
        {
            AdvanceLatestVersion(requestVersion);
        }

        var data = await _provider
            .GetAsync(request.Options, cancellationToken)
            .ConfigureAwait(false);
        var stale = requestVersion < Volatile.Read(ref _latestRequestVersion);
        return new GraphQueryResult(data, requestVersion, stale);
    }

    public async Task<GraphData> GetAsync(
        GraphQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = await QueryAsync(new GraphQueryRequest(options), cancellationToken).ConfigureAwait(false);
        return result.Data;
    }

    public Task<GraphData> LoadAsync(
        GraphQueryOptions options,
        CancellationToken cancellationToken = default)
        => GetAsync(options, cancellationToken);

    private void AdvanceLatestVersion(long requestVersion)
    {
        while (true)
        {
            var current = Volatile.Read(ref _latestRequestVersion);
            if (requestVersion <= current
                || Interlocked.CompareExchange(ref _latestRequestVersion, requestVersion, current) == current)
            {
                return;
            }
        }
    }
}
