namespace ForgeDesk.Infrastructure;

/// <summary>Creates and restores consistent backups of the local SQLite database.</summary>
public interface IDatabaseBackupService
{
    /// <summary>
    /// Creates a uniquely named backup without replacing an existing backup.
    /// When no directory is supplied, the backup is written below the database directory.
    /// </summary>
    Task<DatabaseBackupResult> CreateBackupAsync(
        string? backupDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>Restores the active database from a previously created SQLite backup.</summary>
    Task RestoreAsync(string backupPath, CancellationToken cancellationToken = default);
}

/// <summary>Details about a successfully created database backup.</summary>
public sealed record DatabaseBackupResult(string BackupPath, long SizeBytes);

/// <summary>Reports a failure while creating a database backup.</summary>
public sealed class DatabaseBackupException : IOException
{
    public DatabaseBackupException(string message)
        : base(message)
    {
    }

    public DatabaseBackupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Reports a failure while restoring the active database.</summary>
public sealed class DatabaseRestoreException : IOException
{
    public DatabaseRestoreException(string message)
        : base(message)
    {
    }

    public DatabaseRestoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
