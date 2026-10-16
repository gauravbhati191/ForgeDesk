using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Persistence operations for local key/value settings.</summary>
public interface ISettingRepository
{
    Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(Setting setting, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
