using ForgeDesk.Analysis;
using ForgeDesk.Application;
using ForgeDesk.Domain;
using ForgeDesk.Infrastructure;
using Microsoft.EntityFrameworkCore;

using ApplicationAnalysisEngine = ForgeDesk.Application.IAnalysisEngine;
using ApplicationAnalysisFileInput = ForgeDesk.Application.AnalysisFileInput;
using ApplicationAnalysisRequest = ForgeDesk.Application.AnalysisRequest;
using ApplicationAnalysisResult = ForgeDesk.Application.AnalysisResult;

namespace ForgeDesk.IntegrationTests;

public sealed class AnalysisPersistenceTests
{
    [Fact]
    public async Task Roslyn_analysis_extracts_symbols_dependencies_findings_and_is_idempotent()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var projectRoot = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(projectRoot.Path, "Demo.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(projectRoot.Path, "Contracts.cs"),
            "namespace Demo; public interface IContract { void Execute(); } public class Base { public virtual void Execute() { } }");
        File.WriteAllText(
            Path.Combine(projectRoot.Path, "Derived.cs"),
            "namespace Demo; public sealed class Derived : Base, IContract { // TODO: replace this implementation\n public override void Execute() { Helper(); } private void Helper() { } }");

        var projectRepository = new ProjectRepository(harness.Context);
        var discovery = new FileDiscoveryService();
        var onboarding = new ProjectOnboardingService(
            projectRepository,
            new ProjectValidationService(discovery),
            discovery,
            new ProjectOnboardingStore(harness.Context),
            new FixedClock(DateTimeOffset.UtcNow));
        var onboarded = await onboarding.OnboardAsync(new ProjectOnboardingRequest("Analysis fixture", projectRoot.Path));

