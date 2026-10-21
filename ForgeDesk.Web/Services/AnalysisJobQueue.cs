using System.Threading.Channels;

namespace ForgeDesk.Web.Services;

public sealed record AnalysisJob(Guid ProjectId, Guid ScanId);

public sealed class AnalysisJobQueue
{
    private readonly Channel<AnalysisJob> _jobs = Channel.CreateUnbounded<AnalysisJob>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public ValueTask EnqueueAsync(AnalysisJob job, CancellationToken cancellationToken = default)
        => _jobs.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<AnalysisJob> ReadAllAsync(CancellationToken cancellationToken = default)
        => _jobs.Reader.ReadAllAsync(cancellationToken);
}
