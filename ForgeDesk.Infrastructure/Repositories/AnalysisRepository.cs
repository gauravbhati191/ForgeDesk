using ForgeDesk.Application;
using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;

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
        var oldDependencies = await _dbContext.Dependencies
            .Where(item => item.ScanId == scanId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var oldFindings = await _dbContext.Findings
            .Where(item => item.ScanId == scanId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var oldSymbols = await _dbContext.Symbols
            .Where(item => item.ScanId == scanId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _dbContext.Dependencies.RemoveRange(oldDependencies);
        _dbContext.Findings.RemoveRange(oldFindings);
        _dbContext.Symbols.RemoveRange(oldSymbols);
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

        // Delete in dependency order. Replacing by scan identity makes rerunning a scan safe
        // even though entity IDs are generated for the current result set.
        var oldDependencies = await _dbContext.Dependencies
            .Where(item => item.ScanId == scanId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var oldFindings = await _dbContext.Findings
            .Where(item => item.ScanId == scanId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var oldSymbols = await _dbContext.Symbols
            .Where(item => item.ScanId == scanId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _dbContext.Dependencies.RemoveRange(oldDependencies);
        _dbContext.Findings.RemoveRange(oldFindings);
        _dbContext.Symbols.RemoveRange(oldSymbols);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _dbContext.Symbols.AddRangeAsync(symbols, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _dbContext.Dependencies.AddRangeAsync(dependencies, cancellationToken).ConfigureAwait(false);
        await _dbContext.Findings.AddRangeAsync(findings, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
