using ForgeDesk.Domain;
using ForgeDesk.Graph;
using ForgeDesk.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.IntegrationTests;

public sealed class GraphQueryIntegrationTests
{
    [Fact]
    public async Task Infrastructure_di_resolves_the_graph_application_boundary()
    {
        var directory = Directory.CreateTempSubdirectory("forgedesk-graph-di-");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddForgeDeskInfrastructure(options => options.DatabasePath = Path.Combine(directory.FullName, "graph.db"));
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IGraphQueryRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IGraphDataProvider>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ForgeDesk.Application.IGraphQueryService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ForgeDesk.Application.IGraphService>());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Repository_loads_only_the_selected_scan_as_no_tracking_graph_data()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        var projectOne = Project.Create("One", Directory.CreateTempSubdirectory("forgedesk-graph-one-").FullName);
        var projectTwo = Project.Create("Two", Directory.CreateTempSubdirectory("forgedesk-graph-two-").FullName);
        var scanOne = CreateCompletedScan(projectOne.Id, symbolCount: 1, dependencyCount: 1);
        var scanTwo = CreateCompletedScan(projectTwo.Id, symbolCount: 1, dependencyCount: 0);
        var fileOne = CreateSourceFile(scanOne.Id, "One.cs");
        var fileTwo = CreateSourceFile(scanTwo.Id, "Two.cs");
        var one = Symbol.Create(scanOne.Id, fileOne.Id, $"scan:{scanOne.Id:N}/one", "Shared", SymbolKind.Class, 1, 3);
        var two = Symbol.Create(scanTwo.Id, fileTwo.Id, $"scan:{scanTwo.Id:N}/two", "Shared", SymbolKind.Class, 1, 3);
        var dependency = Dependency.Create(scanOne.Id, one.Id, one.Id, nameof(DependencyKind.Calls), isResolved: true);

        await harness.Context.AddRangeAsync(projectOne, projectTwo, scanOne, scanTwo, fileOne, fileTwo, one, two, dependency);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var repository = new GraphQueryRepository(harness.Context);
        var snapshot = await repository.GetSnapshotAsync(scanOne.Id);

        Assert.NotNull(snapshot);
        Assert.Equal(scanOne.Id, snapshot!.ScanId);
        Assert.Equal(projectOne.Id, snapshot.Project!.Id);
        Assert.Single(snapshot.Symbols);
        Assert.Single(snapshot.Dependencies);
        Assert.Empty(harness.Context.ChangeTracker.Entries());
        Assert.DoesNotContain(snapshot.Symbols, symbol => symbol.ScanId == scanTwo.Id);
    }

    [Fact]
    public async Task Provider_builds_real_sqlite_graph_and_reports_stale_partial_and_missing_states()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        var project = Project.Create("Graph", Directory.CreateTempSubdirectory("forgedesk-graph-").FullName);
        var complete = CreateCompletedScan(project.Id, symbolCount: 1, dependencyCount: 0);
        var partial = CreateCompletedScan(project.Id, symbolCount: 1, dependencyCount: 0);
        partial.StartAnalysis();
        var completeFile = CreateSourceFile(complete.Id, "Complete.cs");
        var partialFile = CreateSourceFile(partial.Id, "Partial.cs");
        var completeSymbol = Symbol.Create(complete.Id, completeFile.Id, "complete", "Complete", SymbolKind.Class, 1, 2);
        var partialSymbol = Symbol.Create(partial.Id, partialFile.Id, "partial", "Partial", SymbolKind.Class, 1, 2);

        await harness.Context.AddRangeAsync(project, complete, partial, completeFile, partialFile, completeSymbol, partialSymbol);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var provider = new GraphDataProvider(new GraphQueryRepository(harness.Context));
        var ready = await provider.GetAsync(new GraphQueryOptions(complete.Id));
        var partialData = await provider.GetAsync(new GraphQueryOptions(partial.Id));
        var missing = await provider.GetAsync(new GraphQueryOptions(Guid.NewGuid()));

        Assert.Equal(GraphDataState.Ready, ready.State);
        Assert.Single(ready.Nodes);
        Assert.Equal("Complete", ready.Nodes[0].Label);
        Assert.Equal(GraphDataState.Partial, partialData.State);
        Assert.True(partialData.IsPartial);
        Assert.Equal(GraphDataState.Missing, missing.State);
    }

    private static Scan CreateCompletedScan(Guid projectId, int symbolCount, int dependencyCount)
    {
        var scan = Scan.CreatePending(projectId, "integration");
        scan.Start();
        scan.Complete(1, 1);
        scan.StartAnalysis();
        scan.CompleteAnalysis(1, symbolCount, dependencyCount, 0);
        return scan;
    }

    private static SourceFile CreateSourceFile(Guid scanId, string fileName)
        => SourceFile.Create(
            scanId,
            fileName,
            fileName,
            ".cs",
            12,
            DateTimeOffset.UtcNow,
            "C#");
}
