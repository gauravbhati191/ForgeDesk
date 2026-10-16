using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Infrastructure;

/// <summary>Applies checked-in EF Core migrations and reports initialization failures to the caller.</summary>
public sealed class DatabaseInitializer
{
    private readonly ForgeDeskDbContext _dbContext;
    private readonly IDatabasePathProvider _pathProvider;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(ForgeDeskDbContext dbContext, IDatabasePathProvider pathProvider, ILogger<DatabaseInitializer> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _pathProvider.EnsureDirectoryExists();
        try
        {
            await _dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ForgeDesk database initialization failed at {DatabasePath}.", _pathProvider.DatabasePath);
            throw;
        }
    }
}
