namespace ForgeDesk.Domain;

/// <summary>Lifecycle states for semantic analysis attached to a completed discovery scan.</summary>
public enum AnalysisStatus
{
    NotStarted = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
