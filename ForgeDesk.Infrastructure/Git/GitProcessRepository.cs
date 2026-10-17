using System.Globalization;
using ForgeDesk.Application;

namespace ForgeDesk.Infrastructure;

/// <summary>
/// Read-only Git adapter backed by the local Git executable. Commands are invoked with
/// ArgumentList and never through a shell; no remote or mutating command is issued.
/// </summary>
public sealed class GitProcessRepository : IGitRepositoryReader
{
    private const string CommitMetadataFormat = "%H%x00%P%x00%an%x00%ae%x00%aI%x00%s";
    private readonly GitCommandRunner _runner;

    public GitProcessRepository(string gitExecutable = "git")
        => _runner = new GitCommandRunner(gitExecutable);

    public async Task<GitRepositorySnapshot> ReadAsync(
        string repositoryRoot,
        GitInspectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var rootResult = await RunAsync(
            repositoryRoot,
            ["rev-parse", "--show-toplevel"],
            options.CommandTimeout,
            options.MaxCommandOutputBytes,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(rootResult);
        var actualRoot = ParseRepositoryRoot(rootResult.StandardOutput);

        var branchResult = await RunAsync(
            actualRoot,
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            options.CommandTimeout,
            options.MaxCommandOutputBytes,
            cancellationToken).ConfigureAwait(false);

        var statusResult = await RunAsync(
            actualRoot,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
            options.CommandTimeout,
            options.MaxCommandOutputBytes,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(statusResult);

        var changes = GitStatusParser.Parse(statusResult.StandardOutput, options.MaxChangedFiles, out var changesTruncated);
        var head = await ReadHeadAsync(actualRoot, options, cancellationToken).ConfigureAwait(false);
        var history = options.IncludeHistory && options.MaxRecentCommits > 0
            ? await ReadHistoryCoreAsync(
                actualRoot,
                new GitHistoryOptions
                {
                    MaxCommits = options.MaxRecentCommits,
                    MaxChangedFilesPerCommit = options.MaxChangedFilesPerCommit,
                    CommandTimeout = options.CommandTimeout,
                    MaxCommandOutputBytes = options.MaxCommandOutputBytes
                },
                head is not null,
                cancellationToken).ConfigureAwait(false)
            : new HistoryReadResult(Array.Empty<GitCommitInfo>(), false);

        var branchName = branchResult.IsSuccess
            ? FirstLine(branchResult.StandardOutput)
            : null;
        var branch = new GitBranchInfo(branchName, head is not null && string.IsNullOrWhiteSpace(branchName));
        var state = changes.Count == 0 && !changesTruncated
            ? GitRepositoryState.Clean
            : GitRepositoryState.Dirty;

        return new GitRepositorySnapshot(
            state,
            actualRoot,
            branch,
            head,
            changes,
            history.Commits,
            ErrorMessage: null,
            ChangesTruncated: changesTruncated,
            HistoryTruncated: history.IsTruncated);
    }

    public async Task<IReadOnlyList<GitCommitInfo>> ReadHistoryAsync(
        string repositoryRoot,
        GitHistoryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var rootResult = await RunAsync(
            repositoryRoot,
            ["rev-parse", "--show-toplevel"],
            options.CommandTimeout,
            options.MaxCommandOutputBytes,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(rootResult);
        var actualRoot = ParseRepositoryRoot(rootResult.StandardOutput);
        var headResult = await RunAsync(
            actualRoot,
            ["rev-parse", "--verify", "--quiet", "HEAD"],
            options.CommandTimeout,
            options.MaxCommandOutputBytes,
            cancellationToken).ConfigureAwait(false);
        if (!headResult.IsSuccess)
        {
            return Array.Empty<GitCommitInfo>();
        }

        var result = await ReadHistoryCoreAsync(actualRoot, options, hasHead: true, cancellationToken).ConfigureAwait(false);
        return result.Commits;
    }

    private async Task<GitCommitInfo?> ReadHeadAsync(
        string repositoryRoot,
        GitInspectionOptions options,
        CancellationToken cancellationToken)
    {
        var headResult = await RunAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "--quiet", "HEAD"],
            options.CommandTimeout,
            options.MaxCommandOutputBytes,
            cancellationToken).ConfigureAwait(false);
        if (!headResult.IsSuccess)
        {
            return null;
        }

        var id = ParseObjectId(FirstLine(headResult.StandardOutput));
        if (id is null)
        {
            throw new InvalidDataException("Git returned an invalid HEAD identifier.");
        }

        var metadataResult = await RunAsync(
            repositoryRoot,
            ["show", "-s", $"--format={CommitMetadataFormat}", "HEAD"],
            options.CommandTimeout,
            options.MaxCommandOutputBytes,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(metadataResult);
        return ParseCommitMetadata(metadataResult.StandardOutput, id);
    }

    private async Task<HistoryReadResult> ReadHistoryCoreAsync(
        string repositoryRoot,
        GitHistoryOptions options,
        bool hasHead,
        CancellationToken cancellationToken)
    {
        if (!hasHead || options.MaxCommits == 0)
        {
            return new HistoryReadResult(Array.Empty<GitCommitInfo>(), false);
        }

        var requested = checked(options.MaxCommits + 1);
        var idsResult = await RunAsync(
            repositoryRoot,
            ["log", $"--max-count={requested}", "--format=%H", "HEAD"],
            options.CommandTimeout,
            options.MaxCommandOutputBytes,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(idsResult);

        var ids = idsResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseObjectId)
            .ToArray();
        if (ids.Any(id => id is null))
        {
            throw new InvalidDataException("Git returned an invalid history identifier.");
        }

        var truncated = ids.Length > options.MaxCommits;
        var commits = new List<GitCommitInfo>(Math.Min(ids.Length, options.MaxCommits));
        foreach (var id in ids.Take(options.MaxCommits).OfType<string>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataResult = await RunAsync(
                repositoryRoot,
                ["show", "-s", $"--format={CommitMetadataFormat}", id],
                options.CommandTimeout,
                options.MaxCommandOutputBytes,
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(metadataResult);
            var metadata = ParseCommitMetadata(metadataResult.StandardOutput, id);

            var filesResult = await RunAsync(
                repositoryRoot,
                ["diff-tree", "--root", "--no-commit-id", "--name-status", "-r", "-M", "-C", "-z", id],
                options.CommandTimeout,
                options.MaxCommandOutputBytes,
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(filesResult);
            var files = GitHistoryParser.Parse(
                filesResult.StandardOutput,
                options.MaxChangedFilesPerCommit,
                out var filesTruncated);
            commits.Add(metadata with
            {
                ChangedFiles = files,
                ChangedFilesTruncated = filesTruncated,
                ChangedFilesAvailable = true
            });
        }

        return new HistoryReadResult(commits, truncated);
    }

    private Task<GitCommandResult> RunAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        int maxOutputBytes,
        CancellationToken cancellationToken)
        => _runner.RunAsync(repositoryRoot, arguments, timeout, maxOutputBytes, cancellationToken);

    private static void EnsureSuccess(GitCommandResult result)
    {
        if (!result.IsSuccess)
        {
            throw new GitCommandException(GitCommandFailure.CommandFailed);
        }
    }

    private static string ParseRepositoryRoot(string output)
    {
        var value = FirstLine(output);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("Git did not return a repository root.");
        }

        try
        {
            var root = Path.GetFullPath(value);
            if (!Directory.Exists(root))
            {
                throw new InvalidDataException("Git returned a repository root that does not exist.");
            }

            return Path.TrimEndingDirectorySeparator(root);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Git returned an invalid repository root.", exception);
        }
    }

    private static GitCommitInfo ParseCommitMetadata(string output, string expectedId)
    {
        var fields = output.Split('\0');
        if (fields.Length < 6)
        {
            throw new InvalidDataException("Git returned incomplete commit metadata.");
        }

        var id = ParseObjectId(fields[0].Trim()) ?? expectedId;
        var parents = fields[1]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseObjectId)
            .ToArray();
        if (parents.Any(parent => parent is null))
        {
            throw new InvalidDataException("Git returned an invalid parent identifier.");
        }

        DateTimeOffset? date = DateTimeOffset.TryParse(
            fields[4].Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
            out var parsedDate)
            ? parsedDate.ToUniversalTime()
            : null;
        var email = fields[3].Trim();
        return new GitCommitInfo(
            id,
            fields[5].TrimEnd('\r', '\n'),
            fields[2].Trim(),
            string.IsNullOrWhiteSpace(email) ? null : email,
            date,
            parents.OfType<string>().ToArray(),
            Array.Empty<GitCommitFileChange>());
    }

    private static string FirstLine(string output)
        => output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

    private static string? ParseObjectId(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length is >= 4 and <= 128
            && trimmed.All(static character => char.IsAsciiHexDigit(character))
            ? trimmed
            : null;
    }

    private sealed record HistoryReadResult(IReadOnlyList<GitCommitInfo> Commits, bool IsTruncated);
}

internal static class GitStatusParser
{
    public static IReadOnlyList<GitFileChange> Parse(string output, int maxFiles, out bool truncated)
    {
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<GitFileChange>(Math.Min(fields.Length, maxFiles));
        truncated = false;
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            if (field.Length < 4)
            {
                throw new InvalidDataException("Git returned malformed status data.");
            }

            var indexStatus = field[0];
            var workingTreeStatus = field[1];
            var path = NormalizeRelativePath(field[3..]);
            string? originalPath = null;
            var kind = KindFor(indexStatus, workingTreeStatus);
            if (kind is GitChangeKind.Renamed or GitChangeKind.Copied)
            {
                if (++index >= fields.Length)
                {
                    throw new InvalidDataException("Git returned an incomplete rename status.");
                }

                originalPath = NormalizeRelativePath(fields[index]);
            }

            if (changes.Count >= maxFiles)
            {
                truncated = true;
                continue;
            }

            changes.Add(new GitFileChange(
                path,
                kind,
                IsStaged(indexStatus),
                IsUnstaged(workingTreeStatus),
                indexStatus == '?' && workingTreeStatus == '?',
                IsConflict(indexStatus, workingTreeStatus),
                indexStatus.ToString(),
                workingTreeStatus.ToString(),
                originalPath));
        }

