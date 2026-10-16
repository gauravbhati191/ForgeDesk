using System.Security.Cryptography;
using ForgeDesk.Application;
using ForgeDesk.Domain;

namespace ForgeDesk.Infrastructure;

/// <summary>Safely enumerates C# source and project metadata without following links outside the selected root.</summary>
public sealed class FileDiscoveryService : IFileDiscoveryService
{
    public Task<FileDiscoveryResult> DiscoverAsync(
        string rootPath,
        FileDiscoveryOptions? options = null,
        IProgress<FileDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => DiscoverCoreAsync(rootPath, options ?? FileDiscoveryOptions.SourceFilesAndProjectFiles, progress, cancellationToken);

    private static async Task<FileDiscoveryResult> DiscoverCoreAsync(
        string rootPath,
        FileDiscoveryOptions options,
        IProgress<FileDiscoveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedRoot = PathNormalization.Normalize(rootPath);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException($"The discovery root '{normalizedRoot}' does not exist.");
        }

        var physicalRoot = ResolvePhysicalPath(new DirectoryInfo(normalizedRoot)) ?? normalizedRoot;
        var files = new List<DiscoveredFile>();
        var skipped = new List<string>();
        var duplicates = new List<string>();
        var visitedDirectories = new HashSet<string>(PathComparer());
        var discoveredPaths = new HashSet<string>(PathComparer());
        var discoveredPhysicalPaths = new HashSet<string>(PathComparer());
        var directoriesVisited = 0;

        await DiscoverDirectoryAsync(
            new DirectoryInfo(normalizedRoot),
            normalizedRoot,
            physicalRoot,
            options,
            files,
            skipped,
            duplicates,
            visitedDirectories,
            discoveredPaths,
            discoveredPhysicalPaths,
            progress,
            () => ++directoriesVisited,
            cancellationToken).ConfigureAwait(false);

        files.Sort((left, right) => PathComparer().Compare(left.NormalizedPath, right.NormalizedPath));
        return new FileDiscoveryResult(files, skipped, duplicates, directoriesVisited);
    }

    private static async Task DiscoverDirectoryAsync(
        DirectoryInfo directory,
        string logicalRoot,
        string physicalRoot,
        FileDiscoveryOptions options,
        ICollection<DiscoveredFile> files,
        ICollection<string> skipped,
        ICollection<string> duplicates,
        ISet<string> visitedDirectories,
        ISet<string> discoveredPaths,
        ISet<string> discoveredPhysicalPaths,
        IProgress<FileDiscoveryProgress>? progress,
        Func<int> incrementDirectoryCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logicalDirectory = PathNormalization.Normalize(directory.FullName);
        var physicalDirectory = ResolvePhysicalPath(directory);
        if (physicalDirectory is null || !PathNormalization.IsWithin(physicalDirectory, physicalRoot))
        {
            skipped.Add(logicalDirectory);
            return;
        }

        if (!visitedDirectories.Add(physicalDirectory))
        {
            return;
        }

        var directoriesVisited = incrementDirectoryCount();
        progress?.Report(new FileDiscoveryProgress(files.Count, directoriesVisited, logicalDirectory));

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = 0
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            skipped.Add(logicalDirectory);
            return;
        }

        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var logicalPath = PathNormalization.Normalize(entry.FullName);
                bool isReparsePoint;
                try
                {
                    isReparsePoint = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(logicalPath);
                    continue;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    if (options.ExcludedDirectoryNames.Contains(entry.Name))
                    {
                        skipped.Add(logicalPath);
                        continue;
                    }

                    if (isReparsePoint && ResolvePhysicalPath(childDirectory) is null)
                    {
                        skipped.Add(logicalPath);
                        continue;
                    }

                    await DiscoverDirectoryAsync(
                        childDirectory,
                        logicalRoot,
                        physicalRoot,
                        options,
                        files,
                        skipped,
                        duplicates,
                        visitedDirectories,
                        discoveredPaths,
                        discoveredPhysicalPaths,
                        progress,
                        incrementDirectoryCount,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (entry is not FileInfo file)
                {
                    skipped.Add(logicalPath);
                    continue;
                }

                var physicalFile = isReparsePoint ? ResolvePhysicalPath(file) : logicalPath;
                if (physicalFile is null || !PathNormalization.IsWithin(physicalFile, physicalRoot))
                {
                    skipped.Add(logicalPath);
                    continue;
                }

                var extension = NormalizeExtension(file.Extension);
                if (options.IncludedExtensions is not null && !options.IncludedExtensions.Contains(extension))
                {
                    continue;
                }

                if (!discoveredPaths.Add(logicalPath) || !discoveredPhysicalPaths.Add(physicalFile))
                {
                    duplicates.Add(logicalPath);
                    continue;
                }

                try
                {
                    var length = file.Length;
                    var lastModified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
                    var relativePath = PathNormalization.NormalizeRelative(Path.GetRelativePath(logicalRoot, logicalPath));
                    var hash = options.ComputeContentHash && length <= options.MaxHashFileSizeBytes
                        ? await ComputeHashAsync(file.FullName, cancellationToken).ConfigureAwait(false)
                        : null;
                    files.Add(new DiscoveredFile(
                        logicalPath,
                        relativePath,
                        file.Name,
                        extension,
                        length,
                        lastModified,
                        LanguageFor(extension),
                        hash));
                    progress?.Report(new FileDiscoveryProgress(files.Count, directoriesVisited, logicalPath));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(logicalPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            skipped.Add(logicalDirectory);
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string? ResolvePhysicalPath(FileSystemInfo path)
    {
        try
        {
            if ((path.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                return PathNormalization.Normalize(path.FullName);
            }

            var target = path.ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? null : PathNormalization.Normalize(target.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim().ToLowerInvariant();
        return value.StartsWith(".", StringComparison.Ordinal) ? value : "." + value;
    }

    private static string LanguageFor(string extension) => extension switch
    {
        ".cs" => "C#",
        ".csproj" => "MSBuild",
        ".sln" or ".slnx" => "Solution",
        _ => "Unknown"
    };

    private static StringComparer PathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
