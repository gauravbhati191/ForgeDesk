using ForgeDesk.Application;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.Infrastructure;

/// <summary>
/// EF Core adapter for report reads. Every query is constrained by both the requested
/// project and scan identifiers; no report data is assembled from global rows.
/// </summary>
public sealed class AnalysisReportDataSource : IAnalysisReportDataSource
{
    private readonly ForgeDeskDbContext _dbContext;

    public AnalysisReportDataSource(ForgeDeskDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<AnalysisReportData?> GetAsync(
        Guid projectId,
        Guid scanId,
        AnalysisReportQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project identifier is required.", nameof(projectId));
        }

        if (scanId == Guid.Empty)
        {
            throw new ArgumentException("A scan identifier is required.", nameof(scanId));
        }

        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var scan = await _dbContext.Scans
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == scanId && item.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (scan is null)
        {
            return null;
        }

        var project = await _dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sourceFileCount = await _dbContext.SourceFiles
            .AsNoTracking()
            .CountAsync(item => item.ScanId == scanId, cancellationToken)
            .ConfigureAwait(false);
        var symbolCount = await _dbContext.Symbols
            .AsNoTracking()
            .CountAsync(item => item.ScanId == scanId, cancellationToken)
            .ConfigureAwait(false);
        var dependencyCount = await _dbContext.Dependencies
            .AsNoTracking()
            .CountAsync(item => item.ScanId == scanId, cancellationToken)
            .ConfigureAwait(false);
        var findingCount = await _dbContext.Findings
            .AsNoTracking()
            .CountAsync(item => item.ScanId == scanId, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var sourceFileRows = await _dbContext.SourceFiles
            .AsNoTracking()
            .Where(item => item.ScanId == scanId)
            .OrderBy(item => item.RelativePath)
            .Take(options.MaxSourceFiles + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var sourceFilesTruncated = sourceFileRows.Count > options.MaxSourceFiles;
        if (sourceFilesTruncated)
        {
            sourceFileRows.RemoveAt(sourceFileRows.Count - 1);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var symbolRows = await _dbContext.Symbols
            .AsNoTracking()
            .Where(item => item.ScanId == scanId)
            .OrderBy(item => item.SymbolKey)
            .ThenBy(item => item.Id)
            .Take(options.MaxSymbols + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var symbolsTruncated = symbolRows.Count > options.MaxSymbols;
        if (symbolsTruncated)
        {
            symbolRows.RemoveAt(symbolRows.Count - 1);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var dependencyRows = await _dbContext.Dependencies
            .AsNoTracking()
            .Where(item => item.ScanId == scanId)
            .OrderBy(item => item.RelationshipType)
            .ThenBy(item => item.SourceSymbolId)
            .ThenBy(item => item.TargetSymbolId)
            .ThenBy(item => item.Id)
            .Take(options.MaxDependencies + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var dependenciesTruncated = dependencyRows.Count > options.MaxDependencies;
        if (dependenciesTruncated)
        {
            dependencyRows.RemoveAt(dependencyRows.Count - 1);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var findingRows = await _dbContext.Findings
            .AsNoTracking()
            .Where(item => item.ScanId == scanId)
            .OrderBy(item => item.RuleId)
            .ThenBy(item => item.SourceFileId)
            .ThenBy(item => item.StartLine)
            .ThenBy(item => item.EndLine)
            .ThenBy(item => item.Message)
            .ThenBy(item => item.Id)
            .Take(options.MaxFindings + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var findingsTruncated = findingRows.Count > options.MaxFindings;
        if (findingsTruncated)
        {
            findingRows.RemoveAt(findingRows.Count - 1);
        }

        return new AnalysisReportData(
            project,
            scan,
            sourceFileRows,
            symbolRows,
            dependencyRows,
            findingRows,
            sourceFileCount,
            symbolCount,
            dependencyCount,
            findingCount,
            sourceFilesTruncated,
            symbolsTruncated,
            dependenciesTruncated,
            findingsTruncated);
    }
}
