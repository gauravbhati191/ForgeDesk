using System.Collections.ObjectModel;
using System.Globalization;
using ForgeDesk.Application;

namespace ForgeDesk.Presentation.Models;

public enum GitChangeGroupKind
{
    Conflicted,
    Staged,
    Unstaged,
    Untracked,
    Other
}

/// <summary>Display projection of repository metadata returned by the application Git service.</summary>
public sealed class GitRepositoryInfoItem
{
    public GitRepositoryInfoItem(GitRepositorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        RepositoryRootPath = snapshot.RepositoryRootPath;
        State = snapshot.State;
        BranchName = snapshot.Branch?.Name;
        IsDetached = snapshot.Branch?.IsDetached == true;
        HeadId = snapshot.HeadCommit?.Id;
        HeadMessage = snapshot.HeadCommit?.Message;
        HeadAuthorName = snapshot.HeadCommit?.AuthorName;
        HeadAuthorEmail = snapshot.HeadCommit?.AuthorEmail;
        HeadAuthorDateUtc = snapshot.HeadCommit?.AuthorDateUtc;
    }

    public string? RepositoryRootPath { get; }

    public GitRepositoryState State { get; }

    public string? BranchName { get; }

    public bool IsDetached { get; }

    public string? HeadId { get; }

    public string? HeadMessage { get; }

    public string? HeadAuthorName { get; }

    public string? HeadAuthorEmail { get; }

    public DateTimeOffset? HeadAuthorDateUtc { get; }

    public bool HasHead => !string.IsNullOrWhiteSpace(HeadId);

    public string RepositoryRootLabel => string.IsNullOrWhiteSpace(RepositoryRootPath)
        ? "Repository root unavailable"
        : RepositoryRootPath;

    public string BranchLabel => IsDetached
        ? "Detached HEAD"
        : string.IsNullOrWhiteSpace(BranchName)
            ? "No branch"
            : BranchName;

    public string HeadIdLabel => string.IsNullOrWhiteSpace(HeadId)
        ? "No commits yet"
        : HeadId;

    public string HeadMessageLabel => string.IsNullOrWhiteSpace(HeadMessage)
        ? "No HEAD commit is available"
        : HeadMessage;

    public string HeadAuthorLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(HeadAuthorName))
            {
                return "Author unavailable";
            }

            return string.IsNullOrWhiteSpace(HeadAuthorEmail)
                ? HeadAuthorName
                : $"{HeadAuthorName} <{HeadAuthorEmail}>";
        }
    }

    public string HeadDateLabel => HeadAuthorDateUtc is DateTimeOffset date
        ? date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
        : "Date unavailable";

    public string StatusLabel => State switch
    {
        GitRepositoryState.Clean => "Clean",
        GitRepositoryState.Dirty => "Dirty",
        GitRepositoryState.Unreadable => "Unreadable",
        _ => "Not a repository"
    };
}

/// <summary>Display projection of one working-tree path.</summary>
public sealed class GitChangeItem
{
    public GitChangeItem(GitFileChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        RelativePath = NormalizePath(change.RelativePath);
        OriginalRelativePath = string.IsNullOrWhiteSpace(change.OriginalRelativePath)
            ? null
            : NormalizePath(change.OriginalRelativePath);
        Kind = change.Kind;
        IsStaged = change.IsStaged;
        IsUnstaged = change.IsUnstaged;
        IsUntracked = change.IsUntracked;
        IsConflicted = change.IsConflicted;
        StatusCode = change.StatusCode;
    }

    public string RelativePath { get; }

    public string? OriginalRelativePath { get; }

    public bool HasOriginalPath => !string.IsNullOrWhiteSpace(OriginalRelativePath);

    public GitChangeKind Kind { get; }

    public bool IsStaged { get; }

    public bool IsUnstaged { get; }

    public bool IsUntracked { get; }

    public bool IsConflicted { get; }

    public string StatusCode { get; }

    public string KindLabel => Kind switch
    {
        GitChangeKind.Added => "Added",
        GitChangeKind.Modified => "Modified",
        GitChangeKind.Deleted => "Deleted",
        GitChangeKind.Renamed => "Renamed",
        GitChangeKind.Copied => "Copied",
        GitChangeKind.TypeChanged => "Type changed",
        GitChangeKind.Untracked => "Untracked",
        GitChangeKind.Conflicted => "Conflicted",
        _ => "Other"
    };

    public string StagingLabel => IsConflicted
        ? "Conflict requires attention"
        : IsStaged && IsUnstaged
            ? "Staged and unstaged"
            : IsStaged
                ? "Staged"
                : IsUntracked
                    ? "Untracked"
                    : IsUnstaged
                        ? "Unstaged"
                        : "Working tree status";

    public string StatusCodeLabel => string.IsNullOrWhiteSpace(StatusCode)
        ? "Status code unavailable"
        : StatusCode;

    public string OriginalPathLabel => OriginalRelativePath is null
        ? string.Empty
        : $"from {OriginalRelativePath}";

    public string AccessibilityName => string.IsNullOrWhiteSpace(OriginalPathLabel)
        ? $"{KindLabel} {RelativePath}, {StagingLabel}"
        : $"{KindLabel} {RelativePath}, {OriginalPathLabel}, {StagingLabel}";

    public string Tooltip => string.IsNullOrWhiteSpace(OriginalPathLabel)
        ? $"{RelativePath} · {KindLabel} · {StatusCodeLabel}"
        : $"{RelativePath} · {KindLabel} · {OriginalPathLabel} · {StatusCodeLabel}";

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');
}

