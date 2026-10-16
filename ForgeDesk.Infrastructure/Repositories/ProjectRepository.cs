using ForgeDesk.Application;
using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.Infrastructure;

/// <summary>EF Core project repository with normalized-path identity and recent ordering.</summary>
public sealed class ProjectRepository : IProjectRepository
{
    private readonly ForgeDeskDbContext _dbContext;

    public ProjectRepository(ForgeDeskDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Projects.SingleOrDefaultAsync(project => project.Id == id, cancellationToken);

    public Task<Project?> GetByNormalizedPathAsync(string normalizedRootPath, CancellationToken cancellationToken = default)
    {
        var normalized = PathNormalization.Normalize(normalizedRootPath);
        return _dbContext.Projects.SingleOrDefaultAsync(project => project.NormalizedRootPath == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Projects.AsNoTracking().OrderBy(project => project.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Project>> ListRecentAsync(CancellationToken cancellationToken = default)
    {
        // SQLite does not translate DateTimeOffset ordering. Materialize the small local
        // recent-project list and order it in memory while retaining deterministic UTC values.
        var projects = await _dbContext.Projects
            .Include(project => project.Scans)
            .ThenInclude(scan => scan.SourceFiles)
            .AsNoTracking()
            .Where(project => project.LastOpenedAtUtc != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return projects
            .OrderByDescending(project => project.IsFavorite)
            .ThenByDescending(project => project.LastOpenedAtUtc)
            .ToArray();
    }

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        await _dbContext.Projects.AddAsync(project, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        _dbContext.Entry(project).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveFromRecentAsync(string normalizedRootPath, CancellationToken cancellationToken = default)
    {
        var normalized = PathNormalization.Normalize(normalizedRootPath);
        var project = await _dbContext.Projects.SingleOrDefaultAsync(item => item.NormalizedRootPath == normalized, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return;
        }

        project.ClearLastOpened();
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
