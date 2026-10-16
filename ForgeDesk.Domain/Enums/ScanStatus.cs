namespace ForgeDesk.Domain;

/// <summary>Lifecycle states for a metadata discovery scan.</summary>
public enum ScanStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    Queued = Pending
}