        changes.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return changes;
    }

    internal static GitChangeKind KindFor(char indexStatus, char workingTreeStatus)
    {
        if (IsConflict(indexStatus, workingTreeStatus))
        {
            return GitChangeKind.Conflicted;
        }

        if (indexStatus == '?' && workingTreeStatus == '?')
        {
            return GitChangeKind.Untracked;
        }

        return indexStatus is 'R' or 'r'
            ? GitChangeKind.Renamed
            : indexStatus is 'C' or 'c'
                ? GitChangeKind.Copied
                : indexStatus is 'A' or 'a' || workingTreeStatus is 'A' or 'a'
                    ? GitChangeKind.Added
                    : indexStatus is 'D' or 'd' || workingTreeStatus is 'D' or 'd'
                        ? GitChangeKind.Deleted
                        : indexStatus is 'T' or 't' || workingTreeStatus is 'T' or 't'
                            ? GitChangeKind.TypeChanged
                            : indexStatus is 'M' or 'm' || workingTreeStatus is 'M' or 'm'
                                ? GitChangeKind.Modified
                                : GitChangeKind.Unknown;
    }

    private static bool IsStaged(char status) => status is not ' ' and not '?' and not '!';
    private static bool IsUnstaged(char status) => status is not ' ' and not '?' and not '!';
    private static bool IsConflict(char indexStatus, char workingTreeStatus)
        => indexStatus is 'U' or 'u'
            || workingTreeStatus is 'U' or 'u'
            || (indexStatus == 'A' && workingTreeStatus == 'A')
            || (indexStatus == 'D' && workingTreeStatus == 'D');

