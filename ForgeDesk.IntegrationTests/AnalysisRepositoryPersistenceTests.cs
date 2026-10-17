using ForgeDesk.Domain;
using ForgeDesk.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.IntegrationTests;

public sealed class AnalysisRepositoryPersistenceTests
{
    [Fact]
    public async Task ReplaceResults_persists_new_rows_and_detaches_deleted_rows_from_the_tracker()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var projectRoot = new TemporaryDirectory();
        var fixture = await CreateFixtureAsync(harness, projectRoot.Path);
        var oldResults = CreateResults(fixture.Scan.Id, fixture.SourceFile.Id, "Old", 2);
        await harness.Context.AddRangeAsync(oldResults.Symbols);
        await harness.Context.AddRangeAsync(oldResults.Dependencies);
        await harness.Context.AddRangeAsync(oldResults.Findings);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        await harness.Context.Symbols.SingleAsync(item => item.Id == oldResults.Symbols[0].Id);
        await harness.Context.Dependencies.SingleAsync(item => item.Id == oldResults.Dependencies[0].Id);
        await harness.Context.Findings.SingleAsync(item => item.Id == oldResults.Findings[0].Id);

        var newResults = CreateResults(fixture.Scan.Id, fixture.SourceFile.Id, "New", 3);
        await new AnalysisRepository(harness.Context).ReplaceResultsAsync(
            fixture.Scan.Id,
            newResults.Symbols,
            newResults.Dependencies,
            newResults.Findings);

