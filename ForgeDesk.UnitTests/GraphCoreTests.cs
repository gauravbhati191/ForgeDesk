using ForgeDesk.Domain;
using ForgeDesk.Application;
using ForgeDesk.Graph;

namespace ForgeDesk.UnitTests;

public sealed class GraphCoreTests
{
    [Fact]
    public void Builder_projects_real_symbol_and_dependency_metadata_with_stable_ids()
    {
        var fixture = GraphFixture.Create();
        var builder = new GraphDataBuilder();

        var first = builder.Build(fixture.Snapshot, new GraphQueryOptions(fixture.Scan.Id));
        var second = builder.Build(fixture.Snapshot, new GraphQueryOptions(fixture.Scan.Id));

        Assert.Equal(GraphDataState.Ready, first.State);
        Assert.Equal(first.Nodes.Select(node => node.Id), second.Nodes.Select(node => node.Id));
        Assert.Equal(first.Edges.Select(edge => (edge.FromId, edge.ToId, edge.RelationshipType)), second.Edges.Select(edge => (edge.FromId, edge.ToId, edge.RelationshipType)));
        Assert.Equal(fixture.Class.Id.ToString("D"), first.Nodes.Single(node => node.Label == "Alpha").Id);
        Assert.Equal("src/Alpha.cs", first.Nodes.Single(node => node.Label == "Alpha").RelativePath);
        Assert.Equal(SymbolKind.Class, first.Nodes.Single(node => node.Label == "Alpha").Kind);
        Assert.Equal(1, first.Nodes.Single(node => node.Label == "Alpha").RelatedFindingCount);
        Assert.Equal(DependencyKind.Calls, first.Edges.Single().Kind);
        Assert.Equal(2, first.TotalNodeCount);
        Assert.Equal(1, first.VisibleEdgeCount);
    }

    [Fact]
    public void Builder_collapses_duplicates_excludes_self_edges_and_can_opt_in_to_self_edges()
    {
        var fixture = GraphFixture.Create(includeDuplicateAndSelfEdges: true);
        var builder = new GraphDataBuilder();

        var collapsed = builder.Build(fixture.Snapshot, new GraphQueryOptions(fixture.Scan.Id));
        var included = builder.Build(
            fixture.Snapshot,
            new GraphQueryOptions(
                fixture.Scan.Id,
                new GraphFilterOptions
                {
                    SelfEdgePolicy = GraphSelfEdgePolicy.Include
                }));

        Assert.Single(collapsed.Edges);
        Assert.Equal(1, collapsed.DuplicateEdgeCount);
        Assert.Equal(1, collapsed.SelfEdgeCount);
        Assert.Equal(2, included.Edges.Count);
        Assert.Contains(included.Edges, edge => edge.IsSelfEdge);
    }

    [Fact]
    public void Filters_support_search_kind_relationship_direction_depth_and_isolated_nodes()
    {
        var fixture = GraphFixture.Create(includeThirdNode: true);
        var builder = new GraphDataBuilder();
        var selected = fixture.Class.Id.ToString("D");

        var depthOne = builder.Build(
            fixture.Snapshot,
            new GraphQueryOptions(
                fixture.Scan.Id,
                new GraphFilterOptions
                {
                    Direction = GraphDirection.Outgoing,
                    NeighborhoodDepth = 1,
                    IncludeIsolatedNodes = false
                },
                new GraphSelection(selected)));
        var search = builder.Build(
            fixture.Snapshot,
            new GraphQueryOptions
            {
                ScanId = fixture.Scan.Id,
                Search = "beta",
                SymbolKinds = [SymbolKind.Method]
            });
        var depthTwo = builder.Build(
            fixture.Snapshot,
            new GraphQueryOptions(
                fixture.Scan.Id,
                new GraphFilterOptions
                {
                    Direction = GraphDirection.Outgoing,
                    NeighborhoodDepth = 2
                },
                new GraphSelection(selected)));
        var relationship = builder.Build(
            fixture.Snapshot,
            new GraphQueryOptions
            {
                ScanId = fixture.Scan.Id,
                RelationshipTypes = [nameof(DependencyKind.Calls)]
            });

        Assert.Equal(2, depthOne.VisibleNodeCount);
        Assert.DoesNotContain(depthOne.Nodes, node => node.Label == "Gamma");
        Assert.Equal(selected, depthOne.SelectedNodeId);
        Assert.True(depthOne.Nodes.Single(node => node.Label == "Alpha").IsSelected);
        Assert.True(depthOne.Nodes.Single(node => node.Label == "Beta").IsNeighbor);
        Assert.All(depthOne.Edges, edge => Assert.True(edge.IsOutgoing));
        Assert.Equal(3, depthTwo.VisibleNodeCount);
        Assert.Single(search.Nodes);
        Assert.Equal("Beta", search.Nodes[0].Label);
        Assert.All(relationship.Edges, edge => Assert.Equal(DependencyKind.Calls, edge.Kind));
    }

