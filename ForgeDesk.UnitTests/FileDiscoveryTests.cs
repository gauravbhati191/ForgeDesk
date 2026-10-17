using ForgeDesk.Application;
using ForgeDesk.Infrastructure;

namespace ForgeDesk.UnitTests;

public sealed class FileDiscoveryTests
{
    [Fact]
    public async Task Discovery_excludes_build_and_source_control_directories_and_finds_project_metadata()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "main.cs"), "class Main {}");
        File.WriteAllText(Path.Combine(directory.Path, "main.csproj"), "<Project />");
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "bin"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "obj"));
        Directory.CreateDirectory(Path.Combine(directory.Path, ".vs"));
        File.WriteAllText(Path.Combine(directory.Path, "obj", "ignored.cs"), "ignored");

        var result = await new FileDiscoveryService().DiscoverAsync(directory.Path);

        Assert.Contains(result.Files, file => file.RelativePath == "main.cs");
        Assert.Contains(result.Files, file => file.RelativePath == "main.csproj");
        Assert.DoesNotContain(result.Files, file => file.RelativePath.Contains("obj", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.Files.Count, result.Files.Select(file => file.NormalizedPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Discovery_keeps_same_file_names_when_the_relative_directories_differ()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "one"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "two"));
        File.WriteAllText(Path.Combine(directory.Path, "one", "Shared.cs"), "class One {}");
        File.WriteAllText(Path.Combine(directory.Path, "two", "Shared.cs"), "class Two {}");

        var result = await new FileDiscoveryService().DiscoverAsync(directory.Path);

        Assert.Equal(2, result.Files.Count(file => file.FileName == "Shared.cs"));
        Assert.Contains(result.Files, file => file.RelativePath == "one/Shared.cs");
        Assert.Contains(result.Files, file => file.RelativePath == "two/Shared.cs");
    }

    [Fact]
    public async Task Discovery_can_filter_extensions_and_compute_small_file_hashes()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "main.cs"), "class Main {}");
        File.WriteAllText(Path.Combine(directory.Path, "notes.txt"), "notes");

        var result = await new FileDiscoveryService().DiscoverAsync(directory.Path, new FileDiscoveryOptions
        {
            IncludedExtensions = new HashSet<string>([".cs"], StringComparer.OrdinalIgnoreCase),
            ComputeContentHash = true
        });

        var file = Assert.Single(result.Files);
        Assert.Equal("main.cs", file.RelativePath);
        Assert.False(string.IsNullOrWhiteSpace(file.ContentHash));
    }

    [Fact]
    public async Task Discovery_does_not_follow_a_symlink_outside_the_root()
    {
        using var directory = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(outside.Path, "secret.cs"), "secret");
        var link = Path.Combine(directory.Path, "linked-outside");
        try
        {
            Directory.CreateSymbolicLink(link, outside.Path);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
        {
            return;
        }

        var result = await new FileDiscoveryService().DiscoverAsync(directory.Path);
        Assert.DoesNotContain(result.Files, file => file.RelativePath.Contains("secret.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SkippedPaths, path => path.Contains("linked-outside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Discovery_honors_cancellation_during_progress_reporting()
    {
        using var directory = new TemporaryDirectory();
        for (var index = 0; index < 50; index++) File.WriteAllText(Path.Combine(directory.Path, $"File{index}.cs"), "class C {}");
        using var cancellation = new CancellationTokenSource();
        var progress = new Progress<FileDiscoveryProgress>(update =>
        {
            if (update.FilesDiscovered >= 1) cancellation.Cancel();
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() => new FileDiscoveryService().DiscoverAsync(directory.Path, progress: progress, cancellationToken: cancellation.Token));
    }
}
