namespace ForgeDesk.Application;

/// <summary>
/// Performs platform-independent lexical validation for a persisted source
/// path. Physical link/reparse-point validation remains in the desktop adapter.
/// </summary>
public static class SourcePathValidation
{
    public static bool TryResolveWithinRoot(
        string projectRootPath,
        string relativePath,
        out string normalizedRootPath,
        out string fullPath,
        out string normalizedRelativePath,
        out string errorMessage)
    {
        normalizedRootPath = string.Empty;
        fullPath = string.Empty;
        normalizedRelativePath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            errorMessage = "The project root is not available for source navigation.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            errorMessage = "This result does not have a source-file path.";
            return false;
        }

        try
        {
            normalizedRootPath = Path.GetFullPath(projectRootPath.Trim());
            if (!Directory.Exists(normalizedRootPath))
            {
                errorMessage = "The project root no longer exists.";
                return false;
            }

            if (Path.IsPathRooted(relativePath) || LooksLikeWindowsRootedPath(relativePath))
            {
                errorMessage = "The stored source path must be relative to the project root.";
                return false;
            }

            var platformRelativePath = relativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            fullPath = Path.GetFullPath(Path.Combine(normalizedRootPath, platformRelativePath));
            if (!IsWithinRoot(normalizedRootPath, fullPath))
            {
                errorMessage = "The stored source path escapes the permitted project root.";
                return false;
            }

            normalizedRelativePath = NormalizeRelativePath(Path.GetRelativePath(normalizedRootPath, fullPath));
            return true;
        }
        catch (ArgumentException)
        {
            errorMessage = "The stored source path is invalid.";
            return false;
        }
        catch (IOException)
        {
            errorMessage = "The source path could not be resolved because of a local I/O error.";
            return false;
        }
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var root = EnsureTrailingSeparator(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.Equals(rootPath, PathComparison())
            || candidate.StartsWith(root, PathComparison());
    }

    private static bool LooksLikeWindowsRootedPath(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal)
            || (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':');

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
