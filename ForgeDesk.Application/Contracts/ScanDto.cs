using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Read model for a scan lifecycle record.</summary>
public sealed record ScanDto(
    Guid Id,
    Guid ProjectId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    ScanStatus Status,
    int FilesDiscovered,
    int FilesProcessed,
    string? ErrorMessage,
    string ToolVersion)
{
    public static ScanDto From(Scan scan)
        => new(scan.Id, scan.ProjectId, scan.StartedAtUtc, scan.CompletedAtUtc, scan.Status, scan.FilesDiscovered, scan.FilesProcessed, scan.ErrorMessage, scan.ToolVersion);
}
