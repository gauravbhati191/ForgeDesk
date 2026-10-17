using System.Text.Json;
using ForgeDesk.Application;
using ForgeDesk.Domain;

namespace ForgeDesk.UnitTests;

public sealed class ReportCoreTests
{
    [Fact]
    public async Task Report_counts_and_groupings_are_built_from_the_selected_run()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("Report fixture", directory.Path);
        var firstScan = CreateCompletedScan(project.Id, "scan-one", discoveredFiles: 2);
        var secondScan = CreateCompletedScan(project.Id, "scan-two");

        var firstFile = SourceFile.Create(firstScan.Id, "src/One.cs", "One.cs", ".cs", 10, DateTimeOffset.UtcNow, "C#");
        var secondFile = SourceFile.Create(firstScan.Id, "src/Two.cs", "Two.cs", ".cs", 20, DateTimeOffset.UtcNow, "C#");
        var firstSymbol = Symbol.Create(firstScan.Id, firstFile.Id, "T:One", "One", SymbolKind.Class, 1, 5);
        var secondSymbol = Symbol.Create(firstScan.Id, secondFile.Id, "T:Two", "Two", SymbolKind.Class, 1, 5);
        var dependencies = new[]
        {
            Dependency.Create(firstScan.Id, firstSymbol.Id, secondSymbol.Id, "Calls", isResolved: true),
            Dependency.Create(firstScan.Id, secondSymbol.Id, null, "Inherits")
        };
        var findings = new[]
        {
            Finding.Create(firstScan.Id, "FD001", FindingSeverity.Warning, "TODO in One", firstFile.Id, firstSymbol.Id, 2, 2),
            Finding.Create(firstScan.Id, "FD001", FindingSeverity.Warning, "TODO in Two", secondFile.Id, secondSymbol.Id, 3, 3),
            Finding.Create(firstScan.Id, "FD004", FindingSeverity.Error, "Empty catch", secondFile.Id, secondSymbol.Id, 4, 6)
        };
        SetAnalysisCounts(firstScan, filesProcessed: 2, symbolCount: 2, dependencyCount: 2, findingCount: findings.Length);

        var dataSource = new InMemoryReportDataSource(
            new AnalysisReportData(
                project,
                firstScan,
                [firstFile, secondFile],
                [firstSymbol, secondSymbol],
                dependencies,
                findings,
                2,
                2,
                2,
                3),
            new AnalysisReportData(
                project,
                secondScan,
                [],
                [],
                [],
                [],
                0,
                0,
                0,
                0));

        var report = await new AnalysisReportService(dataSource)
            .BuildAsync(project.Id, firstScan.Id, new AnalysisReportOptions { IncludeGitContext = false });

