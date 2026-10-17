using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Infrastructure;

/// <summary>Applies checked-in EF Core migrations and reports initialization failures to the caller.</summary>
public sealed class DatabaseInitializer
{
    private readonly ForgeDeskDbContext _dbContext;
    private readonly IDatabasePathProvider _pathProvider;
    private readonly IDatabaseBackupService _backupService;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        ForgeDeskDbContext dbContext,
        IDatabasePathProvider pathProvider,
        IDatabaseBackupService backupService,
        ILogger<DatabaseInitializer> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DatabaseInitializer(
        ForgeDeskDbContext dbContext,
        IDatabasePathProvider pathProvider,
        ILogger<DatabaseInitializer> logger)
        : this(dbContext, pathProvider, new SqliteDatabaseBackupService(pathProvider), logger)
    {
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _pathProvider.EnsureDirectoryExists();
        try
        {
            if (IsFileBackedConfiguredDatabase())
            {
                var pendingMigrations = (await _dbContext.Database
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false))
                    .ToArray();
                if (pendingMigrations.Length > 0)
                {
                    await _backupService
                        .CreateBackupAsync(cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await _dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ForgeDesk database initialization failed at {DatabasePath}.", _pathProvider.DatabasePath);
            throw;
        }
    }

    private bool IsFileBackedConfiguredDatabase()
    {
        if (!File.Exists(_pathProvider.DatabasePath))
        {
            return false;
        }

        var dataSource = _dbContext.Database.GetDbConnection().DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)
            || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var configuredPath = Path.GetFullPath(_pathProvider.DatabasePath);
            var actualPath = Path.GetFullPath(dataSource);
            return string.Equals(
                configuredPath,
                actualPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
