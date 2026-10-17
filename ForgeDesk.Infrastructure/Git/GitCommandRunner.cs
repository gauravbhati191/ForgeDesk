using System.Diagnostics;
using System.Text;

namespace ForgeDesk.Infrastructure;

internal sealed class GitCommandRunner
{
    private readonly string _gitExecutable;

    public GitCommandRunner(string gitExecutable = "git")
    {
        if (string.IsNullOrWhiteSpace(gitExecutable))
        {
            throw new ArgumentException("A Git executable is required.", nameof(gitExecutable));
        }

        _gitExecutable = gitExecutable.Trim();
    }

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        int maxOutputBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // All arguments are supplied through ArgumentList. No shell or string command line is used.
        startInfo.ArgumentList.Add("--no-pager");
        startInfo.ArgumentList.Add("--no-optional-locks");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["LC_ALL"] = "C";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new GitCommandException(GitCommandFailure.ExecutableUnavailable);
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new GitCommandException(GitCommandFailure.ExecutableUnavailable, exception);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var budget = new OutputBudget(maxOutputBytes);
        var stdoutTask = ReadOutputAsync(process.StandardOutput.BaseStream, budget, timeoutSource.Token);
        var stderrTask = ReadOutputAsync(process.StandardError.BaseStream, budget, timeoutSource.Token);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(timeoutSource.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return new GitCommandResult(
                process.ExitCode,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false));
        }
        catch (GitCommandException)
        {
            Kill(process);
            await DrainAsync(stdoutTask).ConfigureAwait(false);
            await DrainAsync(stderrTask).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            await DrainAsync(stdoutTask).ConfigureAwait(false);
            await DrainAsync(stderrTask).ConfigureAwait(false);
            throw new GitCommandException(GitCommandFailure.Timeout);
        }
        catch
        {
            Kill(process);
            await DrainAsync(stdoutTask).ConfigureAwait(false);
            await DrainAsync(stderrTask).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<string> ReadOutputAsync(
        Stream stream,
        OutputBudget budget,
        CancellationToken cancellationToken)
    {
        var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            budget.Consume(read);
            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
    }

    private static async Task DrainAsync(Task<string> task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The process is being terminated; its redirected streams are no longer useful.
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited between HasExited and Kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process cannot be killed on this platform; disposal still closes its handles.
        }
    }

    private sealed class OutputBudget
    {
        private int _remaining;

        public OutputBudget(int maximumBytes)
        {
            if (maximumBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }

            _remaining = maximumBytes;
        }

        public void Consume(int bytes)
        {
            var remaining = Interlocked.Add(ref _remaining, -bytes);
            if (remaining < 0)
            {
                throw new GitCommandException(GitCommandFailure.OutputLimitExceeded);
            }
        }
    }
}

internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}

internal enum GitCommandFailure
{
    ExecutableUnavailable,
    Timeout,
    OutputLimitExceeded,
    CommandFailed
}

internal sealed class GitCommandException : IOException
{
    public GitCommandException(GitCommandFailure failure, Exception? innerException = null)
        : base(CreateMessage(failure), innerException)
        => Failure = failure;

    public GitCommandFailure Failure { get; }

    private static string CreateMessage(GitCommandFailure failure)
        => failure switch
        {
            GitCommandFailure.ExecutableUnavailable => "The Git executable is unavailable.",
            GitCommandFailure.Timeout => "The Git command exceeded its timeout.",
            GitCommandFailure.OutputLimitExceeded => "The Git command exceeded its output limit.",
            _ => "The Git command failed."
        };
}
