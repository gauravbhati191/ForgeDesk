namespace ForgeDesk.Domain;

/// <summary>Outcome recorded for an individual discovered file.</summary>
public enum DiscoveryStatus
{
    Discovered = 0,
    Processed = 1,
    Skipped = 2,
    Failed = 3
}
