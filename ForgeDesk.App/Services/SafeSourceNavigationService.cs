using System.IO;
using ForgeDesk.Application;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;

namespace ForgeDesk.App.Services;

/// <summary>
/// Resolves only persisted relative paths below the selected project root and
/// returns a bounded, read-only source preview. It never launches or executes a
/// source file.
/// </summary>
public sealed class SafeSourceNavigationService : ISourceNavigationService
{
    private const int MaxPreviewLines = 2_000;
    private const int ContextLines = 500;

    public async Task<SourcePreviewResult> LoadPreviewAsync(
        string projectRootPath,
        string relativePath,
        int? selectedLine = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            return SourcePreviewResult.Failure("The project root is not available for source navigation.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return SourcePreviewResult.Failure("This result does not have a source-file path.");
        }

        try
        {
            if (!SourcePathValidation.TryResolveWithinRoot(
                    projectRootPath,
                    relativePath,
                    out var rootPath,
                    out var fullPath,
                    out var normalizedRelativePath,
                    out var validationError))
            {
                return SourcePreviewResult.Failure(validationError);
            }

            if (!File.Exists(fullPath))
            {
                return SourcePreviewResult.Failure($"The source file was not found: {relativePath.Replace('\\', '/')}.");
            }

            if (!IsWithinPhysicalRoot(rootPath, fullPath))
            {
                return SourcePreviewResult.Failure("The source path resolves outside the permitted project root.");
            }

            var totalLineCount = await CountLinesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var validSelectedLine = selectedLine is > 0 && selectedLine <= totalLineCount
                ? selectedLine
                : null;
            var firstLine = 0;
            var lastLine = totalLineCount;
            var isTruncated = totalLineCount > MaxPreviewLines;
            if (isTruncated)
            {
                var center = validSelectedLine ?? 1;
                firstLine = Math.Max(0, Math.Min(center - ContextLines - 1, totalLineCount - MaxPreviewLines));
                lastLine = Math.Min(totalLineCount, firstLine + MaxPreviewLines);
            }

            var previewLines = await ReadPreviewLinesAsync(
                    fullPath,
                    firstLine,
                    lastLine,
                    validSelectedLine,
                    cancellationToken)
                .ConfigureAwait(false);

            return new SourcePreviewResult(
                true,
                normalizedRelativePath,
                fullPath,
                validSelectedLine,
                previewLines,
                totalLineCount,
                isTruncated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return SourcePreviewResult.Failure("ForgeDesk does not have permission to read this source file.");
        }
        catch (IOException)
        {
            return SourcePreviewResult.Failure("The source file could not be read because of a local I/O error.");
        }
        catch (ArgumentException)
        {
            return SourcePreviewResult.Failure("The stored source path is invalid.");
        }
        catch (Exception)
        {
            return SourcePreviewResult.Failure("The source path could not be validated or read.");
        }
    }

    private static bool IsWithinPhysicalRoot(string rootPath, string candidatePath)
    {
        var resolvedRoot = ResolveFinalPath(new DirectoryInfo(rootPath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var relativePath = Path.GetRelativePath(rootPath, candidatePath);
        var currentPath = rootPath;
        foreach (var segment in relativePath.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            var current = string.Equals(currentPath, candidatePath, comparison)
                ? (FileSystemInfo)new FileInfo(currentPath)
                : new DirectoryInfo(currentPath);
            var resolvedCurrent = ResolveFinalPath(current);
            if (!IsWithinPhysicalRoot(resolvedRoot, resolvedCurrent, comparison))
            {
                return false;
            }
        }

        return IsWithinPhysicalRoot(resolvedRoot, ResolveFinalPath(new FileInfo(candidatePath)), comparison);
    }

    private static bool IsWithinPhysicalRoot(
        string resolvedRoot,
        string resolvedCandidate,
        StringComparison comparison)
    {
        var rootWithSeparator = resolvedRoot.EndsWith(Path.DirectorySeparatorChar)
            || resolvedRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? resolvedRoot
            : resolvedRoot + Path.DirectorySeparatorChar;
        return resolvedCandidate.Equals(resolvedRoot, comparison)
            || resolvedCandidate.StartsWith(rootWithSeparator, comparison);
    }

    private static string ResolveFinalPath(FileSystemInfo fileSystemInfo)
    {
        var target = fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true);
        return Path.GetFullPath(target?.FullName ?? fileSystemInfo.FullName);
    }

    private static async Task<int> CountLinesAsync(string path, CancellationToken cancellationToken)
    {
        var count = 0;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            count++;
        }

        return count;
    }

    private static async Task<IReadOnlyList<SourcePreviewLine>> ReadPreviewLinesAsync(
        string path,
        int firstLine,
        int lastLine,
        int? selectedLine,
        CancellationToken cancellationToken)
    {
        var previewLines = new List<SourcePreviewLine>(Math.Max(0, lastLine - firstLine));
        var lineNumber = 0;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (lineNumber > firstLine && lineNumber <= lastLine)
            {
                previewLines.Add(new SourcePreviewLine(lineNumber, line, lineNumber == selectedLine));
            }

            if (lineNumber >= lastLine)
            {
                break;
            }
        }

        return previewLines;
    }

}
