namespace ForgeDesk.Application;

/// <summary>Application boundary for bounded, local, read-only Git inspection.</summary>
public interface IGitRepositoryService
{
    /// <summary>
    /// Finds the nearest repository at <paramref name="projectRoot"/> or one of its parents,
    /// then reads status, HEAD metadata, and bounded recent history.
    /// </summary>
    Task<GitRepositorySnapshot> InspectAsync(
        string projectRoot,
        GitInspectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Refreshes only bounded history for the nearest repository.</summary>
    Task<GitHistoryResult> GetHistoryAsync(
        string projectRoot,
        GitHistoryOptions? options = null,
        CancellationToken cancellationToken = default);
}
