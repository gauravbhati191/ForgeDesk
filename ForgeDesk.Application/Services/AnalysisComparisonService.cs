using System.Globalization;

namespace ForgeDesk.Application;

/// <summary>
/// Compares two persisted scan reports without using database-generated IDs as identity.
/// Exact fingerprints are matched first, followed by conservative line/path/location keys.
/// </summary>
public sealed class AnalysisComparisonService : IAnalysisComparisonService
{
    private const char KeySeparator = '\u001f';
    private readonly IAnalysisReportService _reportService;

    public AnalysisComparisonService(IAnalysisReportService reportService)
    {
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
    }

    public async Task<AnalysisComparisonResult> CompareAsync(
        Guid projectId,
        Guid baselineScanId,
        Guid currentScanId,
        AnalysisReportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project identifier is required.", nameof(projectId));
        }

        if (baselineScanId == Guid.Empty)
        {
            throw new ArgumentException("A baseline scan identifier is required.", nameof(baselineScanId));
        }

        if (currentScanId == Guid.Empty)
        {
            throw new ArgumentException("A current scan identifier is required.", nameof(currentScanId));
        }

        var reportOptions = options ?? new AnalysisReportOptions();
        reportOptions.Validate();
        // Git is current-at-report-time context, not persisted run identity, so it must not
        // affect a read-only historical comparison.
        reportOptions = reportOptions with { IncludeGitContext = false };

