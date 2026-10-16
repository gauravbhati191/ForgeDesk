namespace ForgeDesk.Domain;

/// <summary>A metadata discovery run for a registered project.</summary>
public sealed class Scan
{
    private Scan()
    {
        ToolVersion = string.Empty;
    }

    private Scan(Guid id, Guid projectId, string toolVersion, DateTimeOffset queuedAtUtc)
    {
        if (id == Guid.Empty || projectId == Guid.Empty)
        {
            throw new DomainInvariantException("A scan and project identifier are required.");
        }

        Id = id;
        ProjectId = projectId;
        ToolVersion = ValidateToolVersion(toolVersion);
        QueuedAtUtc = queuedAtUtc;
        Status = ScanStatus.Pending;
    }

    public static Scan CreatePending(Guid projectId, string toolVersion, DateTimeOffset? queuedAtUtc = null)
        => new(Guid.NewGuid(), projectId, toolVersion, queuedAtUtc ?? DateTimeOffset.UtcNow);

    public static Scan Queue(Guid projectId, DateTimeOffset? queuedAtUtc = null)
        => CreatePending(projectId, "Phase1", queuedAtUtc);

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public ScanStatus Status { get; private set; }
    public int FilesDiscovered { get; private set; }
    public int FilesProcessed { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string ToolVersion { get; private set; }
    public DateTimeOffset QueuedAtUtc { get; private set; }
    public bool CancellationRequested { get; private set; }

    // Compatibility aliases for earlier lifecycle callers.
    public DateTimeOffset QueuedAt => QueuedAtUtc;
    public DateTimeOffset? StartedAt => StartedAtUtc;
    public DateTimeOffset? CompletedAt => CompletedAtUtc;
    public int SourceFileCount => FilesDiscovered;
    public int FindingCount { get; private set; }

    public Project? Project { get; private set; }
    public ICollection<SourceFile> SourceFiles { get; private set; } = new List<SourceFile>();
    public ICollection<Symbol> Symbols { get; private set; } = new List<Symbol>();
    public ICollection<Dependency> Dependencies { get; private set; } = new List<Dependency>();
    public ICollection<Finding> Findings { get; private set; } = new List<Finding>();

    public void Start(DateTimeOffset? startedAtUtc = null)
    {
        EnsureStatus(ScanStatus.Pending, "Only a pending scan can start.");
        if (CancellationRequested)
        {
            throw new DomainInvariantException("A cancelled scan cannot start.");
        }

        Status = ScanStatus.Running;
        StartedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    public void RecordDiscovered(int count)
    {
        EnsureRunning();
        if (count < 0)
        {
            throw new DomainInvariantException("Discovered file counts cannot be negative.");
        }

        FilesDiscovered = count;
        if (FilesProcessed > FilesDiscovered)
        {
            throw new DomainInvariantException("Processed files cannot exceed discovered files.");
        }
    }

    public void RecordProcessed(int count)
    {
        EnsureRunning();
        if (count < 0 || count > FilesDiscovered)
        {
            throw new DomainInvariantException("Processed files must be between zero and discovered files.");
        }

        FilesProcessed = count;
    }

    public void Complete(int filesDiscovered, int filesProcessed, DateTimeOffset? completedAtUtc = null)
    {
        EnsureStatus(ScanStatus.Running, "Only a running scan can complete.");
        if (filesDiscovered < 0 || filesProcessed < 0 || filesProcessed > filesDiscovered)
        {
            throw new DomainInvariantException("Scan file counts are invalid.");
        }

        FilesDiscovered = filesDiscovered;
        FilesProcessed = filesProcessed;
        Status = ScanStatus.Completed;
        CompletedAtUtc = completedAtUtc ?? DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    public void Fail(string errorMessage, DateTimeOffset? completedAtUtc = null)
    {
        EnsureStatus(ScanStatus.Pending, ScanStatus.Running, "Only a pending or running scan can fail.");
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new DomainInvariantException("A scan failure must include an error message.");
        }

        Status = ScanStatus.Failed;
        CompletedAtUtc = completedAtUtc ?? DateTimeOffset.UtcNow;
        ErrorMessage = errorMessage.Trim();
    }

    public void RequestCancellation()
    {
        EnsureStatus(ScanStatus.Pending, ScanStatus.Running, "Only an active scan can request cancellation.");
        CancellationRequested = true;
    }

    public void Cancel(DateTimeOffset? completedAtUtc = null)
    {
        EnsureStatus(ScanStatus.Pending, ScanStatus.Running, "Only an active scan can be cancelled.");
        CancellationRequested = true;
        Status = ScanStatus.Cancelled;
        CompletedAtUtc = completedAtUtc ?? DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    private void EnsureRunning() => EnsureStatus(ScanStatus.Running, "Only a running scan can record progress.");

    private void EnsureStatus(ScanStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new DomainInvariantException(message);
        }
    }

    private void EnsureStatus(ScanStatus first, ScanStatus second, string message)
    {
        if (Status != first && Status != second)
        {
            throw new DomainInvariantException(message);
        }
    }

    private static string ValidateToolVersion(string toolVersion)
    {
        var value = toolVersion?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 128)
        {
            throw new DomainInvariantException("A scan tool version is required and cannot exceed 128 characters.");
        }

        return value;
    }
}