        var progress = new List<AnalysisProgress>();
        var service = new AnalysisService(
            new AnalysisRepository(harness.Context),
            new RoslynAnalysisEngine(),
            new FixedClock(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero)));

        var first = await service.AnalyzeAsync(
            onboarded.Scan.Id,
            new AnalysisQualityOptions(MaxMethodLines: 1),
            new Progress<AnalysisProgress>(progress.Add));

        Assert.Equal(AnalysisStatus.Completed, first.Status);
        Assert.Equal(2, first.FilesProcessed);
        Assert.True(first.SymbolCount >= 7);
        var relationships = await harness.Context.Dependencies
            .Where(item => item.ScanId == onboarded.Scan.Id)
            .Select(item => item.RelationshipType)
            .ToListAsync();
        Assert.Contains(relationships, item => item is "Inherits" or "Implements" or "Calls");
        var findingRuleIds = await harness.Context.Findings
            .Where(item => item.ScanId == onboarded.Scan.Id)
            .Select(item => item.RuleId)
            .ToListAsync();
        Assert.Contains("FD001", findingRuleIds);
        Assert.Contains(progress, item => item.Phase == "Completed");

        var firstCounts = (
            Symbols: await harness.Context.Symbols.CountAsync(item => item.ScanId == onboarded.Scan.Id),
            Dependencies: await harness.Context.Dependencies.CountAsync(item => item.ScanId == onboarded.Scan.Id),
            Findings: await harness.Context.Findings.CountAsync(item => item.ScanId == onboarded.Scan.Id));
        var firstSymbolKeys = await harness.Context.Symbols
            .Where(item => item.ScanId == onboarded.Scan.Id)
            .Select(item => item.SymbolKey)
            .OrderBy(item => item)
            .ToListAsync();

        var second = await service.AnalyzeAsync(
            onboarded.Scan.Id,
            new AnalysisQualityOptions(MaxMethodLines: 1));

        Assert.Equal(AnalysisStatus.Completed, second.Status);
        Assert.Equal(firstCounts.Symbols, await harness.Context.Symbols.CountAsync(item => item.ScanId == onboarded.Scan.Id));
        Assert.Equal(firstCounts.Dependencies, await harness.Context.Dependencies.CountAsync(item => item.ScanId == onboarded.Scan.Id));
        Assert.Equal(firstCounts.Findings, await harness.Context.Findings.CountAsync(item => item.ScanId == onboarded.Scan.Id));
        Assert.Equal(first.SymbolCount, second.SymbolCount);
        Assert.Equal(firstSymbolKeys, await harness.Context.Symbols
            .Where(item => item.ScanId == onboarded.Scan.Id)
            .Select(item => item.SymbolKey)
            .OrderBy(item => item)
            .ToListAsync());
    }

    [Fact]
    public async Task Analysis_cancellation_is_persisted_without_claiming_completion()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var projectRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(projectRoot.Path, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(projectRoot.Path, "Program.cs"), "class Program { static void Main() { } }");

        var discovery = new FileDiscoveryService();
        var onboarding = new ProjectOnboardingService(
            new ProjectRepository(harness.Context),
            new ProjectValidationService(discovery),
            discovery,
            new ProjectOnboardingStore(harness.Context));
        var onboarded = await onboarding.OnboardAsync(new ProjectOnboardingRequest("Cancellation fixture", projectRoot.Path));

        var repository = new AnalysisRepository(harness.Context);
        await new AnalysisService(
            repository,
            new RoslynAnalysisEngine(),
            new FixedClock(DateTimeOffset.UtcNow)).AnalyzeAsync(onboarded.Scan.Id);
        Assert.NotEmpty(await harness.Context.Symbols.Where(item => item.ScanId == onboarded.Scan.Id).ToListAsync());

        using var cancellation = new CancellationTokenSource();
        var service = new AnalysisService(
            repository,
            new CancellingEngine(cancellation),
            new FixedClock(DateTimeOffset.UtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync(
            onboarded.Scan.Id,
            cancellationToken: cancellation.Token));

        var stored = await harness.Context.Scans.SingleAsync(item => item.Id == onboarded.Scan.Id);
        Assert.Equal(AnalysisStatus.Cancelled, stored.AnalysisStatus);
        Assert.NotEqual(ScanStatus.Failed, stored.Status);
        Assert.Equal(0, await harness.Context.Symbols.CountAsync(item => item.ScanId == onboarded.Scan.Id));
        Assert.Equal(0, await harness.Context.Dependencies.CountAsync(item => item.ScanId == onboarded.Scan.Id));
        Assert.Equal(0, await harness.Context.Findings.CountAsync(item => item.ScanId == onboarded.Scan.Id));
    }

    [Fact]
    public async Task Syntax_fallback_reports_quality_rules_and_rule_selection_is_deterministic()
    {
        using var projectRoot = new TemporaryDirectory();
        var sourceFileId = Guid.NewGuid();
        var partialSourceFileId = Guid.NewGuid();
        var request = new ApplicationAnalysisRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            projectRoot.Path,
            [
                new ApplicationAnalysisFileInput(
                    sourceFileId,
                    "Rules.cs",
                    Path.Combine(projectRoot.Path, "Rules.cs"),
                    """
                    namespace Demo;
                    public partial class Outer<T>
                    {
                        // FIXME: replace this implementation
                        public void Overload(int value) { }
                        public void Overload(string value) { }
                        public void TooMany(int one, int two, int three, int four, int five, int six)
                        {
                            try { }
                            catch { }
                        }
                    }
                    """),
                new ApplicationAnalysisFileInput(
                    partialSourceFileId,
                    "Partial.cs",
                    Path.Combine(projectRoot.Path, "Partial.cs"),
                    "namespace Demo; public partial class Outer<T> { public class Inner { } }")
            ],
            new AnalysisQualityOptions(MaxMethodLines: 2, MaxParameters: 5));

        var engine = new RoslynAnalysisEngine();
        var first = await engine.AnalyzeAsync(request);
        var second = await engine.AnalyzeAsync(request);

        Assert.Equal(AnalysisBindingMode.SyntaxOnlyFallback, first.BindingMode);
        Assert.Equal(first.Symbols.Select(symbol => symbol.SymbolKey), second.Symbols.Select(symbol => symbol.SymbolKey));
        Assert.Equal(first.Dependencies.ToArray(), second.Dependencies.ToArray());
        Assert.Equal(first.Findings.ToArray(), second.Findings.ToArray());
        Assert.Contains(first.Findings, finding => finding.RuleId == AnalysisRuleIds.TodoOrFixme);
        Assert.Contains(first.Findings, finding => finding.RuleId == AnalysisRuleIds.LongMethod);
        Assert.Contains(first.Findings, finding => finding.RuleId == AnalysisRuleIds.ExcessiveParameters);
        Assert.Contains(first.Findings, finding => finding.RuleId == AnalysisRuleIds.EmptyCatch);
        Assert.Equal(
            first.Symbols.Count,
            first.Symbols.Select(symbol => symbol.SymbolKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, first.Symbols.Count(symbol => symbol.Name == "Overload"));
        Assert.Contains(first.Symbols, symbol => symbol.Name == "Inner");

        var enabledOnly = await engine.AnalyzeAsync(
            request with
            {
                QualityOptions = new AnalysisQualityOptions(
                    MaxMethodLines: 2,
                    MaxParameters: 5,
                    EnabledRuleIds: new HashSet<string>([AnalysisRuleIds.ExcessiveParameters], StringComparer.Ordinal))
            });

        Assert.NotEmpty(enabledOnly.Findings);
        Assert.All(enabledOnly.Findings, finding => Assert.Equal(AnalysisRuleIds.ExcessiveParameters, finding.RuleId));
    }

    [Fact]
    public async Task Failed_retry_clears_previous_results_before_persisting_failure()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var projectRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(projectRoot.Path, "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var sourcePath = Path.Combine(projectRoot.Path, "Program.cs");
        File.WriteAllText(sourcePath, "namespace Demo; public class Program { }");

        var discovery = new FileDiscoveryService();
        var onboarding = new ProjectOnboardingService(
            new ProjectRepository(harness.Context),
            new ProjectValidationService(discovery),
            discovery,
            new ProjectOnboardingStore(harness.Context));
        var onboarded = await onboarding.OnboardAsync(new ProjectOnboardingRequest("Retry fixture", projectRoot.Path));
        var service = new AnalysisService(
            new AnalysisRepository(harness.Context),
            new RoslynAnalysisEngine(),
            new FixedClock(DateTimeOffset.UtcNow));

        await service.AnalyzeAsync(onboarded.Scan.Id);
        Assert.NotEqual(0, await harness.Context.Symbols.CountAsync(item => item.ScanId == onboarded.Scan.Id));

        File.Delete(sourcePath);
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.AnalyzeAsync(onboarded.Scan.Id));

        var stored = await harness.Context.Scans.SingleAsync(item => item.Id == onboarded.Scan.Id);
        Assert.Equal(AnalysisStatus.Failed, stored.AnalysisStatus);
        Assert.Equal(0, await harness.Context.Symbols.CountAsync(item => item.ScanId == onboarded.Scan.Id));
        Assert.Equal(0, await harness.Context.Dependencies.CountAsync(item => item.ScanId == onboarded.Scan.Id));
        Assert.Equal(0, await harness.Context.Findings.CountAsync(item => item.ScanId == onboarded.Scan.Id));
    }

    private sealed class CancellingEngine : ApplicationAnalysisEngine
    {
        private readonly CancellationTokenSource _source;

        public CancellingEngine(CancellationTokenSource source) => _source = source;

        public Task<ApplicationAnalysisResult> AnalyzeAsync(ApplicationAnalysisRequest request, CancellationToken cancellationToken = default)
            => AnalyzeAsync(request, null, cancellationToken);

        public Task<ApplicationAnalysisResult> AnalyzeAsync(
            ApplicationAnalysisRequest request,
            IProgress<AnalysisProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            _source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ApplicationAnalysisResult([], [], []));
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
