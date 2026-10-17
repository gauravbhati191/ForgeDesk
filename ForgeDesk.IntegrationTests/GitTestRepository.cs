using System.Diagnostics;
using System.Text;

namespace ForgeDesk.IntegrationTests;

internal sealed class GitTestRepository : IDisposable
{
    public GitTestRepository()
    {
        Root = Directory.CreateTempSubdirectory("forgedesk-git-").FullName;
        Run("init", "-q", "-b", "main");
        Run("config", "user.name", "ForgeDesk Test Author");
        Run("config", "user.email", "forgedesk-tests@example.test");
    }

    public string Root { get; }

    public void Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(path);
        if (parent is not null)
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, content);
    }

    public void Delete(string relativePath)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(path);
    }

    public string Run(params string[] arguments)
    {
        var result = RunAllowFailure(arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git test command failed with exit code {result.ExitCode}.");
        }

        return result.StandardOutput;
    }

    public GitTestCommandResult RunAllowFailure(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git could not be started for the integration test.");
        var stdout = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitTestCommandResult(process.ExitCode, stdout);
    }

    public void Commit(string message)
    {
        Run("add", "--all");
        Run("commit", "-q", "-m", message);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

internal sealed record GitTestCommandResult(int ExitCode, string StandardOutput);
