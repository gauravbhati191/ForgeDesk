using System.Globalization;
using ForgeDesk.Application;
using ForgeDesk.Domain;

namespace ForgeDesk.Presentation.Models;

/// <summary>Display option for a persisted analysis scan.</summary>
public sealed class ReportRunItem
{
    public ReportRunItem(AnalysisRunDto run, Guid? currentScanId)
    {
        ScanId = run.ScanId;
        Status = run.Status;
        CompletedAtUtc = run.CompletedAtUtc;
        SymbolCount = run.SymbolCount;
        DependencyCount = run.DependencyCount;
        FindingCount = run.FindingCount;
        IsCurrent = currentScanId == run.ScanId;
    }

    public Guid ScanId { get; }
    public AnalysisStatus Status { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public int SymbolCount { get; }
    public int DependencyCount { get; }
    public int FindingCount { get; }
    public bool IsCurrent { get; }

    public string ScanIdLabel => ScanId.ToString("D");

    public string StatusLabel => IsCurrent ? $"{Status} · Current" : Status.ToString();

    public string DateLabel => CompletedAtUtc is DateTimeOffset date
        ? date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
        : "Not completed";

    public string SummaryLabel
        => $"{DateLabel} · {SymbolCount:N0} symbols · {DependencyCount:N0} dependencies · {FindingCount:N0} findings";

    public string AccessibilityName => $"Scan {ScanIdLabel}, {StatusLabel}, {SummaryLabel}";
}

public enum ReportsDisplayState
{
    NoProject,
    Loading,
    Empty,
    Ready,
    Error,
    Cancelled
}
