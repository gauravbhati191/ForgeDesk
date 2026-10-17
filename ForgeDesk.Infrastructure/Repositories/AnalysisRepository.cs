using ForgeDesk.Application;
using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ForgeDesk.Infrastructure;

/// <summary>EF Core persistence boundary for analysis state and scan-owned semantic results.</summary>
public sealed class AnalysisRepository : IAnalysisRepository
{
    private readonly ForgeDeskDbContext _dbContext;

    public AnalysisRepository(ForgeDeskDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<AnalysisContext?> GetContextAsync(Guid scanId, CancellationToken cancellationToken = default)
    {
        var scan = await _dbContext.Scans
            .Include(item => item.Project)
            .Include(item => item.SourceFiles)
            .SingleOrDefaultAsync(item => item.Id == scanId, cancellationToken)
            .ConfigureAwait(false);
        if (scan?.Project is null)
        {
            return null;
        }

        return new AnalysisContext(scan, scan.Project, scan.SourceFiles.ToArray());
    }

    public async Task SaveStatusAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (_dbContext.Entry(scan).State == EntityState.Detached)
        {
            _dbContext.Scans.Attach(scan);
        }

        _dbContext.Entry(scan).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearResultsAsync(Guid scanId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await DeleteResultsAsync(scanId, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceResultsAsync(
        Guid scanId,
        IReadOnlyCollection<Symbol> symbols,
        IReadOnlyCollection<Dependency> dependencies,
        IReadOnlyCollection<Finding> findings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(findings);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Delete in dependency order. Replacing by scan identity makes rerunning a scan safe
            // even though entity IDs are generated for the current result set.
            await DeleteResultsAsync(scanId, cancellationToken).ConfigureAwait(false);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _dbContext.Symbols.AddRangeAsync(symbols, cancellationToken).ConfigureAwait(false);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _dbContext.Dependencies.AddRangeAsync(dependencies, cancellationToken).ConfigureAwait(false);
            await _dbContext.Findings.AddRangeAsync(findings, cancellationToken).ConfigureAwait(false);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Rollback restores database rows, while detaching result entries leaves this context
            // usable after a failed or cancelled replacement instead of retaining pending rows.
            DetachTrackedResults(scanId);
            throw;
        }
    }

    private async Task DeleteResultsAsync(Guid scanId, CancellationToken cancellationToken)
    {
        // ExecuteDeleteAsync bypasses EF's change tracker. Detach any tracked rows first so
        // a later SaveChangesAsync cannot try to update or delete rows removed by the bulk SQL.
        DetachTrackedResults(scanId);

        // Dependencies and findings reference symbols with nullable SetNull foreign keys. Keep
        // the established order so this also works with providers enforcing the relationships.
        await _dbContext.Dependencies
            .Where(item => item.ScanId == scanId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await _dbContext.Findings
            .Where(item => item.ScanId == scanId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await _dbContext.Symbols
            .Where(item => item.ScanId == scanId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private void DetachTrackedResults(Guid scanId)
    {
        DetachTracked(_dbContext.ChangeTracker.Entries<Dependency>(), item => item.ScanId == scanId);
        DetachTracked(_dbContext.ChangeTracker.Entries<Finding>(), item => item.ScanId == scanId);
        DetachTracked(_dbContext.ChangeTracker.Entries<Symbol>(), item => item.ScanId == scanId);
    }

    private static void DetachTracked<TEntity>(
        IEnumerable<EntityEntry<TEntity>> entries,
        Func<TEntity, bool> belongsToScan)
        where TEntity : class
    {
        foreach (var entry in entries.Where(entry => belongsToScan(entry.Entity)).ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }
}