    internal static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (normalized.Length == 0
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            throw new InvalidDataException("Git returned a non-relative path.");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException("Git returned a path with traversal segments.");
        }

        return string.Join('/', segments);
    }
}

internal static class GitHistoryParser
{
    public static IReadOnlyList<GitCommitFileChange> Parse(string output, int maxFiles, out bool truncated)
    {
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<GitCommitFileChange>(Math.Min(fields.Length, maxFiles));
        truncated = false;
        for (var index = 0; index < fields.Length; index++)
        {
            var statusCode = fields[index].Trim();
            if (statusCode.Length == 0)
            {
                continue;
            }

            var kind = KindFor(statusCode);
            if (++index >= fields.Length)
            {
                throw new InvalidDataException("Git returned an incomplete history path.");
            }

            var firstPath = GitStatusParser.NormalizeRelativePath(fields[index]);
            string? originalPath = null;
            var currentPath = firstPath;
            if (kind is GitChangeKind.Renamed or GitChangeKind.Copied)
            {
                if (++index >= fields.Length)
                {
                    throw new InvalidDataException("Git returned an incomplete history rename.");
                }

                currentPath = GitStatusParser.NormalizeRelativePath(fields[index]);
                originalPath = firstPath;
            }

            if (changes.Count >= maxFiles)
            {
                truncated = true;
                continue;
            }

            changes.Add(new GitCommitFileChange(currentPath, kind, originalPath, statusCode));
        }

        changes.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return changes;
    }

    private static GitChangeKind KindFor(string statusCode)
    {
        var status = statusCode[0];
        return status switch
        {
            'A' or 'a' => GitChangeKind.Added,
            'C' or 'c' => GitChangeKind.Copied,
            'D' or 'd' => GitChangeKind.Deleted,
            'M' or 'm' => GitChangeKind.Modified,
            'R' or 'r' => GitChangeKind.Renamed,
            'T' or 't' => GitChangeKind.TypeChanged,
            'U' or 'u' or 'X' or 'x' or 'B' or 'b' => GitChangeKind.Conflicted,
            _ => GitChangeKind.Unknown
        };
    }
}
