namespace ForgeDesk.Domain;

/// <summary>Path helpers used to establish a stable project identity.</summary>
public static class PathNormalization
{
    /// <summary>Returns a full lexical path without resolving symbolic links.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DomainInvariantException("A path is required.");
        }

        var fullPath = Path.GetFullPath(path.Trim());
        fullPath = Path.TrimEndingDirectorySeparator(fullPath);
        return OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }

    /// <summary>Returns true when the candidate is the root or a descendant of it.</summary>
    public static bool IsWithin(string candidatePath, string rootPath)
    {
        var candidate = Normalize(candidatePath);
        var root = Normalize(rootPath);
        var relative = Path.GetRelativePath(root, candidate);
        return relative.Equals(".", StringComparison.Ordinal)
            || (!Path.IsPathRooted(relative)
                && !relative.Equals("..", StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    /// <summary>Normalizes a relative path and rejects traversal.</summary>
    public static string NormalizeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DomainInvariantException("A relative path is required.");
        }

        var normalized = path.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new DomainInvariantException("A source-file path must be relative.");
        }

        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new DomainInvariantException("A source-file path cannot contain traversal segments.");
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    internal static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