        var baseline = await _reportService
            .BuildAsync(projectId, baselineScanId, reportOptions, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var current = await _reportService
            .BuildAsync(projectId, currentScanId, reportOptions, cancellationToken)
            .ConfigureAwait(false);

        if (baseline.Project.ProjectId != current.Project.ProjectId || baseline.Project.ProjectId != projectId)
        {
            throw new ProjectValidationException(["Both selected analysis runs must belong to the requested project."]);
        }

        var limitations = new SortedSet<string>(StringComparer.Ordinal);
        AddReportLimitations(baseline, limitations);
        AddReportLimitations(current, limitations);
        limitations.Add("Analysis rule configuration and binding mode are not persisted; compatibility is inferred from lifecycle status and tool version.");

        var reliable = baseline.Scan.AnalysisStatus == "Completed"
            && current.Scan.AnalysisStatus == "Completed"
            && string.Equals(baseline.Scan.ToolVersion, current.Scan.ToolVersion, StringComparison.Ordinal);
        if (!string.Equals(baseline.Scan.ToolVersion, current.Scan.ToolVersion, StringComparison.Ordinal))
        {
            limitations.Add("The selected scans use different tool versions; comparison compatibility cannot be guaranteed.");
        }

        var baselineEntries = baseline.Findings
            .Select((finding, index) => new IndexedFinding(index, finding))
            .ToArray();
        var currentEntries = current.Findings
            .Select((finding, index) => new IndexedFinding(index, finding))
            .ToArray();
        foreach (var entry in baselineEntries.Concat(currentEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.HasMissingMetadata)
            {
                reliable = false;
                limitations.Add("One or more findings lack source path, line, or symbol metadata; those matches are tentative.");
                break;
            }
        }

        var baselineRemaining = baselineEntries.Select(entry => entry.Index).ToHashSet();
        var currentRemaining = currentEntries.Select(entry => entry.Index).ToHashSet();
        var changes = new List<AnalysisFindingChange>(baselineEntries.Length + currentEntries.Length);

        MatchExactFingerprints(
            baselineEntries,
            currentEntries,
            baselineRemaining,
            currentRemaining,
            changes,
            limitations,
            cancellationToken);
        MatchByKey(
            baselineEntries,
            currentEntries,
            baselineRemaining,
            currentRemaining,
            entry => entry.LineIndependentKey,
            "same rule, path, symbol, and message",
            allowAmbiguous: true,
            changes,
            limitations,
            cancellationToken);
        MatchByKey(
            baselineEntries,
            currentEntries,
            baselineRemaining,
            currentRemaining,
            entry => entry.PathIndependentKey,
            "same rule, symbol, and message",
            allowAmbiguous: false,
            changes,
            limitations,
            cancellationToken);
        MatchByKey(
            baselineEntries,
            currentEntries,
            baselineRemaining,
            currentRemaining,
            entry => entry.LocationKey,
            "same rule, path, symbol, and location",
            allowAmbiguous: false,
            changes,
            limitations,
            cancellationToken);

        foreach (var index in baselineRemaining.OrderBy(index => index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finding = baselineEntries[index].Finding;
            changes.Add(new AnalysisFindingChange(
                AnalysisFindingChangeKind.Removed,
                finding.Fingerprint,
                null,
                finding,
                null,
                Array.Empty<string>(),
                !baselineEntries[index].HasMissingMetadata,
                baselineEntries[index].HasMissingMetadata ? "The baseline finding lacks stable location metadata." : null));
        }

        foreach (var index in currentRemaining.OrderBy(index => index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finding = currentEntries[index].Finding;
            changes.Add(new AnalysisFindingChange(
                AnalysisFindingChangeKind.New,
                null,
                finding.Fingerprint,
                null,
                finding,
                Array.Empty<string>(),
                !currentEntries[index].HasMissingMetadata,
                currentEntries[index].HasMissingMetadata ? "The current finding lacks stable location metadata." : null));
        }

        var orderedChanges = changes
            .OrderBy(change => change.Kind)
            .ThenBy(change => change.BaselineFingerprint ?? change.CurrentFingerprint, StringComparer.Ordinal)
            .ToArray();
        if (limitations.Any(item => item.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                || item.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)))
        {
            reliable = false;
        }

        return new AnalysisComparisonResult(
            AnalysisReportSchema.Version,
            AnalysisReportSchema.FingerprintVersion,
            projectId,
            current.Project.Name,
            baseline.Scan.ScanId,
            current.Scan.ScanId,
            baseline.Scan.AnalysisStatus,
            current.Scan.AnalysisStatus,
            baseline.Summary.FindingCount,
            current.Summary.FindingCount,
            orderedChanges.Count(change => change.Kind == AnalysisFindingChangeKind.Unchanged),
            orderedChanges.Count(change => change.Kind == AnalysisFindingChangeKind.New),
            orderedChanges.Count(change => change.Kind == AnalysisFindingChangeKind.Removed),
            orderedChanges.Count(change => change.Kind == AnalysisFindingChangeKind.Changed),
            BuildCountDeltas(baseline.Summary.FindingsBySeverity, current.Summary.FindingsBySeverity),
            BuildCountDeltas(baseline.Summary.FindingsByRule, current.Summary.FindingsByRule),
            BuildCountDeltas(baseline.Summary.FindingsByFile, current.Summary.FindingsByFile),
            orderedChanges,
            limitations.ToArray(),
            reliable);
    }

    private static void MatchExactFingerprints(
        IReadOnlyList<IndexedFinding> baseline,
        IReadOnlyList<IndexedFinding> current,
        ISet<int> baselineRemaining,
        ISet<int> currentRemaining,
        ICollection<AnalysisFindingChange> changes,
        ISet<string> limitations,
        CancellationToken cancellationToken)
    {
        var keys = baseline
            .Where(entry => baselineRemaining.Contains(entry.Index))
            .Select(entry => entry.Finding.Fingerprint)
            .Concat(current.Where(entry => currentRemaining.Contains(entry.Index)).Select(entry => entry.Finding.Fingerprint))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baselineMatches = baseline.Where(entry => baselineRemaining.Contains(entry.Index) && entry.Finding.Fingerprint == key).OrderBy(entry => entry.Index).ToArray();
            var currentMatches = current.Where(entry => currentRemaining.Contains(entry.Index) && entry.Finding.Fingerprint == key).OrderBy(entry => entry.Index).ToArray();
            if (baselineMatches.Length > 1 || currentMatches.Length > 1)
            {
                limitations.Add("Duplicate findings share a stable fingerprint; occurrence order is used and the comparison is ambiguous.");
            }

            var pairCount = Math.Min(baselineMatches.Length, currentMatches.Length);
            for (var index = 0; index < pairCount; index++)
            {
                baselineRemaining.Remove(baselineMatches[index].Index);
                currentRemaining.Remove(currentMatches[index].Index);
                changes.Add(CreatePairedChange(baselineMatches[index], currentMatches[index], isReliable: true, note: null));
            }
        }
    }

