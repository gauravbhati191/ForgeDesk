using ForgeDesk.Application;

namespace ForgeDesk.UnitTests;

public sealed class GitCoreTests
{
    [Fact]
    public async Task Service_uses_the_nearest_repository_marker_for_a_nested_project()
    {
        using var directory = new TemporaryDirectory();
        var outer = Directory.CreateDirectory(Path.Combine(directory.Path, "outer"));
        var repository = Directory.CreateDirectory(Path.Combine(outer.FullName, "repository"));
        var project = Directory.CreateDirectory(Path.Combine(repository.FullName, "src", "Project"));
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        Directory.CreateDirectory(Path.Combine(repository.FullName, ".git"));
        var reader = new RecordingGitReader();
        var service = new GitRepositoryService(reader);

        var result = await service.InspectAsync(project.FullName);

        Assert.Equal(Path.GetFullPath(repository.FullName), reader.LastRepositoryRoot);
        Assert.Equal(GitRepositoryState.Clean, result.State);
        Assert.Equal(reader.LastRepositoryRoot, result.RepositoryRootPath);
    }

    [Fact]
    public async Task Service_accepts_worktree_file_metadata_without_inspecting_unrelated_parents()
    {
        using var directory = new TemporaryDirectory();
        var project = Directory.CreateDirectory(Path.Combine(directory.Path, "project"));
        File.WriteAllText(Path.Combine(project.FullName, ".git"), "gitdir: /temporary/worktree/metadata");
        var reader = new RecordingGitReader();

        var result = await new GitRepositoryService(reader).InspectAsync(project.FullName);

        Assert.Equal(Path.GetFullPath(project.FullName), reader.LastRepositoryRoot);
        Assert.Equal(GitRepositoryState.Clean, result.State);
    }

    [Fact]
    public async Task Service_returns_an_explicit_missing_repository_state()
    {
        using var directory = new TemporaryDirectory();

        var result = await new GitRepositoryService(new RecordingGitReader())
            .InspectAsync(directory.Path);

        Assert.Equal(GitRepositoryState.NotRepository, result.State);
        Assert.Null(result.RepositoryRootPath);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task Service_honors_cancellation_before_touching_the_filesystem()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new GitRepositoryService(new RecordingGitReader())
            .InspectAsync(directory.Path, cancellationToken: cancellation.Token));
    }

    private sealed class RecordingGitReader : IGitRepositoryReader
    {
        public string? LastRepositoryRoot { get; private set; }

        public Task<GitRepositorySnapshot> ReadAsync(
            string repositoryRoot,
            GitInspectionOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRepositoryRoot = Path.GetFullPath(repositoryRoot);
            return Task.FromResult(new GitRepositorySnapshot(
                GitRepositoryState.Clean,
                LastRepositoryRoot,
                new GitBranchInfo("main", false),
                null,
                Array.Empty<GitFileChange>(),
                Array.Empty<GitCommitInfo>()));
        }

        public Task<IReadOnlyList<GitCommitInfo>> ReadHistoryAsync(
            string repositoryRoot,
            GitHistoryOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GitCommitInfo>>(Array.Empty<GitCommitInfo>());
    }
}
