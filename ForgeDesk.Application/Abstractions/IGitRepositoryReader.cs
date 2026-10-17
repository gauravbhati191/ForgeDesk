namespace ForgeDesk.Application;

/// <summary>
/// Infrastructure boundary for reading an already located repository. Implementations must
/// never mutate repository state or contact a remote.
/// </summary>
public interface IGitRepositoryReader
{
    Task<GitRepositorySnapshot> ReadAsync(
        string repositoryRoot,
        GitInspectionOptions options,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitCommitInfo>> ReadHistoryAsync(
        string repositoryRoot,
        GitHistoryOptions options,
        CancellationToken cancellationToken = default);
}