    private static void MatchByKey(
        IReadOnlyList<IndexedFinding> baseline,
        IReadOnlyList<IndexedFinding> current,
        ISet<int> baselineRemaining,
        ISet<int> currentRemaining,
        Func<IndexedFinding, string> keySelector,
        string keyDescription,
        bool allowAmbiguous,
        ICollection<AnalysisFindingChange> changes,
        ISet<string> limitations,
        CancellationToken cancellationToken)
    {
        var keys = baseline
            .Where(entry => baselineRemaining.Contains(entry.Index))
            .Select(keySelector)
            .Concat(current.Where(entry => currentRemaining.Contains(entry.Index)).Select(keySelector))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baselineMatches = baseline.Where(entry => baselineRemaining.Contains(entry.Index) && keySelector(entry) == key).OrderBy(entry => EntryOrder(entry)).ToArray();
            var currentMatches = current.Where(entry => currentRemaining.Contains(entry.Index) && keySelector(entry) == key).OrderBy(entry => EntryOrder(entry)).ToArray();
            if (baselineMatches.Length == 0 || currentMatches.Length == 0)
            {
                continue;
            }

            if (!allowAmbiguous && (baselineMatches.Length > 1 || currentMatches.Length > 1))
            {
                limitations.Add($"Ambiguous finding matches for {keyDescription}; unmatched rows remain new/removed rather than being falsely paired.");
                continue;
            }

            if (baselineMatches.Length > 1 || currentMatches.Length > 1)
            {
                limitations.Add($"Duplicate findings matched by {keyDescription}; occurrence order is used and the comparison is ambiguous.");
            }

            var pairCount = Math.Min(baselineMatches.Length, currentMatches.Length);
            for (var index = 0; index < pairCount; index++)
            {
                baselineRemaining.Remove(baselineMatches[index].Index);
                currentRemaining.Remove(currentMatches[index].Index);
                changes.Add(CreatePairedChange(
                    baselineMatches[index],
                    currentMatches[index],
                    isReliable: baselineMatches.Length == 1 && currentMatches.Length == 1,
                    note: baselineMatches.Length == 1 && currentMatches.Length == 1
                        ? null
                        : "The finding match used duplicate occurrence order."));
            }
        }
    }

    private static AnalysisFindingChange CreatePairedChange(
        IndexedFinding baseline,
        IndexedFinding current,
        bool isReliable,
        string? note)
    {
        var differences = new SortedSet<string>(StringComparer.Ordinal);
        if (!string.Equals(baseline.Finding.RuleId, current.Finding.RuleId, StringComparison.Ordinal))
        {
            differences.Add("RuleChanged");
        }

        if (!string.Equals(baseline.Finding.Severity, current.Finding.Severity, StringComparison.Ordinal))
        {
            differences.Add("SeverityChanged");
        }

        if (!string.Equals(
                AnalysisFindingFingerprint.NormalizeRelativePath(baseline.Finding.RelativePath),
                AnalysisFindingFingerprint.NormalizeRelativePath(current.Finding.RelativePath),
                StringComparison.Ordinal))
        {
            differences.Add("PathChanged");
        }

        if (baseline.Finding.StartLine != current.Finding.StartLine || baseline.Finding.EndLine != current.Finding.EndLine)
        {
            differences.Add("LineChanged");
        }

        if (!string.Equals(
                AnalysisFindingFingerprint.NormalizeText(baseline.Finding.Message),
                AnalysisFindingFingerprint.NormalizeText(current.Finding.Message),
                StringComparison.Ordinal))
        {
            differences.Add("MessageChanged");
        }

        if (!string.Equals(
                AnalysisFindingFingerprint.NormalizeSymbol(baseline.Finding.SymbolKey, baseline.Finding.SymbolName),
                AnalysisFindingFingerprint.NormalizeSymbol(current.Finding.SymbolKey, current.Finding.SymbolName),
                StringComparison.Ordinal))
        {
            differences.Add("SymbolChanged");
        }

        var kind = differences.Count == 0 ? AnalysisFindingChangeKind.Unchanged : AnalysisFindingChangeKind.Changed;
        return new AnalysisFindingChange(
            kind,
            baseline.Finding.Fingerprint,
            current.Finding.Fingerprint,
            baseline.Finding,
            current.Finding,
            differences.ToArray(),
            isReliable && !baseline.HasMissingMetadata && !current.HasMissingMetadata,
            note);
    }

    private static IReadOnlyDictionary<string, AnalysisComparisonCountDelta> BuildCountDeltas(
        IReadOnlyDictionary<string, int> baseline,
        IReadOnlyDictionary<string, int> current)
    {
        var keys = baseline.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal);
        var result = new SortedDictionary<string, AnalysisComparisonCountDelta>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            result[key] = new AnalysisComparisonCountDelta(
                baseline.TryGetValue(key, out var baselineCount) ? baselineCount : 0,
                current.TryGetValue(key, out var currentCount) ? currentCount : 0);
        }

        return result;
    }

    private static void AddReportLimitations(AnalysisReportModel report, ISet<string> limitations)
    {
        foreach (var limitation in report.Limitations)
        {
            if (!limitation.Contains("Absolute project and repository root paths", StringComparison.Ordinal))
            {
                limitations.Add(limitation);
            }
        }
    }

    private static string EntryOrder(IndexedFinding entry)
        => string.Join(
            KeySeparator,
            entry.Finding.RelativePath ?? string.Empty,
            entry.Finding.StartLine?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            entry.Finding.EndLine?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            entry.Finding.Fingerprint,
            entry.Index.ToString(CultureInfo.InvariantCulture));

    private sealed record IndexedFinding(int Index, AnalysisReportFinding Finding)
    {
        public string LineIndependentKey
            => string.Join(
                KeySeparator,
                Finding.RuleId,
                AnalysisFindingFingerprint.NormalizeRelativePath(Finding.RelativePath),
                AnalysisFindingFingerprint.NormalizeSymbol(Finding.SymbolKey, Finding.SymbolName),
                AnalysisFindingFingerprint.NormalizeText(Finding.Message));

        public string PathIndependentKey
            => string.Join(
                KeySeparator,
                Finding.RuleId,
                AnalysisFindingFingerprint.NormalizeSymbol(Finding.SymbolKey, Finding.SymbolName),
                AnalysisFindingFingerprint.NormalizeText(Finding.Message));

        public string LocationKey
            => string.Join(
                KeySeparator,
                Finding.RuleId,
                AnalysisFindingFingerprint.NormalizeRelativePath(Finding.RelativePath),
                AnalysisFindingFingerprint.NormalizeSymbol(Finding.SymbolKey, Finding.SymbolName),
                Finding.StartLine?.ToString(CultureInfo.InvariantCulture) ?? "<missing-start>",
                Finding.EndLine?.ToString(CultureInfo.InvariantCulture) ?? "<missing-end>");

        public bool HasMissingMetadata
            => string.IsNullOrWhiteSpace(Finding.RelativePath)
                || Finding.StartLine is null
                || string.IsNullOrWhiteSpace(Finding.SymbolKey) && string.IsNullOrWhiteSpace(Finding.SymbolName);
    }
}
