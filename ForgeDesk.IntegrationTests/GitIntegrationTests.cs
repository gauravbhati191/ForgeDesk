using ForgeDesk.Application;
using ForgeDesk.Infrastructure;

namespace ForgeDesk.IntegrationTests;

public sealed class GitIntegrationTests
{
    [Fact]
    public async Task No_commit_repository_is_reported_clean_without_fabricated_head()
    {
        using var repository = new GitTestRepository();
        var project = Directory.CreateDirectory(Path.Combine(repository.Root, "src", "Project"));
        var snapshot = await CreateService().InspectAsync(project.FullName);

        Assert.Equal(GitRepositoryState.Clean, snapshot.State);
        Assert.Equal(Path.GetFullPath(repository.Root), snapshot.RepositoryRootPath);
        Assert.Equal("main", snapshot.BranchName);
        Assert.Null(snapshot.HeadCommit);
        Assert.Empty(snapshot.Changes);
        Assert.Empty(snapshot.RecentCommits);
    }

    [Fact]
    public async Task Clean_repository_reports_head_metadata_and_changed_files_for_history()
    {
        using var repository = new GitTestRepository();
        repository.Write("src/Program.cs", "class Program { }\n");
        repository.Write("README.md", "ForgeDesk\n");
        repository.Commit("initial repository");
        Assert.Empty(repository.Run("remote"));

        var snapshot = await CreateService().InspectAsync(repository.Root);

        Assert.Equal(GitRepositoryState.Clean, snapshot.State);
        Assert.NotNull(snapshot.HeadCommit);
        Assert.Equal("initial repository", snapshot.HeadCommit!.Message);
        Assert.Equal("ForgeDesk Test Author", snapshot.HeadCommit.AuthorName);
        Assert.NotNull(snapshot.HeadCommit.AuthorDateUtc);
        Assert.Empty(snapshot.HeadCommit.ParentIds);
        Assert.Single(snapshot.RecentCommits);
        Assert.Contains(snapshot.RecentCommits[0].ChangedFiles, change => change.RelativePath == "src/Program.cs");
        Assert.Contains(snapshot.RecentCommits[0].ChangedFiles, change => change.RelativePath == "README.md");
        var history = await CreateService().GetHistoryAsync(
            repository.Root,
            new GitHistoryOptions { MaxCommits = 1 });
        Assert.Equal(GitRepositoryState.Clean, history.State);
        Assert.Single(history.Commits);
        Assert.False(history.IsTruncated);
    }

    [Fact]
    public async Task Dirty_repository_groups_staged_unstaged_untracked_and_renamed_paths()
    {
        using var repository = new GitTestRepository();
        repository.Write("tracked.txt", "one\n");
        repository.Write("rename-before.txt", "rename me\n");
        repository.Commit("base");

        repository.Write("tracked.txt", "two\n");
        repository.Run("add", "tracked.txt");
        repository.Write("tracked.txt", "three\n");
        repository.Write("staged.txt", "staged\n");
        repository.Run("add", "staged.txt");
        repository.Write("untracked.txt", "untracked\n");
        repository.Run("mv", "rename-before.txt", "rename-after.txt");

        var snapshot = await CreateService().InspectAsync(repository.Root);

        Assert.Equal(GitRepositoryState.Dirty, snapshot.State);
        var tracked = Assert.Single(snapshot.Changes, change => change.RelativePath == "tracked.txt");
        Assert.True(tracked.IsStaged);
        Assert.True(tracked.IsUnstaged);
        Assert.Equal(GitChangeKind.Modified, tracked.Kind);
        Assert.Contains(snapshot.StagedChanges, change => change.RelativePath == "staged.txt");
        Assert.Contains(snapshot.UntrackedChanges, change => change.RelativePath == "untracked.txt");
        var renamed = Assert.Single(snapshot.Changes, change => change.RelativePath == "rename-after.txt");
        Assert.Equal(GitChangeKind.Renamed, renamed.Kind);
        Assert.Equal("rename-before.txt", renamed.OriginalRelativePath);

        var bounded = await CreateService().InspectAsync(
            repository.Root,
            new GitInspectionOptions
            {
                MaxChangedFiles = 1,
                MaxRecentCommits = 0
            });
        Assert.Single(bounded.Changes);
        Assert.True(bounded.ChangesTruncated);
    }

