using ForgeDesk.Graph;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.Infrastructure;

/// <summary>
/// Read-only EF Core boundary for graph data. Every query is scoped to one scan and uses
/// no-tracking materialization so graph filtering/layout cannot mutate persisted analysis rows.
/// </summary>
public sealed class GraphQueryRepository : IGraphQueryRepository
{
    private readonly ForgeDeskDbContext _dbContext;

    public GraphQueryRepository(ForgeDeskDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<GraphQuerySnapshot?> GetSnapshotAsync(
        Guid scanId,
        CancellationToken cancellationToken = default)
    {
        if (scanId == Guid.Empty)
        {
            throw new ArgumentException("A scan identifier is required.", nameof(scanId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scan = await _dbContext.Scans
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == scanId, cancellationToken)
            .ConfigureAwait(false);
        if (scan is null)
        {
            return null;
        }

        var project = await _dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == scan.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var sourceFiles = await _dbContext.SourceFiles
            .AsNoTracking()
            .Where(item => item.ScanId == scanId)
            .OrderBy(item => item.RelativePath)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var symbols = await _dbContext.Symbols
            .AsNoTracking()
            .Where(item => item.ScanId == scanId)
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var dependencies = await _dbContext.Dependencies
            .AsNoTracking()
            .Where(item => item.ScanId == scanId)
            .OrderBy(item => item.SourceSymbolId)
            .ThenBy(item => item.TargetSymbolId)
            .ThenBy(item => item.RelationshipType)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var findings = await _dbContext.Findings
            .AsNoTracking()
            .Where(item => item.ScanId == scanId)
            .OrderBy(item => item.SymbolId)
            .ThenBy(item => item.StartLine)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new GraphQuerySnapshot(scanId, scan, project, sourceFiles, symbols, dependencies, findings);
    }

    public Task<GraphQuerySnapshot?> LoadAsync(
        Guid scanId,
        CancellationToken cancellationToken = default)
        => GetSnapshotAsync(scanId, cancellationToken);
}
