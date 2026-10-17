using ForgeDesk.Application;

namespace ForgeDesk.UnitTests;

public sealed class Phase3SourcePathValidationTests
{
    [Fact]
    public void Source_path_validation_normalizes_slashes_inside_the_project_root()
    {
        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "src"));

        var resolved = SourcePathValidation.TryResolveWithinRoot(
            directory.Path,
            "src\\Main.cs",
            out _,
            out var fullPath,
            out var relativePath,
            out var error);

        Assert.True(resolved, error);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory.Path, "src", "Main.cs")), fullPath);
        Assert.Equal("src/Main.cs", relativePath);
    }

    [Fact]
    public void Source_path_validation_rejects_traversal_and_rooted_windows_paths()
    {
        using var directory = TemporaryDirectory.Create();

        var traversal = SourcePathValidation.TryResolveWithinRoot(
            directory.Path,
            "../outside.cs",
            out _,
            out _,
            out _,
            out var traversalError);
        var rooted = SourcePathValidation.TryResolveWithinRoot(
            directory.Path,
            "C:\\outside.cs",
            out _,
            out _,
            out _,
            out var rootedError);

        Assert.False(traversal);
        Assert.Contains("escapes", traversalError, StringComparison.OrdinalIgnoreCase);
        Assert.False(rooted);
        Assert.Contains("relative", rootedError, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
            => new(Directory.CreateTempSubdirectory("forgedesk-phase3-").FullName);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
