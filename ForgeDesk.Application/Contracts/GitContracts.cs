namespace ForgeDesk.Application;

/// <summary>Describes the usable state of a repository discovered for a project.</summary>
public enum GitRepositoryState
{
    /// <summary>No <c>.git</c> directory or worktree metadata was found in the project path or its parents.</summary>
    NotRepository,

    /// <summary>A repository was read successfully and has no reported working-tree changes.</summary>
    Clean,

    /// <summary>A repository was read successfully and has one or more reported working-tree changes.</summary>
    Dirty,

    /// <summary>Repository metadata was found, but the repository could not be read safely.</summary>
    Unreadable
}

/// <summary>The kind of a path change reported by Git.</summary>
public enum GitChangeKind
{
    Unknown,
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Untracked,
    Conflicted
}

/// <summary>Bounded limits used while reading a local repository.</summary>
public sealed record GitInspectionOptions
{
    /// <summary>Maximum number of working-tree paths retained in a snapshot.</summary>
    public int MaxChangedFiles { get; init; } = 2_000;

    /// <summary>Maximum number of recent commits retained in a snapshot.</summary>
    public int MaxRecentCommits { get; init; } = 25;

    /// <summary>Maximum number of changed paths retained for each history entry.</summary>
    public int MaxChangedFilesPerCommit { get; init; } = 200;

    /// <summary>Maximum time allowed for each Git subprocess.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum combined byte count read from either redirected subprocess stream.</summary>
    public int MaxCommandOutputBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Whether recent history should be loaded along with status.</summary>
    public bool IncludeHistory { get; init; } = true;

    public void Validate()
    {
        if (MaxChangedFiles is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChangedFiles), "MaxChangedFiles must be between 1 and 100000.");
        }

        if (MaxRecentCommits is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRecentCommits), "MaxRecentCommits must be between 0 and 10000.");
        }

        if (MaxChangedFilesPerCommit is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChangedFilesPerCommit), "MaxChangedFilesPerCommit must be between 1 and 100000.");
        }

        if (CommandTimeout <= TimeSpan.Zero || CommandTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(CommandTimeout), "CommandTimeout must be greater than zero and no longer than five minutes.");
        }

        if (MaxCommandOutputBytes is < 4_096 or > 128 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCommandOutputBytes), "MaxCommandOutputBytes must be between 4096 and 128 MiB.");
        }
    }
}

/// <summary>Independent bounded history limits for callers that refresh only history.</summary>
public sealed record GitHistoryOptions
{
    public int MaxCommits { get; init; } = 25;
    public int MaxChangedFilesPerCommit { get; init; } = 200;
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxCommandOutputBytes { get; init; } = 8 * 1024 * 1024;

    public void Validate()
    {
        if (MaxCommits is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCommits), "MaxCommits must be between 0 and 10000.");
        }

        if (MaxChangedFilesPerCommit is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChangedFilesPerCommit), "MaxChangedFilesPerCommit must be between 1 and 100000.");
        }

        if (CommandTimeout <= TimeSpan.Zero || CommandTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(CommandTimeout), "CommandTimeout must be greater than zero and no longer than five minutes.");
        }

        if (MaxCommandOutputBytes is < 4_096 or > 128 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCommandOutputBytes), "MaxCommandOutputBytes must be between 4096 and 128 MiB.");
        }
    }
}

/// <summary>Current branch information. A null name means detached HEAD or no branch yet.</summary>
public sealed record GitBranchInfo(string? Name, bool IsDetached);

/// <summary>A path reported by the Git working tree.</summary>
public sealed record GitFileChange(
    string RelativePath,
    GitChangeKind Kind,
    bool IsStaged,
    bool IsUnstaged,
    bool IsUntracked,
    bool IsConflicted,
    string IndexStatus,
    string WorkingTreeStatus,
    string? OriginalRelativePath = null)
{
    /// <summary>The two-character porcelain status as emitted by Git.</summary>
    public string StatusCode => IndexStatus + WorkingTreeStatus;

    /// <summary>Whether this entry has an old path in addition to its current path.</summary>
    public bool IsRename => Kind == GitChangeKind.Renamed && OriginalRelativePath is not null;
}

/// <summary>A path changed by a historical commit.</summary>
public sealed record GitCommitFileChange(
    string RelativePath,
    GitChangeKind Kind,
    string? OriginalRelativePath = null,
    string StatusCode = "");

/// <summary>Metadata for one commit and its bounded changed-path list.</summary>
public sealed record GitCommitInfo(
    string Id,
    string Message,
    string AuthorName,
    string? AuthorEmail,
    DateTimeOffset? AuthorDateUtc,
    IReadOnlyList<string> ParentIds,
    IReadOnlyList<GitCommitFileChange> ChangedFiles,
    bool ChangedFilesTruncated = false,
    bool ChangedFilesAvailable = true)
{
    /// <summary>UTC author date, when Git supplied a valid date.</summary>
    public DateTimeOffset? DateUtc => AuthorDateUtc;
}

/// <summary>Read-only result for repository status and bounded recent history.</summary>
public sealed record GitRepositorySnapshot(
    GitRepositoryState State,
    string? RepositoryRootPath,
    GitBranchInfo? Branch,
    GitCommitInfo? HeadCommit,
    IReadOnlyList<GitFileChange> Changes,
    IReadOnlyList<GitCommitInfo> RecentCommits,
    string? ErrorMessage = null,
    bool ChangesTruncated = false,
    bool HistoryTruncated = false)
{
    public bool IsRepository => State is GitRepositoryState.Clean or GitRepositoryState.Dirty;
    public bool IsDirty => State == GitRepositoryState.Dirty;
    public string? BranchName => Branch?.Name;

    public IReadOnlyList<GitFileChange> StagedChanges
        => Changes.Where(change => change.IsStaged).ToArray();

    public IReadOnlyList<GitFileChange> UnstagedChanges
        => Changes.Where(change => change.IsUnstaged).ToArray();

    public IReadOnlyList<GitFileChange> UntrackedChanges
        => Changes.Where(change => change.IsUntracked).ToArray();

    public IReadOnlyList<GitFileChange> ConflictedChanges
        => Changes.Where(change => change.IsConflicted).ToArray();
}

/// <summary>Read-only result for a history refresh.</summary>
public sealed record GitHistoryResult(
    GitRepositoryState State,
    string? RepositoryRootPath,
    IReadOnlyList<GitCommitInfo> Commits,
    string? ErrorMessage = null,
    bool IsTruncated = false)
{
    public bool IsAvailable => State is GitRepositoryState.Clean or GitRepositoryState.Dirty;
}
