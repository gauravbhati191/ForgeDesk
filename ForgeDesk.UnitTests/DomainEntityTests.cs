using ForgeDesk.Domain;

namespace ForgeDesk.UnitTests;

public sealed class DomainEntityTests
{
    [Fact]
    public void Project_contains_required_identity_and_timestamp_fields()
    {
        using var directory = new TemporaryDirectory();
        var created = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var project = Project.Create("  ForgeDesk  ", directory.Path + Path.DirectorySeparatorChar + ".", ProjectType.CSharpProject, created);

        Assert.Equal("ForgeDesk", project.Name);
        Assert.Equal(ProjectType.CSharpProject, project.ProjectType);
        Assert.Equal(PathNormalization.Normalize(directory.Path), project.NormalizedRootPath);
        Assert.Equal(created, project.CreatedAtUtc);
        Assert.Throws<DomainInvariantException>(() => Project.Create("", directory.Path));
    }

    [Fact]
    public void Scan_allows_only_forward_lifecycle_transitions_and_accurate_counts()
    {
        var queuedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var scan = Scan.CreatePending(Guid.NewGuid(), "test", queuedAt);

        scan.Start(queuedAt.AddMinutes(1));
        scan.RecordDiscovered(3);
        scan.RecordProcessed(3);
        scan.Complete(3, 3, queuedAt.AddMinutes(2));

        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Equal(3, scan.FilesDiscovered);
        Assert.Equal(3, scan.FilesProcessed);
        Assert.Throws<DomainInvariantException>(() => scan.Start());
    }

    [Fact]
    public void Source_file_requires_relative_paths_and_preserves_discovery_metadata()
    {
        var scanId = Guid.NewGuid();
        Assert.Throws<DomainInvariantException>(() => SourceFile.Create(scanId, "../outside.cs", "outside.cs", ".cs", 1, DateTimeOffset.UtcNow, "C#"));

        var file = SourceFile.Create(scanId, "src/Main.cs", "Main.cs", ".cs", 42, DateTimeOffset.UtcNow, "C#");
        Assert.Equal("src/Main.cs", file.RelativePath);
        Assert.Equal(42, file.FileSizeBytes);
        Assert.Equal(DiscoveryStatus.Discovered, file.DiscoveryStatus);
    }

    [Fact]
    public void Future_symbol_and_dependency_entities_require_explicit_identity()
    {
        var sourceFileId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        var symbol = Symbol.Create(scanId, sourceFileId, "T:Demo.Widget", "Widget", SymbolKind.Class, 4, 10);
        var dependency = Dependency.Create(scanId, symbol.Id, null, "inherits");

        Assert.Equal(SymbolKind.Class, symbol.Kind);
        Assert.Equal(symbol.Id, dependency.SourceSymbolId);
        Assert.False(dependency.IsResolved);
    }

    [Fact]
    public void Analysis_lifecycle_is_separate_from_discovery_and_supports_retry()
    {
        var queued = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var scan = Scan.CreatePending(Guid.NewGuid(), "test", queued);
        scan.Start(queued.AddMinutes(1));
        scan.Complete(2, 2, queued.AddMinutes(2));

        scan.StartAnalysis(queued.AddMinutes(3));
        scan.RecordAnalysisProgress(1);
        scan.CompleteAnalysis(2, 4, 3, 1, queued.AddMinutes(4));

        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Equal(AnalysisStatus.Completed, scan.AnalysisStatus);
        Assert.Equal(4, scan.AnalysisSymbolCount);
        Assert.Equal(3, scan.AnalysisDependencyCount);
        Assert.Equal(1, scan.FindingCount);

        scan.StartAnalysis(queued.AddMinutes(5));
        Assert.Equal(AnalysisStatus.Running, scan.AnalysisStatus);
        Assert.Equal(0, scan.AnalysisSymbolCount);
        Assert.Throws<DomainInvariantException>(() => Scan.CreatePending(Guid.NewGuid(), "test").StartAnalysis());
    }
}
