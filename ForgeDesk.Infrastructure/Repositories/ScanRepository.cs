using ForgeDesk.Application;
using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.Infrastructure;

/// <summary>EF Core repository for persisted scan lifecycle records.</summary>
public sealed class ScanRepository : IScanRepository
{
    private readonly ForgeDeskDbContext _dbContext;

    public ScanRepository(ForgeDeskDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<Scan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Scans.SingleOrDefaultAsync(scan => scan.Id == id, cancellationToken);

    public async Task AddAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        await _dbContext.Scans.AddAsync(scan, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        _dbContext.Entry(scan).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
