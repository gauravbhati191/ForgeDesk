using System.Text.Json;
using ForgeDesk.Application;
using ForgeDesk.Domain;
using ForgeDesk.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.IntegrationTests;

public sealed class ReportPersistenceIntegrationTests
{
    [Fact]
    public async Task SQLite_report_query_builds_selected_run_exports_and_compares_persisted_runs()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var projectRoot = new TemporaryDirectory();
        var project = Project.Create("Persisted reports", projectRoot.Path);
        var baselineScan = CreateCompletedScan(project.Id, "analysis-test");
        var currentScan = CreateCompletedScan(project.Id, "analysis-test");
        var baselineFile = SourceFile.Create(baselineScan.Id, "Baseline.cs", "Baseline.cs", ".cs", 12, DateTimeOffset.UtcNow, "C#");
        var currentFile = SourceFile.Create(currentScan.Id, "Current.cs", "Current.cs", ".cs", 12, DateTimeOffset.UtcNow, "C#");
        var baselineSymbol = Symbol.Create(baselineScan.Id, baselineFile.Id, "T:Demo.Widget", "Widget", SymbolKind.Class, 1, 4);
        var currentSymbol = Symbol.Create(currentScan.Id, currentFile.Id, "T:Demo.Widget", "Widget", SymbolKind.Class, 1, 4);
        var baselineFinding = Finding.Create(baselineScan.Id, "FD001", FindingSeverity.Warning, "baseline", baselineFile.Id, baselineSymbol.Id, 2, 2);
        var currentFinding = Finding.Create(currentScan.Id, "FD002", FindingSeverity.Error, "current", currentFile.Id, currentSymbol.Id, 3, 3);
        baselineScan.StartAnalysis();
        baselineScan.CompleteAnalysis(1, 1, 0, 1);
        currentScan.StartAnalysis();
        currentScan.CompleteAnalysis(1, 1, 0, 1);

        await harness.Context.AddRangeAsync(
            project,
            baselineScan,
            currentScan,
            baselineFile,
            currentFile,
            baselineSymbol,
            currentSymbol,
            baselineFinding,
            currentFinding);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var service = new AnalysisReportService(new AnalysisReportDataSource(harness.Context));
        var report = await service.BuildAsync(
            project.Id,
            baselineScan.Id,
            new AnalysisReportOptions { IncludeGitContext = false });

        Assert.Equal(project.Id, report.Project.ProjectId);
        Assert.Equal(baselineScan.Id, report.Scan.ScanId);
        Assert.Single(report.SourceFiles);
        Assert.Single(report.Symbols);
        Assert.Single(report.Findings);
        Assert.Equal("FD001", report.Findings[0].RuleId);
        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == "FD002");

        var comparison = await new AnalysisComparisonService(service)
            .CompareAsync(project.Id, baselineScan.Id, currentScan.Id);
        Assert.Equal(1, comparison.NewCount);
        Assert.Equal(1, comparison.RemovedCount);
        Assert.Equal(0, comparison.UnchangedCount);

        var jsonPath = Path.Combine(projectRoot.Path, "persisted.json");
        var jsonExport = await service.ExportAsync(
            project.Id,
            baselineScan.Id,
            AnalysisReportFormat.Json,
            jsonPath,
            new AnalysisReportOptions { IncludeGitContext = false });
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        Assert.Equal(jsonExport.BytesWritten, new FileInfo(jsonPath).Length);
        Assert.Equal(baselineScan.Id.ToString(), json.RootElement.GetProperty("scan").GetProperty("scanId").GetString());

        var htmlPath = Path.Combine(projectRoot.Path, "persisted.html");
        await service.ExportAsync(
            project.Id,
            baselineScan.Id,
            AnalysisReportFormat.Html,
            htmlPath,
            new AnalysisReportOptions { IncludeGitContext = false });
        var html = await File.ReadAllTextAsync(htmlPath);
        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FD001", html, StringComparison.Ordinal);
        Assert.DoesNotContain(projectRoot.Path, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SQLite_report_adapter_returns_null_for_wrong_project_and_bounds_rows_honestly()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var firstRoot = new TemporaryDirectory();
        using var secondRoot = new TemporaryDirectory();
        var firstProject = Project.Create("First", firstRoot.Path);
        var secondProject = Project.Create("Second", secondRoot.Path);
        var scan = CreateCompletedScan(firstProject.Id, "test");
        var file = SourceFile.Create(scan.Id, "Program.cs", "Program.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#");
        var finding = Finding.Create(scan.Id, "FD001", FindingSeverity.Warning, "one", file.Id, null, 1, 1);
        scan.StartAnalysis();
        scan.CompleteAnalysis(1, 0, 0, 1);
        await harness.Context.AddRangeAsync(firstProject, secondProject, scan, file, finding);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var adapter = new AnalysisReportDataSource(harness.Context);
        Assert.Null(await adapter.GetAsync(secondProject.Id, scan.Id, new AnalysisReportQueryOptions()));
        var data = await adapter.GetAsync(
            firstProject.Id,
            scan.Id,
            new AnalysisReportQueryOptions { MaxFindings = 0 });

        Assert.NotNull(data);
        Assert.Empty(data!.Findings);
        Assert.Equal(1, data.FindingCount);
        Assert.True(data.FindingsTruncated);
    }

    [Fact]
    public async Task Report_services_are_registered_without_presentation_dependencies()
    {
        using var directory = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddForgeDeskInfrastructure(options => options.DatabasePath = Path.Combine(directory.Path, "reports.db"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAnalysisReportDataSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAnalysisReportService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAnalysisComparisonService>());
    }

    private static Scan CreateCompletedScan(Guid projectId, string toolVersion)
    {
        var scan = Scan.CreatePending(projectId, toolVersion);
        scan.Start();
        scan.Complete(1, 1);
        return scan;
    }
}
