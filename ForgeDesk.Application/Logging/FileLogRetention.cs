using System.Text;

namespace ForgeDesk.Application.Logging;

/// <summary>
/// Implements the bounded local-file retention policy used by the desktop logger.
/// The active file is rotated before a write would exceed its byte limit. Numbered
/// backups use the <c>.1</c>, <c>.2</c>, ... suffixes; the configured retention count
/// is the number of backups retained in addition to the active file.
/// </summary>
public static class FileLogRetention
{
    /// <summary>The default maximum size of one active log file, in bytes.</summary>
    public const long DefaultMaxFileSizeBytes = 5 * 1024 * 1024;

    /// <summary>The default number of numbered backups retained beside the active file.</summary>
    public const int DefaultRetainedFileCount = 3;

    /// <summary>
    /// Returns whether an entry of <paramref name="incomingLength"/> bytes would
    /// exceed <paramref name="maxFileSizeBytes"/> when appended to the current file.
    /// </summary>
    public static bool ShouldRotate(long currentLength, long incomingLength, long maxFileSizeBytes)
    {
        if (currentLength < 0 || incomingLength < 0 || maxFileSizeBytes <= 0)
        {
            return true;
        }

        return currentLength > maxFileSizeBytes
            || incomingLength > maxFileSizeBytes - currentLength;
    }

    /// <summary>
    /// Rotates <paramref name="path"/> into numbered backups. A failed file-system
    /// operation returns <see langword="false"/> and never reports the failure through
    /// the application logger, preventing a logging failure from recursively logging.
    /// </summary>
    public static bool TryRotate(string path, int retainedFileCount)
    {
        if (string.IsNullOrWhiteSpace(path) || retainedFileCount < 1)
        {
            return false;
        }

        try
        {
            DeleteStaleBackups(path, retainedFileCount);

            for (var generation = retainedFileCount; generation >= 1; generation--)
            {
                var source = generation == 1
                    ? path
                    : GetBackupPath(path, generation - 1);
                var destination = GetBackupPath(path, generation);
                if (File.Exists(source))
                {
                    File.Move(source, destination, overwrite: true);
                }
            }

            return true;
        }
        catch (Exception)
        {
            // Logging must remain best-effort. In particular, do not use an
            // ILogger here: reporting this failure could recurse into this policy.
            return false;
        }
    }

    /// <summary>Returns the conventional numbered-backup path for a log file.</summary>
    public static string GetBackupPath(string path, int generation)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A log path is required.", nameof(path));
        }

        if (generation < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        return path + "." + generation;
    }

    /// <summary>
    /// Encodes a formatted line without allowing one unusually large message or
    /// exception to exceed the configured file-size bound. Truncation is performed
    /// at Unicode scalar boundaries so the resulting UTF-8 remains valid.
    /// </summary>
    public static byte[] EncodeBoundedUtf8(string value, long maxBytes)
    {
        if (string.IsNullOrEmpty(value) || maxBytes <= 0)
        {
            return Array.Empty<byte>();
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (encoding.GetByteCount(value) <= maxBytes)
        {
            return encoding.GetBytes(value);
        }

        var byteLimit = Math.Min(maxBytes, int.MaxValue);
        var builder = new StringBuilder((int)Math.Min(value.Length, byteLimit));
        var length = 0L;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeLength = encoding.GetByteCount(runeText);
            if (length + runeLength > byteLimit)
            {
                break;
            }

            builder.Append(runeText);
            length += runeLength;
        }

        return encoding.GetBytes(builder.ToString());
    }

    private static void DeleteStaleBackups(string path, int retainedFileCount)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var prefix = fileName + ".";
        foreach (var candidate in Directory.EnumerateFiles(directory, prefix + "*", SearchOption.TopDirectoryOnly))
        {
            var candidateName = Path.GetFileName(candidate);
            if (!candidateName.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(candidateName[prefix.Length..], out var generation)
                || generation <= retainedFileCount)
            {
                continue;
            }

            File.Delete(candidate);
        }
    }
}
