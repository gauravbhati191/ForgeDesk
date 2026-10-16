using ForgeDesk.Application;
using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.Infrastructure;

/// <summary>Persists onboarding phases with explicit SQLite transactions.</summary>
public sealed class ProjectOnboardingStore : IProjectOnboardingStore
{
    private readonly ForgeDeskDbContext _dbContext;

    public ProjectOnboardingStore(ForgeDeskDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task BeginAsync(Project project, Scan scan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(scan);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (_dbContext.Entry(project).State == EntityState.Detached)
        {
            var existing = await _dbContext.Projects.SingleOrDefaultAsync(item => item.Id == project.Id, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                await _dbContext.Projects.AddAsync(project, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _dbContext.Entry(existing).CurrentValues.SetValues(project);
            }
        }

        await _dbContext.Scans.AddAsync(scan, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveRunningAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(Project project, Scan scan, IReadOnlyCollection<SourceFile> sourceFiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(sourceFiles);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var trackedProject = await _dbContext.Projects.SingleAsync(item => item.Id == project.Id, cancellationToken).ConfigureAwait(false);
        var trackedScan = await _dbContext.Scans.SingleAsync(item => item.Id == scan.Id, cancellationToken).ConfigureAwait(false);
        _dbContext.Entry(trackedProject).CurrentValues.SetValues(project);
        _dbContext.Entry(trackedScan).CurrentValues.SetValues(scan);
        await _dbContext.SourceFiles.AddRangeAsync(sourceFiles, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveFailedAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveCancelledAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