        Assert.Equal(3, await harness.Context.Symbols.CountAsync(item => item.ScanId == fixture.Scan.Id));
        Assert.Equal(2, await harness.Context.Dependencies.CountAsync(item => item.ScanId == fixture.Scan.Id));
        Assert.Equal(3, await harness.Context.Findings.CountAsync(item => item.ScanId == fixture.Scan.Id));
        Assert.Equal(
            newResults.Symbols.Select(item => item.SymbolKey),
            await harness.Context.Symbols
                .Where(item => item.ScanId == fixture.Scan.Id)
                .OrderBy(item => item.SymbolKey)
                .Select(item => item.SymbolKey)
                .ToListAsync());
        Assert.DoesNotContain(
            harness.Context.ChangeTracker.Entries<Symbol>(),
            entry => oldResults.Symbols.Any(item => item.Id == entry.Entity.Id));
        Assert.DoesNotContain(
            harness.Context.ChangeTracker.Entries<Dependency>(),
            entry => oldResults.Dependencies.Any(item => item.Id == entry.Entity.Id));
        Assert.DoesNotContain(
            harness.Context.ChangeTracker.Entries<Finding>(),
            entry => oldResults.Findings.Any(item => item.Id == entry.Entity.Id));
    }

    [Fact]
    public async Task ReplaceResults_rolls_back_bulk_deletes_when_new_rows_fail_persistence()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var projectRoot = new TemporaryDirectory();
        var fixture = await CreateFixtureAsync(harness, projectRoot.Path);
        var oldResults = CreateResults(fixture.Scan.Id, fixture.SourceFile.Id, "Old", 2);
        await harness.Context.AddRangeAsync(oldResults.Symbols);
        await harness.Context.AddRangeAsync(oldResults.Dependencies);
        await harness.Context.AddRangeAsync(oldResults.Findings);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var duplicateSymbols = new[]
        {
            Symbol.Create(fixture.Scan.Id, fixture.SourceFile.Id, "T:Duplicate", "DuplicateA", SymbolKind.Class),
            Symbol.Create(fixture.Scan.Id, fixture.SourceFile.Id, "T:Duplicate", "DuplicateB", SymbolKind.Class)
        };

        await Assert.ThrowsAsync<DbUpdateException>(() => new AnalysisRepository(harness.Context).ReplaceResultsAsync(
            fixture.Scan.Id,
            duplicateSymbols,
            [],
            []));

        harness.Context.ChangeTracker.Clear();
        Assert.Equal(2, await harness.Context.Symbols.CountAsync(item => item.ScanId == fixture.Scan.Id));
        Assert.Equal(1, await harness.Context.Dependencies.CountAsync(item => item.ScanId == fixture.Scan.Id));
        Assert.Equal(2, await harness.Context.Findings.CountAsync(item => item.ScanId == fixture.Scan.Id));
        Assert.Contains(
            oldResults.Symbols[0].SymbolKey,
            await harness.Context.Symbols
                .Where(item => item.ScanId == fixture.Scan.Id)
                .Select(item => item.SymbolKey)
                .ToListAsync());
    }

    [Fact]
    public async Task ClearResults_persists_removal_of_dependency_finding_and_symbol_rows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var projectRoot = new TemporaryDirectory();
        var fixture = await CreateFixtureAsync(harness, projectRoot.Path);
        var results = CreateResults(fixture.Scan.Id, fixture.SourceFile.Id, "Stored", 4);
        await harness.Context.AddRangeAsync(results.Symbols);
        await harness.Context.AddRangeAsync(results.Dependencies);
        await harness.Context.AddRangeAsync(results.Findings);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();
        await harness.Context.Symbols.ToListAsync();
        await harness.Context.Dependencies.ToListAsync();
        await harness.Context.Findings.ToListAsync();

        await new AnalysisRepository(harness.Context).ClearResultsAsync(fixture.Scan.Id);

        Assert.Equal(0, await harness.Context.Symbols.CountAsync(item => item.ScanId == fixture.Scan.Id));
        Assert.Equal(0, await harness.Context.Dependencies.CountAsync(item => item.ScanId == fixture.Scan.Id));
        Assert.Equal(0, await harness.Context.Findings.CountAsync(item => item.ScanId == fixture.Scan.Id));
        Assert.Empty(harness.Context.ChangeTracker.Entries<Symbol>());
        Assert.Empty(harness.Context.ChangeTracker.Entries<Dependency>());
        Assert.Empty(harness.Context.ChangeTracker.Entries<Finding>());
    }

    private static async Task<Fixture> CreateFixtureAsync(SqliteTestHarness harness, string projectRoot)
    {
        var project = Project.Create("Analysis repository", projectRoot);
        var scan = Scan.CreatePending(project.Id, "integration");
        scan.Start();
        scan.Complete(1, 1);
        var sourceFile = SourceFile.Create(
            scan.Id,
            "Program.cs",
            "Program.cs",
            ".cs",
            1,
            DateTimeOffset.UnixEpoch,
            "C#");
        await harness.Context.AddRangeAsync(project, scan, sourceFile);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();
        return new Fixture(scan, sourceFile);
    }

    private static ResultSet CreateResults(Guid scanId, Guid sourceFileId, string prefix, int symbolCount)
    {
        var symbols = Enumerable.Range(0, symbolCount)
            .Select(index => Symbol.Create(
                scanId,
                sourceFileId,
                $"T:{prefix}.Symbol{index}",
                $"{prefix}Symbol{index}",
                SymbolKind.Class,
                index + 1,
                index + 1))
            .ToArray();
        var dependencies = Enumerable.Range(0, symbolCount - 1)
            .Select(index => Dependency.Create(scanId, symbols[index].Id, symbols[index + 1].Id, "Calls", true))
            .ToArray();
        var findings = symbols
            .Select((symbol, index) => Finding.Create(
                scanId,
                "FD001",
                FindingSeverity.Warning,
                $"{prefix} finding {index}",
                sourceFileId,
                symbol.Id,
                index + 1,
                index + 1,
                DateTimeOffset.UnixEpoch))
            .ToArray();
        return new ResultSet(symbols, dependencies, findings);
    }

    private sealed record Fixture(Scan Scan, SourceFile SourceFile);

    private sealed record ResultSet(
        Symbol[] Symbols,
        Dependency[] Dependencies,
        Finding[] Findings);
}
