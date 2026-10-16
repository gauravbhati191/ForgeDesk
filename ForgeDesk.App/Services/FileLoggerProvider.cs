using System.IO;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.App.Services;

/// <summary>Small local logger used by the desktop app when no external logging service is available.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _syncRoot = new();
    private readonly string _path;

    public FileLoggerProvider(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_syncRoot, _path, categoryName);

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly object _syncRoot;
        private readonly string _path;
        private readonly string _categoryName;

        public FileLogger(object syncRoot, string path, string categoryName)
        {
            _syncRoot = syncRoot;
            _path = path;
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {_categoryName} {message}";
            if (exception is not null) line += Environment.NewLine + exception;
            lock (_syncRoot)
            {
                try { File.AppendAllText(_path, line + Environment.NewLine); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
