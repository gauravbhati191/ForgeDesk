using ForgeDesk.Application;
using ForgeDesk.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.Infrastructure;

/// <summary>EF Core setting repository with key-based upsert semantics.</summary>
public sealed class SettingRepository : ISettingRepository
{
    private readonly ForgeDeskDbContext _dbContext;

    public SettingRepository(ForgeDeskDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default)
        => _dbContext.Settings.SingleOrDefaultAsync(setting => setting.Key == NormalizeKey(key), cancellationToken);

    public async Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Settings.AsNoTracking().OrderBy(setting => setting.Key).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task UpsertAsync(Setting setting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var existing = await _dbContext.Settings.SingleOrDefaultAsync(item => item.Key == setting.Key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await _dbContext.Settings.AddAsync(setting, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.SetValue(setting.Value, setting.UpdatedAtUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }
        _dbContext.Settings.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeKey(string key)
    {
        var value = key?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new ArgumentException("A setting key is required.", nameof(key));
        }
        return value;
    }
}