/// <summary>One non-empty working-tree change group used by the Git page.</summary>
public sealed class GitChangeGroupItem
{
    public GitChangeGroupItem(GitChangeGroupKind kind, IEnumerable<GitChangeItem> changes)
    {
        Kind = kind;
        Changes = new ObservableCollection<GitChangeItem>(changes ?? throw new ArgumentNullException(nameof(changes)));
    }

    public GitChangeGroupKind Kind { get; }

    public ObservableCollection<GitChangeItem> Changes { get; }

    public string Title => Kind switch
    {
        GitChangeGroupKind.Conflicted => "Conflicted",
        GitChangeGroupKind.Staged => "Staged",
        GitChangeGroupKind.Unstaged => "Unstaged",
        GitChangeGroupKind.Untracked => "Untracked",
        _ => "Other changes"
    };

    public string CountLabel => $"{Changes.Count:N0} {(Changes.Count == 1 ? "file" : "files")}";

    public string AccessibilityName => $"{Title}, {CountLabel}";
}

/// <summary>Display projection of one file changed by a historical commit.</summary>
public sealed class GitCommitFileItem
{
    public GitCommitFileItem(GitCommitFileChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        RelativePath = NormalizePath(change.RelativePath);
        OriginalRelativePath = string.IsNullOrWhiteSpace(change.OriginalRelativePath)
            ? null
            : NormalizePath(change.OriginalRelativePath);
        Kind = change.Kind;
        StatusCode = change.StatusCode;
    }

    public string RelativePath { get; }

    public string? OriginalRelativePath { get; }

    public bool HasOriginalPath => !string.IsNullOrWhiteSpace(OriginalRelativePath);

    public GitChangeKind Kind { get; }

    public string StatusCode { get; }

    public string KindLabel => Kind switch
    {
        GitChangeKind.Added => "Added",
        GitChangeKind.Modified => "Modified",
        GitChangeKind.Deleted => "Deleted",
        GitChangeKind.Renamed => "Renamed",
        GitChangeKind.Copied => "Copied",
        GitChangeKind.TypeChanged => "Type changed",
        GitChangeKind.Untracked => "Untracked",
        GitChangeKind.Conflicted => "Conflicted",
        _ => "Other"
    };

    public string StatusCodeLabel => string.IsNullOrWhiteSpace(StatusCode)
        ? "Status code unavailable"
        : StatusCode;

    public string OriginalPathLabel => OriginalRelativePath is null
        ? string.Empty
        : $"from {OriginalRelativePath}";

    public string AccessibilityName => string.IsNullOrWhiteSpace(OriginalPathLabel)
        ? $"{KindLabel} {RelativePath}"
        : $"{KindLabel} {RelativePath}, {OriginalPathLabel}";

    public string Tooltip => string.IsNullOrWhiteSpace(OriginalPathLabel)
        ? $"{RelativePath} · {KindLabel} · {StatusCodeLabel}"
        : $"{RelativePath} · {KindLabel} · {OriginalPathLabel} · {StatusCodeLabel}";

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');
}

/// <summary>Display projection of a bounded recent commit.</summary>
public sealed class GitCommitItem
{
    public GitCommitItem(GitCommitInfo commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        Id = commit.Id;
        Message = commit.Message;
        AuthorName = commit.AuthorName;
        AuthorEmail = commit.AuthorEmail;
        AuthorDateUtc = commit.AuthorDateUtc;
        ParentIds = commit.ParentIds ?? Array.Empty<string>();
        ChangedFiles = new ObservableCollection<GitCommitFileItem>(
            (commit.ChangedFiles ?? Array.Empty<GitCommitFileChange>()).Select(change => new GitCommitFileItem(change)));
        ChangedFilesTruncated = commit.ChangedFilesTruncated;
        ChangedFilesAvailable = commit.ChangedFilesAvailable;
    }

    public string Id { get; }

    public string Message { get; }

    public string AuthorName { get; }

    public string? AuthorEmail { get; }

    public DateTimeOffset? AuthorDateUtc { get; }

    public IReadOnlyList<string> ParentIds { get; }

    public ObservableCollection<GitCommitFileItem> ChangedFiles { get; }

    public bool ChangedFilesTruncated { get; }

    public bool ChangedFilesAvailable { get; }

    public bool HasChangedFiles => ChangedFilesAvailable && ChangedFiles.Count > 0;

    public string IdLabel => string.IsNullOrWhiteSpace(Id)
        ? "Commit ID unavailable"
        : Id.Length <= 12
            ? Id
            : Id[..12];

    public string IdTooltip => string.IsNullOrWhiteSpace(Id) ? "Commit ID unavailable" : Id;

    public string MessageLabel => string.IsNullOrWhiteSpace(Message)
        ? "Commit message unavailable"
        : Message;

    public string AuthorLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AuthorName))
            {
                return "Author unavailable";
            }

            return string.IsNullOrWhiteSpace(AuthorEmail)
                ? AuthorName
                : $"{AuthorName} <{AuthorEmail}>";
        }
    }

    public string DateLabel => AuthorDateUtc is DateTimeOffset date
        ? date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
        : "Date unavailable";

    public string ParentLabel => ParentIds.Count switch
    {
        0 => "No parents",
        1 => $"Parent {ParentIds[0]}",
        _ => $"{ParentIds.Count:N0} parents"
    };

    public string ChangedFilesLabel
    {
        get
        {
            if (!ChangedFilesAvailable)
            {
                return "Changed files unavailable";
            }

            var fileLabel = ChangedFiles.Count == 1 ? "file" : "files";
            var label = $"{ChangedFiles.Count:N0} {fileLabel}";
            return ChangedFilesTruncated ? $"{label} shown (bounded)" : label;
        }
    }

    public string AccessibilityName => $"Commit {IdLabel}: {MessageLabel}, {AuthorLabel}, {DateLabel}";

}
