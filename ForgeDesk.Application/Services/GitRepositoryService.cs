namespace ForgeDesk.Application;

/// <summary>Locates the nearest local repository and delegates safe reads to infrastructure.</summary>
public sealed class GitRepositoryService : IGitRepositoryService
{
    private readonly IGitRepositoryReader _reader;

    public GitRepositoryService(IGitRepositoryReader reader)
        => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public async Task<GitRepositorySnapshot> InspectAsync(
        string projectRoot,
        GitInspectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inspectionOptions = options ?? new GitInspectionOptions();
        inspectionOptions.Validate();

        var location = FindNearestRepository(projectRoot);
        if (location.State != GitRepositoryState.Clean || location.RootPath is null)
        {
            return location.ToSnapshot();
        }

        try
        {
            var snapshot = await _reader
                .ReadAsync(location.RootPath, inspectionOptions, cancellationToken)
                .ConfigureAwait(false);
            return snapshot.RepositoryRootPath is null
                ? snapshot with { RepositoryRootPath = location.RootPath }
                : snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return GitRepositorySnapshotUnreadable(location.RootPath, ErrorFor(exception));
        }
    }

    public async Task<GitHistoryResult> GetHistoryAsync(
        string projectRoot,
        GitHistoryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var historyOptions = options ?? new GitHistoryOptions();
        historyOptions.Validate();

        var location = FindNearestRepository(projectRoot);
        if (location.State == GitRepositoryState.NotRepository || location.RootPath is null)
        {
            return new GitHistoryResult(location.State, location.RootPath, Array.Empty<GitCommitInfo>(), location.ErrorMessage);
        }

        if (location.State == GitRepositoryState.Unreadable)
        {
            return new GitHistoryResult(GitRepositoryState.Unreadable, location.RootPath, Array.Empty<GitCommitInfo>(), location.ErrorMessage);
        }

        try
        {
            // Reuse the same read boundary as a full inspection so the result carries the
            // actual clean/dirty state and the adapter can report whether the bounded list
            // was truncated by an additional probe commit.
            var snapshot = await _reader
                .ReadAsync(
                    location.RootPath,
                    new GitInspectionOptions
                    {
                        MaxChangedFiles = 1,
                        MaxRecentCommits = historyOptions.MaxCommits,
                        MaxChangedFilesPerCommit = historyOptions.MaxChangedFilesPerCommit,
                        CommandTimeout = historyOptions.CommandTimeout,
                        MaxCommandOutputBytes = historyOptions.MaxCommandOutputBytes,
                        IncludeHistory = true
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return new GitHistoryResult(
                snapshot.State,
                snapshot.RepositoryRootPath ?? location.RootPath,
                snapshot.RecentCommits,
                snapshot.ErrorMessage,
                snapshot.HistoryTruncated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new GitHistoryResult(
                GitRepositoryState.Unreadable,
                location.RootPath,
                Array.Empty<GitCommitInfo>(),
                ErrorFor(exception));
        }
    }

    private static RepositoryLocation FindNearestRepository(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return RepositoryLocation.NotRepository("A project root is required.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(projectRoot.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return RepositoryLocation.Unreadable(null, "The project root could not be read.");
        }

        try
        {
            var rootAttributes = File.GetAttributes(fullPath);
            if ((rootAttributes & FileAttributes.Directory) == 0)
            {
                return RepositoryLocation.NotRepository();
            }
        }
        catch (FileNotFoundException)
        {
            return RepositoryLocation.NotRepository();
        }
        catch (DirectoryNotFoundException)
        {
            return RepositoryLocation.NotRepository();
        }
        catch (UnauthorizedAccessException)
        {
            return RepositoryLocation.Unreadable(fullPath, "The project root could not be accessed.");
        }
        catch (IOException)
        {
            return RepositoryLocation.Unreadable(fullPath, "The project root could not be read.");
        }

        var current = Path.TrimEndingDirectorySeparator(fullPath);
        while (!string.IsNullOrEmpty(current))
        {
            var marker = Path.Combine(current, ".git");
            try
            {
                _ = File.GetAttributes(marker);
                return RepositoryLocation.Found(current);
            }
            catch (FileNotFoundException)
            {
                // Continue to the parent.
            }
            catch (DirectoryNotFoundException)
            {
                // Continue to the parent.
            }
            catch (UnauthorizedAccessException)
            {
                return RepositoryLocation.Unreadable(current, "Git metadata could not be accessed.");
            }
            catch (IOException)
            {
                return RepositoryLocation.Unreadable(current, "Git metadata could not be read.");
            }

            string? parent;
            try
            {
                parent = Directory.GetParent(current)?.FullName;
            }
            catch (UnauthorizedAccessException)
            {
                return RepositoryLocation.Unreadable(current, "A parent directory could not be accessed.");
            }
            catch (IOException)
            {
                return RepositoryLocation.Unreadable(current, "A parent directory could not be read.");
            }

            if (parent is null || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return RepositoryLocation.NotRepository();
    }

    private static GitRepositorySnapshot GitRepositorySnapshotUnreadable(string? rootPath, string errorMessage)
        => new(
            GitRepositoryState.Unreadable,
            rootPath,
            Branch: null,
            HeadCommit: null,
            Changes: Array.Empty<GitFileChange>(),
            RecentCommits: Array.Empty<GitCommitInfo>(),
            ErrorMessage: errorMessage);

    private static string ErrorFor(Exception exception)
        => exception switch
        {
            FileNotFoundException => "Git executable or repository metadata was not found.",
            TimeoutException => "Git did not finish before the configured read timeout.",
            _ => "Git repository metadata could not be read."
        };

    private sealed record RepositoryLocation(
        GitRepositoryState State,
        string? RootPath,
        string? ErrorMessage)
    {
        public static RepositoryLocation Found(string rootPath)
            => new(GitRepositoryState.Clean, rootPath, null);

        public static RepositoryLocation NotRepository(string? errorMessage = null)
            => new(GitRepositoryState.NotRepository, null, errorMessage);

        public static RepositoryLocation Unreadable(string? rootPath, string errorMessage)
            => new(GitRepositoryState.Unreadable, rootPath, errorMessage);

        public GitRepositorySnapshot ToSnapshot()
            => new(
                State,
                RootPath,
                Branch: null,
                HeadCommit: null,
                Changes: Array.Empty<GitFileChange>(),
                RecentCommits: Array.Empty<GitCommitInfo>(),
                ErrorMessage: ErrorMessage);
    }
}
