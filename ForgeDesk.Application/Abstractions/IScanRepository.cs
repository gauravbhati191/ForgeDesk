using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Persistence operations for scan lifecycle records.</summary>
public interface IScanRepository
{
    Task<Scan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Scan scan, CancellationToken cancellationToken = default);
    Task UpdateAsync(Scan scan, CancellationToken cancellationToken = default);
}