    [Fact]
    public void Layout_is_deterministic_and_honors_cancellation()
    {
        var fixture = GraphFixture.Create(includeThirdNode: true);
        var graph = new GraphDataBuilder().Build(fixture.Snapshot, new GraphQueryOptions(fixture.Scan.Id));
        var layout = new DeterministicGraphLayoutService();

        var first = layout.Layout(graph);
        var second = layout.Layout(graph);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Equal(first.Nodes.Select(node => node.Position), second.Nodes.Select(node => node.Position));
        Assert.Throws<OperationCanceledException>(() => layout.Layout(graph, cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task Provider_honors_cancellation_before_loading()
    {
        var fixture = GraphFixture.Create();
        var provider = new GraphDataProvider(new InMemoryGraphRepository(fixture.Snapshot));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetAsync(
            new GraphQueryOptions(fixture.Scan.Id),
            cancelled.Token));
    }

    [Fact]
    public async Task Application_graph_service_marks_an_older_response_as_stale()
    {
        var slowScan = Guid.NewGuid();
        var fastScan = Guid.NewGuid();
        var provider = new VersionedGraphProvider(slowScan);
        var service = new GraphQueryService(provider);

        var older = service.QueryAsync(new GraphQueryRequest(slowScan, requestVersion: 1));
        var current = await service.QueryAsync(new GraphQueryRequest(fastScan, requestVersion: 2));
        provider.ReleaseSlowQuery();
        var stale = await older;

        Assert.True(stale.IsStaleRequest);
        Assert.False(current.IsStaleRequest);
        Assert.Equal(2, current.RequestVersion);
    }

    private sealed class InMemoryGraphRepository : IGraphQueryRepository
    {
        private readonly GraphQuerySnapshot _snapshot;

        public InMemoryGraphRepository(GraphQuerySnapshot snapshot) => _snapshot = snapshot;

        public Task<GraphQuerySnapshot?> GetSnapshotAsync(Guid scanId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<GraphQuerySnapshot?>(_snapshot.ScanId == scanId ? _snapshot : null);
        }
    }

    private sealed class VersionedGraphProvider : IGraphDataProvider
    {
        private readonly Guid _slowScan;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public VersionedGraphProvider(Guid slowScan) => _slowScan = slowScan;

        public async Task<GraphData> GetAsync(GraphQueryOptions options, CancellationToken cancellationToken = default)
        {
            if (options.ScanId == _slowScan)
            {
                await _release.Task.WaitAsync(cancellationToken);
            }

            return new GraphData(Array.Empty<GraphNode>(), Array.Empty<GraphEdge>());
        }

        public void ReleaseSlowQuery() => _release.TrySetResult();
    }

    private sealed class GraphFixture
    {
        private GraphFixture(
            Project project,
            Scan scan,
            SourceFile sourceFile,
            Symbol @class,
            Symbol method,
            Symbol? third,
            GraphQuerySnapshot snapshot)
        {
            Project = project;
            Scan = scan;
            SourceFile = sourceFile;
            Class = @class;
            Method = method;
            Third = third;
            Snapshot = snapshot;
        }

        public Project Project { get; }
        public Scan Scan { get; }
        public SourceFile SourceFile { get; }
        public Symbol Class { get; }
        public Symbol Method { get; }
        public Symbol? Third { get; }
        public GraphQuerySnapshot Snapshot { get; }

        public static GraphFixture Create(
            bool includeDuplicateAndSelfEdges = false,
            bool includeThirdNode = false)
        {
            using var directory = new TemporaryDirectory();
            var project = Project.Create("Graph", directory.Path);
            var scan = Scan.CreatePending(project.Id, "test");
            scan.Start();
            scan.Complete(2, 2);
            var sourceFile = SourceFile.Create(
                scan.Id,
                "src/Alpha.cs",
                "Alpha.cs",
                ".cs",
                100,
                DateTimeOffset.UtcNow,
                "C#");
            var betaFile = SourceFile.Create(
                scan.Id,
                "src/Beta.cs",
                "Beta.cs",
                ".cs",
                100,
                DateTimeOffset.UtcNow,
                "C#");
            var alpha = Symbol.Create(scan.Id, sourceFile.Id, $"scan:{scan.Id:N}/file:{sourceFile.Id:N}/NamedType:global::Alpha", "Alpha", SymbolKind.Class, 1, 10);
            var beta = Symbol.Create(scan.Id, betaFile.Id, $"scan:{scan.Id:N}/file:{betaFile.Id:N}/Method:global::Beta.Run", "Beta", SymbolKind.Method, 1, 4);
            var symbols = new List<Symbol> { alpha, beta };
            var gamma = includeThirdNode
                ? Symbol.Create(scan.Id, sourceFile.Id, $"scan:{scan.Id:N}/file:{sourceFile.Id:N}/NamedType:global::Gamma", "Gamma", SymbolKind.Class, 12, 20)
                : null;
            if (gamma is not null)
            {
                symbols.Add(gamma);
            }

            var dependencies = new List<Dependency>
            {
                Dependency.Create(scan.Id, alpha.Id, beta.Id, nameof(DependencyKind.Calls), isResolved: true)
            };
            if (includeDuplicateAndSelfEdges)
            {
                dependencies.Add(Dependency.Create(scan.Id, alpha.Id, beta.Id, nameof(DependencyKind.Calls), isResolved: true));
                dependencies.Add(Dependency.Create(scan.Id, alpha.Id, alpha.Id, nameof(DependencyKind.TypeReference), isResolved: true));
            }
            if (gamma is not null)
            {
                dependencies.Add(Dependency.Create(scan.Id, beta.Id, gamma.Id, nameof(DependencyKind.Inherits), isResolved: true));
            }

            var findings = new List<Finding>
            {
                Finding.Create(
                    scan.Id,
                    "TEST001",
                    FindingSeverity.Warning,
                    "Fixture finding",
                    sourceFileId: sourceFile.Id,
                    symbolId: alpha.Id,
                    startLine: 1,
                    endLine: 1)
            };

            scan.StartAnalysis();
            scan.CompleteAnalysis(2, symbols.Count, dependencies.Count, findings.Count);
            var snapshot = new GraphQuerySnapshot(
                scan,
                project,
                [sourceFile, betaFile],
                symbols,
                dependencies,
                findings);
            return new GraphFixture(project, scan, sourceFile, alpha, beta, gamma, snapshot);
        }
    }
}