        Assert.Equal(2, report.Summary.SourceFileCount);
        Assert.Equal(2, report.Summary.SymbolCount);
        Assert.Equal(2, report.Summary.DependencyCount);
        Assert.Equal(3, report.Summary.FindingCount);
        Assert.Equal(2, report.Summary.FindingsBySeverity["Warning"]);
        Assert.Equal(1, report.Summary.FindingsBySeverity["Error"]);
        Assert.Equal(2, report.Summary.FindingsByRule["FD001"]);
        Assert.Equal(2, report.Summary.FindingsByFile["src/Two.cs"]);
        Assert.Equal(1, report.Summary.DependencyStatistics!.ResolvedCount);
        Assert.Equal(1, report.Summary.DependencyStatistics.UnresolvedCount);
        Assert.Null(report.Project.RootPath);
        Assert.True(report.Project.RootPathRedacted);
        Assert.Equal(3, report.Findings.Count);
        Assert.All(report.Findings, finding => Assert.False(string.IsNullOrWhiteSpace(finding.Fingerprint)));
        Assert.DoesNotContain(directory.Path, AnalysisReportSerializer.ToJson(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_and_incomplete_runs_distinguish_missing_analysis_counts()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("Empty fixture", directory.Path);
        var scan = Scan.CreatePending(project.Id, "test");
        scan.Start();
        scan.Complete(0, 0);

        var report = await new AnalysisReportService(
                new InMemoryReportDataSource(new AnalysisReportData(project, scan, [], [], [], [], 0, 0, 0, 0)))
            .BuildAsync(project.Id, scan.Id, new AnalysisReportOptions { IncludeGitContext = false });

        Assert.Equal(0, report.Summary.SourceFileCount);
        Assert.Null(report.Summary.SymbolCount);
        Assert.Null(report.Summary.DependencyCount);
        Assert.Null(report.Summary.FindingCount);
        Assert.Contains(report.Limitations, item => item.Contains("NotStarted", StringComparison.Ordinal));
        Assert.Empty(report.Findings);
    }

    [Fact]
    public async Task Json_is_valid_stable_and_has_the_documented_schema_fields()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("JSON fixture", directory.Path);
        var scan = CreateCompletedScan(project.Id, "test");
        var data = new AnalysisReportData(project, scan, [], [], [], [], 0, 0, 0, 0);
        var service = new AnalysisReportService(new InMemoryReportDataSource(data));
        var first = await service.BuildAsync(project.Id, scan.Id, new AnalysisReportOptions { IncludeGitContext = false });
        var second = await service.BuildAsync(project.Id, scan.Id, new AnalysisReportOptions { IncludeGitContext = false });

        var firstJson = AnalysisReportSerializer.ToJson(first);
        var secondJson = AnalysisReportSerializer.ToJson(second);
        using var document = JsonDocument.Parse(firstJson);

        Assert.Equal(firstJson, secondJson);
        Assert.Equal(AnalysisReportSchema.Version, document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(AnalysisReportSchema.FingerprintVersion, document.RootElement.GetProperty("fingerprintVersion").GetString());
        Assert.True(document.RootElement.TryGetProperty("summary", out _));
        Assert.True(document.RootElement.TryGetProperty("findings", out _));
    }

    [Fact]
    public async Task Html_escapes_untrusted_messages_and_csv_escapes_fields()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("Markup fixture", directory.Path);
        var scan = CreateCompletedScan(project.Id, "test", discoveredFiles: 1);
        var file = SourceFile.Create(scan.Id, "unsafe.cs", "unsafe.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var finding = Finding.Create(
            scan.Id,
            "FD<1>",
            FindingSeverity.Warning,
            "<script>alert(\"x\")</script>, \"quoted\"\nnext",
            file.Id,
            null,
            4,
            5);
        SetAnalysisCounts(scan, filesProcessed: 1, symbolCount: 0, dependencyCount: 0, findingCount: 1);
        var report = await new AnalysisReportService(
                new InMemoryReportDataSource(new AnalysisReportData(project, scan, [file], [], [], [finding], 1, 0, 0, 1)))
            .BuildAsync(project.Id, scan.Id, new AnalysisReportOptions { IncludeGitContext = false });

        var html = AnalysisReportSerializer.ToHtml(report);
        var csv = AnalysisReportSerializer.ToCsv(report);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("\"<script>alert(\"\"x\"\")</script>, \"\"quoted\"\"\nnext\"", csv, StringComparison.Ordinal);
        Assert.Contains("FD<1>", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Safe_file_names_reject_traversal_and_keep_generated_names_bounded()
    {
        var scanId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var generated = AnalysisReportFileNames.Create("CON: <unsafe> project", scanId, AnalysisReportFormat.Json);

        Assert.EndsWith(".json", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("<", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(":", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("/", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("..", generated, StringComparison.Ordinal);
        using var directory = new TemporaryDirectory();
        Assert.Throws<AnalysisReportExportException>(() => AnalysisReportFileNames.ValidateOutputPath(
            Path.Combine(directory.Path, "..", "escape.json"),
            AnalysisReportFormat.Json));
    }

    [Fact]
    public async Task Export_reopens_as_json_does_not_overwrite_and_honors_cancellation()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("Export fixture", directory.Path);
        var scan = CreateCompletedScan(project.Id, "test");
        var service = new AnalysisReportService(
            new InMemoryReportDataSource(new AnalysisReportData(project, scan, [], [], [], [], 0, 0, 0, 0)));
        var path = Path.Combine(directory.Path, "report.json");
        var result = await service.ExportAsync(
            project.Id,
            scan.Id,
            AnalysisReportFormat.Json,
            path,
            new AnalysisReportOptions { IncludeGitContext = false });

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(result.BytesWritten, new FileInfo(path).Length);
        Assert.Equal(project.Name, document.RootElement.GetProperty("project").GetProperty("name").GetString());
        await Assert.ThrowsAsync<AnalysisReportExportException>(() => service.ExportAsync(
            project.Id,
            scan.Id,
            AnalysisReportFormat.Json,
            path,
            new AnalysisReportOptions { IncludeGitContext = false }));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(
            project.Id,
            scan.Id,
            AnalysisReportFormat.Json,
            Path.Combine(directory.Path, "cancelled.json"),
            new AnalysisReportOptions { IncludeGitContext = false },
            cancellation.Token));
        Assert.False(File.Exists(Path.Combine(directory.Path, "cancelled.json")));
    }

    [Fact]
    public async Task Comparison_matches_identical_new_removed_changed_line_and_path_findings()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("Comparison fixture", directory.Path);
        var baselineScan = CreateCompletedScan(project.Id, "test", discoveredFiles: 2);
        var currentScan = CreateCompletedScan(project.Id, "test", discoveredFiles: 2);

        var baselineFile = SourceFile.Create(baselineScan.Id, "src/Before.cs", "Before.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var baselineNewFile = SourceFile.Create(baselineScan.Id, "src/Removed.cs", "Removed.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var currentFile = SourceFile.Create(currentScan.Id, "src/Before.cs", "Before.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var currentMovedFile = SourceFile.Create(currentScan.Id, "src/After.cs", "After.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var baselineSymbol = Symbol.Create(baselineScan.Id, baselineFile.Id, "M:Demo.Widget.Run", "Run", SymbolKind.Method, 1, 2);
        var baselineRemovedSymbol = Symbol.Create(baselineScan.Id, baselineNewFile.Id, "M:Demo.Removed.Run", "Run", SymbolKind.Method, 1, 2);
        var currentSymbol = Symbol.Create(currentScan.Id, currentFile.Id, "M:Demo.Widget.Run", "Run", SymbolKind.Method, 1, 2);
        var currentMovedSymbol = Symbol.Create(currentScan.Id, currentMovedFile.Id, "M:Demo.Widget.Run", "Run", SymbolKind.Method, 1, 2);

        var baselineFindings = new[]
        {
            Finding.Create(baselineScan.Id, "FD001", FindingSeverity.Warning, "same", baselineFile.Id, baselineSymbol.Id, 10, 10),
            Finding.Create(baselineScan.Id, "FD002", FindingSeverity.Warning, "line moved", baselineFile.Id, baselineSymbol.Id, 20, 20),
            Finding.Create(baselineScan.Id, "FD003", FindingSeverity.Error, "path moved", baselineFile.Id, baselineSymbol.Id, 30, 30),
            Finding.Create(baselineScan.Id, "FD004", FindingSeverity.Info, "removed", baselineNewFile.Id, baselineRemovedSymbol.Id, 40, 40)
        };
        var currentFindings = new[]
        {
            Finding.Create(currentScan.Id, "FD001", FindingSeverity.Warning, "same", currentFile.Id, currentSymbol.Id, 10, 10),
            Finding.Create(currentScan.Id, "FD002", FindingSeverity.Warning, "line moved", currentFile.Id, currentSymbol.Id, 21, 21),
            Finding.Create(currentScan.Id, "FD003", FindingSeverity.Error, "path moved", currentMovedFile.Id, currentMovedSymbol.Id, 30, 30),
            Finding.Create(currentScan.Id, "FD005", FindingSeverity.Critical, "new", currentFile.Id, currentSymbol.Id, 50, 50)
        };
        SetAnalysisCounts(baselineScan, filesProcessed: 2, symbolCount: 2, dependencyCount: 0, findingCount: baselineFindings.Length);
        SetAnalysisCounts(currentScan, filesProcessed: 2, symbolCount: 2, dependencyCount: 0, findingCount: currentFindings.Length);

        var service = new AnalysisReportService(
            new InMemoryReportDataSource(
                CreateData(project, baselineScan, [baselineFile, baselineNewFile], [baselineSymbol, baselineRemovedSymbol], baselineFindings),
                CreateData(project, currentScan, [currentFile, currentMovedFile], [currentSymbol, currentMovedSymbol], currentFindings)));
        var comparison = await new AnalysisComparisonService(service)
            .CompareAsync(project.Id, baselineScan.Id, currentScan.Id);

        Assert.Equal(1, comparison.UnchangedCount);
        Assert.Equal(1, comparison.NewCount);
        Assert.Equal(1, comparison.RemovedCount);
        Assert.Equal(2, comparison.ChangedCount);
        Assert.Contains(comparison.ChangedFindings, item => item.Differences.Contains("LineChanged"));
        Assert.Contains(comparison.ChangedFindings, item => item.Differences.Contains("PathChanged"));
        Assert.Contains(comparison.NewFindings, item => item.CurrentFinding!.RuleId == "FD005");
        Assert.Contains(comparison.RemovedFindings, item => item.BaselineFinding!.RuleId == "FD004");
    }

    [Fact]
    public async Task Comparison_marks_same_location_message_and_severity_changes_without_database_ids()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("Changed finding", directory.Path);
        var baselineScan = CreateCompletedScan(project.Id, "test", discoveredFiles: 1);
        var currentScan = CreateCompletedScan(project.Id, "test", discoveredFiles: 1);
        var baselineFile = SourceFile.Create(baselineScan.Id, "src/Changed.cs", "Changed.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var currentFile = SourceFile.Create(currentScan.Id, "src/Changed.cs", "Changed.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var baselineSymbol = Symbol.Create(baselineScan.Id, baselineFile.Id, "M:Demo.Changed.Run", "Run", SymbolKind.Method, 1, 3);
        var currentSymbol = Symbol.Create(currentScan.Id, currentFile.Id, "M:Demo.Changed.Run", "Run", SymbolKind.Method, 1, 3);
        var baselineFinding = Finding.Create(baselineScan.Id, "FD010", FindingSeverity.Warning, "old message", baselineFile.Id, baselineSymbol.Id, 2, 2);
        var currentFinding = Finding.Create(currentScan.Id, "FD010", FindingSeverity.Error, "new message", currentFile.Id, currentSymbol.Id, 2, 2);
        SetAnalysisCounts(baselineScan, filesProcessed: 1, symbolCount: 1, dependencyCount: 0, findingCount: 1);
        SetAnalysisCounts(currentScan, filesProcessed: 1, symbolCount: 1, dependencyCount: 0, findingCount: 1);

        var service = new AnalysisReportService(
            new InMemoryReportDataSource(
                CreateData(project, baselineScan, [baselineFile], [baselineSymbol], [baselineFinding]),
                CreateData(project, currentScan, [currentFile], [currentSymbol], [currentFinding])));
        var comparison = await new AnalysisComparisonService(service)
            .CompareAsync(project.Id, baselineScan.Id, currentScan.Id);

        var changed = Assert.Single(comparison.ChangedFindings);
        Assert.Contains("MessageChanged", changed.Differences);
        Assert.Contains("SeverityChanged", changed.Differences);
        Assert.Equal(0, comparison.NewCount);
        Assert.Equal(0, comparison.RemovedCount);
    }

    [Fact]
    public async Task Comparison_reports_duplicate_and_missing_metadata_limitations()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("Comparison limitations", directory.Path);
        var baselineScan = CreateCompletedScan(project.Id, "test", discoveredFiles: 1);
        var currentScan = CreateCompletedScan(project.Id, "test", discoveredFiles: 1);
        var baselineFile = SourceFile.Create(baselineScan.Id, "same.cs", "same.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var currentFile = SourceFile.Create(currentScan.Id, "same.cs", "same.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var baselineDuplicateOne = Finding.Create(baselineScan.Id, "FD001", FindingSeverity.Warning, "duplicate", baselineFile.Id, null, 1, 1);
        var baselineDuplicateTwo = Finding.Create(baselineScan.Id, "FD001", FindingSeverity.Warning, "duplicate", baselineFile.Id, null, 1, 1);
        var currentDuplicate = Finding.Create(currentScan.Id, "FD001", FindingSeverity.Warning, "duplicate", currentFile.Id, null, 1, 1);
        var missingBaseline = Finding.Create(baselineScan.Id, "FD002", FindingSeverity.Warning, "missing", null, null);
        var missingCurrent = Finding.Create(currentScan.Id, "FD002", FindingSeverity.Warning, "missing", null, null);
        SetAnalysisCounts(baselineScan, filesProcessed: 1, symbolCount: 0, dependencyCount: 0, findingCount: 3);
        SetAnalysisCounts(currentScan, filesProcessed: 1, symbolCount: 0, dependencyCount: 0, findingCount: 2);

        var service = new AnalysisReportService(
            new InMemoryReportDataSource(
                CreateData(project, baselineScan, [baselineFile], [], [baselineDuplicateOne, baselineDuplicateTwo, missingBaseline]),
                CreateData(project, currentScan, [currentFile], [], [currentDuplicate, missingCurrent])));
        var comparison = await new AnalysisComparisonService(service)
            .CompareAsync(project.Id, baselineScan.Id, currentScan.Id);

        Assert.Contains(comparison.Limitations, item => item.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) || item.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(comparison.Limitations, item => item.Contains("lack source path", StringComparison.OrdinalIgnoreCase));
        Assert.False(comparison.IsReliable);
    }

    [Fact]
    public async Task Report_and_comparison_honor_cancellation_before_data_access()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("Cancellation", directory.Path);
        var scan = CreateCompletedScan(project.Id, "test");
        var source = new InMemoryReportDataSource(CreateData(project, scan, [], [], []));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AnalysisReportService(source)
            .BuildAsync(project.Id, scan.Id, new AnalysisReportOptions { IncludeGitContext = false }, cancellation.Token));
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task Optional_git_context_is_read_through_the_application_abstraction_and_redacted()
    {
        using var directory = new TemporaryDirectory();
        var project = Project.Create("Git report", directory.Path);
        var scan = CreateCompletedScan(project.Id, "test");
        var git = new RecordingGitService(new GitRepositorySnapshot(
            GitRepositoryState.Dirty,
            directory.Path,
            new GitBranchInfo("main", false),
            new GitCommitInfo(
                "0123456789abcdef0123456789abcdef01234567",
                "Commit <message>",
                "Author",
                "author@example.test",
                DateTimeOffset.UtcNow,
                [],
                []),
            [new GitFileChange("changed.cs", GitChangeKind.Modified, false, true, false, false, " ", "M")],
            []));

        var report = await new AnalysisReportService(
                new InMemoryReportDataSource(CreateData(project, scan, [], [], [])),
                git)
            .BuildAsync(project.Id, scan.Id);

        Assert.NotNull(report.GitContext);
        Assert.Equal(GitRepositoryState.Dirty, report.GitContext!.State);
        Assert.Null(report.GitContext.RepositoryRootPath);
        Assert.True(report.GitContext.RepositoryRootPathRedacted);
        Assert.False(report.GitContext.IsAssociatedWithAnalysisRun);
        Assert.Contains(report.Limitations, item => item.Contains("report time", StringComparison.Ordinal));
        Assert.DoesNotContain(directory.Path, AnalysisReportSerializer.ToJson(report), StringComparison.Ordinal);
    }

    private static AnalysisReportData CreateData(
        Project project,
        Scan scan,
        IReadOnlyList<SourceFile> sourceFiles,
        IReadOnlyList<Symbol> symbols,
        IReadOnlyList<Finding> findings)
        => new(
            project,
            scan,
            sourceFiles,
            symbols,
            [],
            findings,
            sourceFiles.Count,
            symbols.Count,
            0,
            findings.Count);

    private static Scan CreateCompletedScan(Guid projectId, string toolVersion, int discoveredFiles = 0)
    {
        var scan = Scan.CreatePending(projectId, toolVersion);
        scan.Start();
        scan.Complete(discoveredFiles, discoveredFiles);
        return scan;
    }

    private static void SetAnalysisCounts(Scan scan, int filesProcessed, int symbolCount, int dependencyCount, int findingCount)
    {
        scan.StartAnalysis();
        scan.CompleteAnalysis(filesProcessed, symbolCount, dependencyCount, findingCount);
    }

    private sealed class InMemoryReportDataSource : IAnalysisReportDataSource
    {
        private readonly Dictionary<(Guid ProjectId, Guid ScanId), AnalysisReportData> _data;

        public InMemoryReportDataSource(params AnalysisReportData[] data)
            => _data = data.ToDictionary(item => (item.Project.Id, item.Scan.Id));

        public int ReadCount { get; private set; }

        public Task<AnalysisReportData?> GetAsync(
            Guid projectId,
            Guid scanId,
            AnalysisReportQueryOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(_data.GetValueOrDefault((projectId, scanId)));
        }
    }

    private sealed class RecordingGitService : IGitRepositoryService
    {
        private readonly GitRepositorySnapshot _snapshot;

        public RecordingGitService(GitRepositorySnapshot snapshot) => _snapshot = snapshot;

        public Task<GitRepositorySnapshot> InspectAsync(
            string projectRoot,
            GitInspectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshot);
        }

        public Task<GitHistoryResult> GetHistoryAsync(
            string projectRoot,
            GitHistoryOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new GitHistoryResult(_snapshot.State, _snapshot.RepositoryRootPath, _snapshot.RecentCommits));
    }
}
