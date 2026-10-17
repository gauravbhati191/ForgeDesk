using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ForgeDesk.Infrastructure;

/// <summary>
/// Uses SQLite's online backup API instead of copying the live database file.
/// </summary>
public sealed class SqliteDatabaseBackupService : IDatabaseBackupService
{
    private const string BackupDirectoryName = "backups";
    private readonly IDatabasePathProvider _pathProvider;

    public SqliteDatabaseBackupService(IDatabasePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<DatabaseBackupResult> CreateBackupAsync(
        string? backupDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = GetDatabasePath();
        EnsureDatabaseFileExists(sourcePath, "The ForgeDesk database to back up was not found.");

        var destinationDirectory = ResolveBackupDirectory(sourcePath, backupDirectory);
        (string StagingPath, string DestinationPath) reservation;
        try
        {
            reservation = ReserveUniqueBackupPath(destinationDirectory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new DatabaseBackupException(
                $"Could not reserve a new SQLite backup in '{destinationDirectory}'. Check that the directory exists and is writable.",
                exception);
        }

        try
        {
            await BackupDatabaseAsync(sourcePath, reservation.StagingPath, cancellationToken).ConfigureAwait(false);
            File.Move(reservation.StagingPath, reservation.DestinationPath, overwrite: false);

            var destinationPath = reservation.DestinationPath;
            var backupInfo = new FileInfo(destinationPath);
            if (!backupInfo.Exists || backupInfo.Length == 0)
            {
                throw new DatabaseBackupException(
                    $"SQLite reported a successful backup, but the backup file '{destinationPath}' is empty.");
            }

            return new DatabaseBackupResult(destinationPath, backupInfo.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DatabaseBackupException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DatabaseBackupException(
                $"Could not create a consistent SQLite backup at '{reservation.DestinationPath}'. Ensure no ForgeDesk operation is writing the database and that the backup directory is writable. The staging file '{reservation.StagingPath}' was not removed; inspect or remove it manually after resolving the failure.",
                exception);
        }
    }

    public async Task RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = NormalizePath(backupPath, nameof(backupPath));
        var destinationPath = GetDatabasePath();

        if (PathsReferToSameFile(sourcePath, destinationPath))
        {
            throw new ArgumentException(
                "The backup path must be different from the active ForgeDesk database path.",
                nameof(backupPath));
        }

        EnsureDatabaseFileExists(sourcePath, "The SQLite backup to restore was not found.");

        try
        {
            _pathProvider.EnsureDirectoryExists();
            await RestoreDatabaseAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DatabaseRestoreException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DatabaseRestoreException(
                $"Could not restore the ForgeDesk database from '{sourcePath}'. Ensure no ForgeDesk operation is writing the database and that the destination directory is writable.",
                exception);
        }
    }

    private static async Task BackupDatabaseAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(BuildConnectionString(sourcePath, SqliteOpenMode.ReadOnly));
        await using var destination = new SqliteConnection(BuildConnectionString(destinationPath, SqliteOpenMode.ReadWriteCreate));
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // BackupDatabase uses SQLite's online backup API, which takes a consistent
        // snapshot while allowing SQLite to coordinate with concurrent writers.
        source.BackupDatabase(destination);
        cancellationToken.ThrowIfCancellationRequested();
        await VerifyIntegrityAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RestoreDatabaseAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(BuildConnectionString(sourcePath, SqliteOpenMode.ReadOnly));
        await using var destination = new SqliteConnection(BuildConnectionString(destinationPath, SqliteOpenMode.ReadWriteCreate));
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await VerifyIntegrityAsync(source, cancellationToken).ConfigureAwait(false);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Restoring through SQLite keeps the operation transactional at the
        // database level and avoids replacing a live database file by copying it.
        source.BackupDatabase(destination);
        cancellationToken.ThrowIfCancellationRequested();
        await VerifyIntegrityAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyIntegrityAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(Convert.ToString(result, CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("SQLite integrity verification did not return 'ok'.");
        }
    }

    private string GetDatabasePath() => NormalizePath(_pathProvider.DatabasePath, nameof(IDatabasePathProvider.DatabasePath));

    private static string ResolveBackupDirectory(string sourcePath, string? requestedDirectory)
    {
        if (!string.IsNullOrWhiteSpace(requestedDirectory))
        {
            return NormalizePath(requestedDirectory, nameof(requestedDirectory));
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new DatabaseBackupException(
                $"The database path '{sourcePath}' does not have a directory in which to store a backup.");
        }

        return Path.Combine(sourceDirectory, BackupDirectoryName);
    }

    private static (string StagingPath, string DestinationPath) ReserveUniqueBackupPath(string directory)
    {
        Directory.CreateDirectory(directory);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
            var baseName = $"forgedesk-backup-{timestamp}-{Guid.NewGuid():N}";
            var stagingPath = Path.Combine(directory, $"{baseName}.partial");
            var destinationPath = Path.Combine(directory, $"{baseName}.db");
            try
            {
                // Reserve a staging file with CreateNew. The completed backup is
                // atomically renamed afterward, so readers never see a partial
                // file using the final .db name and no existing file is replaced.
                using var reservation = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                return (stagingPath, destinationPath);
            }
            catch (IOException) when (File.Exists(stagingPath))
            {
                // A generated name collided. Generate another one without ever
                // replacing the file that won the race.
            }
        }

        throw new IOException("Could not allocate a unique SQLite backup filename after several attempts.");
    }

    private static void EnsureDatabaseFileExists(string path, string message)
    {
        if (Directory.Exists(path))
        {
            throw new IOException($"The expected SQLite database path '{path}' is a directory.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{message} Path: '{path}'.", path);
        }
    }

    private static string NormalizePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A non-empty filesystem path is required.", parameterName);
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"The filesystem path '{path}' is not valid.", parameterName, exception);
        }
    }

    private static bool PathsReferToSameFile(string firstPath, string secondPath)
        => string.Equals(
            firstPath,
            secondPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string BuildConnectionString(string path, SqliteOpenMode mode)
        => new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
}
