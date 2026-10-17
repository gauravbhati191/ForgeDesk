using System.IO;
using ForgeDesk.Application.Logging;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.App.Services;

/// <summary>
/// Small local logger used by the desktop app when no external logging service is available.
///
/// The active file is capped at its configured byte limit. Before a write
/// would exceed that cap, the active file is moved to <c>.1</c>, existing numbered
/// backups are shifted upward, and backups older than the configured retention count
/// are removed. This keeps at most one active file plus the configured number of
/// numbered backups. File-system failures are intentionally silent: diagnostics must
/// never prevent ForgeDesk from starting or recursively fail while reporting an error.
/// The provider remains local-only and does not inspect project files; callers should
/// pass diagnostic metadata rather than credentials or complete source contents.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _syncRoot = new();
    private readonly string _path;
    private readonly long _maxFileSizeBytes;
    private readonly int _retainedFileCount;

    public FileLoggerProvider(string path)
        : this(path, FileLogRetention.DefaultMaxFileSizeBytes, FileLogRetention.DefaultRetainedFileCount)
    {
    }

    public FileLoggerProvider(string path, long maxFileSizeBytes, int retainedFileCount)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        if (maxFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileSizeBytes));
        }

        if (retainedFileCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedFileCount));
        }

        _maxFileSizeBytes = maxFileSizeBytes;
        _retainedFileCount = retainedFileCount;
        TryCreateDirectory(_path);
    }

    public ILogger CreateLogger(string categoryName)
        => new FileLogger(_syncRoot, _path, _maxFileSizeBytes, _retainedFileCount, categoryName);

    public void Dispose()
    {
    }

    private static void TryCreateDirectory(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch (Exception)
        {
            // The subsequent open will fail harmlessly if the directory is
            // unavailable. There is no safe logging path for this failure.
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly object _syncRoot;
        private readonly string _path;
        private readonly long _maxFileSizeBytes;
        private readonly int _retainedFileCount;
        private readonly string _categoryName;

        [ThreadStatic]
        private static bool s_isLogging;

        public FileLogger(
            object syncRoot,
            string path,
            long maxFileSizeBytes,
            int retainedFileCount,
            string categoryName)
        {
            _syncRoot = syncRoot;
            _path = path;
            _maxFileSizeBytes = maxFileSizeBytes;
            _retainedFileCount = retainedFileCount;
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || s_isLogging)
            {
                return;
            }

            s_isLogging = true;
            try
            {
                string message;
                try
                {
                    message = formatter(state, exception) ?? string.Empty;
                }
                catch (Exception)
                {
                    // A formatter is application code. It must not be able to
                    // turn a diagnostic failure into an application failure.
                    return;
                }

                var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {_categoryName} {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                var entry = FileLogRetention.EncodeBoundedUtf8(
                    line + Environment.NewLine,
                    _maxFileSizeBytes);
                if (entry.Length == 0)
                {
                    return;
                }

                lock (_syncRoot)
                {
                    TryAppend(entry);
                }
            }
            catch (Exception)
            {
                // Logging is best-effort. Do not call another logger here: that
                // would make a permission or rotation failure recursively fail.
            }
            finally
            {
                s_isLogging = false;
            }
        }

        private void TryAppend(byte[] entry)
        {
            try
            {
                FileLoggerProvider.TryCreateDirectory(_path);
                using (var stream = new FileStream(
                           _path,
                           FileMode.Append,
                           FileAccess.Write,
                           FileShare.Read,
                           bufferSize: 4 * 1024,
                           options: FileOptions.SequentialScan))
                {
                    if (!FileLogRetention.ShouldRotate(stream.Length, entry.LongLength, _maxFileSizeBytes))
                    {
                        stream.Write(entry);
                        return;
                    }
                }

                if (!FileLogRetention.TryRotate(_path, _retainedFileCount))
                {
                    return;
                }

                using var rotatedStream = new FileStream(
                    _path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4 * 1024,
                    options: FileOptions.SequentialScan);
                if (FileLogRetention.ShouldRotate(
                        rotatedStream.Length,
                        entry.LongLength,
                        _maxFileSizeBytes))
                {
                    return;
                }

                rotatedStream.Write(entry);
            }
            catch (Exception)
            {
                // Directory, file, sharing, and rotation permission failures are
                // intentionally swallowed so the application remains usable.
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