    [Fact]
    public async Task Conflicted_repository_reports_conflict_state_without_mutating_the_merge()
    {
        using var repository = new GitTestRepository();
        repository.Write("conflict.txt", "base\n");
        repository.Commit("base");
        repository.Run("checkout", "-q", "-b", "feature");
        repository.Write("conflict.txt", "feature\n");
        repository.Commit("feature change");
        repository.Run("checkout", "-q", "main");
        repository.Write("conflict.txt", "main\n");
        repository.Commit("main change");
        var merge = repository.RunAllowFailure("merge", "feature");
        Assert.NotEqual(0, merge.ExitCode);

        var snapshot = await CreateService().InspectAsync(repository.Root);

        var conflict = Assert.Single(snapshot.ConflictedChanges);
        Assert.Equal("conflict.txt", conflict.RelativePath);
        Assert.Equal(GitChangeKind.Conflicted, conflict.Kind);
        Assert.Equal(GitRepositoryState.Dirty, snapshot.State);
    }

    [Fact]
    public async Task History_is_bounded_and_reports_parent_ids_and_changed_file_limits()
    {
        using var repository = new GitTestRepository();
        repository.Write("one.txt", "one\n");
        repository.Commit("commit one");
        repository.Write("two.txt", "two\n");
        repository.Commit("commit two");
        repository.Write("three.txt", "three\n");
        repository.Write("four.txt", "four\n");
        repository.Commit("commit three");
        repository.Write("five.txt", "five\n");
        repository.Commit("commit four");

        var snapshot = await CreateService().InspectAsync(
            repository.Root,
            new GitInspectionOptions
            {
                MaxRecentCommits = 2,
                MaxChangedFilesPerCommit = 1
            });

        Assert.Equal(2, snapshot.RecentCommits.Count);
        Assert.True(snapshot.HistoryTruncated);
        Assert.Equal("commit four", snapshot.RecentCommits[0].Message);
        Assert.Contains(snapshot.RecentCommits[0].ParentIds, parent => parent == snapshot.RecentCommits[1].Id);
        Assert.Single(snapshot.RecentCommits[0].ChangedFiles);
        Assert.True(snapshot.RecentCommits[1].ChangedFilesTruncated);

        var history = await CreateService().GetHistoryAsync(
            repository.Root,
            new GitHistoryOptions { MaxCommits = 2 });
        Assert.Equal(2, history.Commits.Count);
        Assert.True(history.IsTruncated);
    }

    [Fact]
    public async Task Worktree_git_file_is_detected_as_the_worktree_root()
    {
        using var repository = new GitTestRepository();
        repository.Write("tracked.txt", "tracked\n");
        repository.Commit("base");
        using var worktreeDirectory = new TemporaryDirectory();
        Directory.Delete(worktreeDirectory.Path);
        repository.Run("worktree", "add", "-q", worktreeDirectory.Path, "-b", "worktree");

        var snapshot = await CreateService().InspectAsync(worktreeDirectory.Path);

        Assert.Equal(GitRepositoryState.Clean, snapshot.State);
        Assert.Equal(Path.GetFullPath(worktreeDirectory.Path), snapshot.RepositoryRootPath);
        Assert.True(File.Exists(Path.Combine(worktreeDirectory.Path, ".git")));
    }

    [Fact]
    public async Task Missing_and_malformed_repositories_have_distinct_explicit_states()
    {
        using var missing = new TemporaryDirectory();
        using var malformed = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(malformed.Path, ".git"), "not valid git metadata\n");
        var service = CreateService();

        var missingSnapshot = await service.InspectAsync(missing.Path);
        var malformedSnapshot = await service.InspectAsync(malformed.Path);

        Assert.Equal(GitRepositoryState.NotRepository, missingSnapshot.State);
        Assert.Equal(GitRepositoryState.Unreadable, malformedSnapshot.State);
        Assert.NotNull(malformedSnapshot.ErrorMessage);
        Assert.Empty(malformedSnapshot.Changes);
    }

    private static IGitRepositoryService CreateService()
        => new GitRepositoryService(new GitProcessRepository());
}
