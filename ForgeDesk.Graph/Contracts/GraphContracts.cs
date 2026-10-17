using ForgeDesk.Domain;

namespace ForgeDesk.Graph;

/// <summary>Lifecycle/data quality state for a graph read model.</summary>
public enum GraphDataState
{
    /// <summary>The requested scan or project could not be found.</summary>
    Missing = 0,

    /// <summary>No symbols were persisted for the requested scan.</summary>
    Empty = 1,

    /// <summary>The graph is complete according to the persisted analysis counts.</summary>
    Ready = 2,

    /// <summary>Some analysis data exists, but the analysis was not completed successfully.</summary>
    Partial = 3,

    /// <summary>The scan says it completed, but its persisted rows no longer match its counts.</summary>
    Stale = 4,

    /// <summary>A graph query failed before a usable graph could be produced.</summary>
    Failed = 5
}

/// <summary>Direction used by selection-scoped graph filtering.</summary>
public enum GraphDirection
{
    Both = 0,
    Incoming = 1,
    Outgoing = 2
}

/// <summary>Policy for malformed persisted dependencies whose endpoints are the same symbol.</summary>
public enum GraphSelfEdgePolicy
{
    /// <summary>Do not expose self-edges. This is the default because Phase 2 does not emit them.</summary>
    Exclude = 0,

    /// <summary>Keep self-edges when the caller explicitly requests them.</summary>
    Include = 1
}

/// <summary>Policy for repeated dependencies with the same source, target, and relationship.</summary>
public enum GraphDuplicateEdgePolicy
{
    /// <summary>Collapse repeated semantic relationships into one deterministic edge.</summary>
    Collapse = 0,

    /// <summary>Keep every persisted dependency row as a separate edge.</summary>
    Preserve = 1
}

/// <summary>Stable coordinates assigned by the graph layout service.</summary>
public readonly record struct GraphPoint(double X, double Y);

/// <summary>A source symbol projected into the graph read model.</summary>
public sealed record GraphNode
{
    /// <summary>Creates a node with the minimum graph metadata.</summary>
    public GraphNode(string id, string label, SymbolKind kind)
    {
        Id = RequireText(id, nameof(id));
        Label = RequireText(label, nameof(label));
        Kind = kind;
    }

    /// <summary>
    /// Compatibility constructor for the original Phase 1 graph contract. Dependency kinds
    /// describe edges, so the node symbol kind is intentionally Unknown in this overload.
    /// </summary>
    public GraphNode(string id, string label, DependencyKind kind)
        : this(id, label, SymbolKind.Unknown)
    {
        RelationshipKind = kind;
    }

    public string Id { get; init; }
    public string Label { get; init; }
    public string DisplayName => Label;
    public SymbolKind Kind { get; init; }
    public SymbolKind SymbolKind => Kind;
    public DependencyKind? RelationshipKind { get; init; }

    /// <summary>The persisted Symbol identifier represented by <see cref="Id"/> when available.</summary>
    public Guid? SymbolId { get; init; }

    public Guid ScanId { get; init; }
    public string? SymbolKey { get; init; }
    public string? FullyQualifiedName { get; init; }
    public string? Namespace { get; init; }
    public string? ContainingType { get; init; }
    public string? RelativePath { get; init; }
    public string? SourceFilePath => RelativePath;
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }

    public double X { get; init; }
    public double Y { get; init; }
    public GraphPoint Position => new(X, Y);

    /// <summary>True when this node is the current graph selection.</summary>
    public bool IsSelected { get; init; }

    /// <summary>True when this node is a direct incoming or outgoing neighbor of the selection.</summary>
    public bool IsNeighbor { get; init; }

    /// <summary>Alias useful to renderers that only need one highlight flag.</summary>
    public bool IsHighlighted { get; init; }

    public bool IsIsolated { get; init; }
    public int IncomingDependencyCount { get; init; }
    public int OutgoingDependencyCount { get; init; }
    public int RelatedFindingCount { get; init; }

    private static string RequireText(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A graph value is required.", parameterName)
            : value.Trim();
}

/// <summary>A directed dependency relationship between two graph nodes.</summary>
public sealed record GraphEdge
{
    /// <summary>Creates an edge with a known relationship enum.</summary>
    public GraphEdge(string fromId, string toId, DependencyKind kind)
        : this(fromId, toId, kind.ToString(), kind)
    {
    }

    /// <summary>Creates an edge from the persisted relationship text.</summary>
    public GraphEdge(string fromId, string toId, string relationshipType, bool isResolved = true)
        : this(fromId, toId, relationshipType, ParseKind(relationshipType))
    {
        IsResolved = isResolved;
    }

    public GraphEdge(
        string fromId,
        string toId,
        string relationshipType,
        DependencyKind kind,
        Guid? dependencyId = null,
        Guid? scanId = null,
        bool isResolved = true)
    {
        FromId = RequireText(fromId, nameof(fromId));
        ToId = RequireText(toId, nameof(toId));
        RelationshipType = RequireText(relationshipType, nameof(relationshipType));
        Kind = kind;
        DependencyId = dependencyId;
        ScanId = scanId;
        IsResolved = isResolved;
    }

    public string FromId { get; init; }
    public string ToId { get; init; }
    public string SourceId => FromId;
    public string TargetId => ToId;
    public string RelationshipType { get; init; }
    public DependencyKind Kind { get; init; }
    public DependencyKind RelationshipKind => Kind;
    public Guid? DependencyId { get; init; }
    public Guid? ScanId { get; init; }
    public bool IsResolved { get; init; }
    public bool IsSelfEdge => StringComparer.Ordinal.Equals(FromId, ToId);
    public bool IsIncoming { get; init; }
    public bool IsOutgoing { get; init; }
    public bool IsHighlighted { get; init; }

    private static DependencyKind ParseKind(string relationshipType)
        => Enum.TryParse<DependencyKind>(relationshipType, ignoreCase: true, out var kind)
            ? kind
            : DependencyKind.Unknown;

    private static string RequireText(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A graph value is required.", parameterName)
            : value.Trim();
}

/// <summary>Immutable graph data plus counts and analysis-quality metadata.</summary>
public sealed record GraphData
{
    public GraphData(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        Edges = edges ?? throw new ArgumentNullException(nameof(edges));
        TotalNodeCount = nodes.Count;
        TotalEdgeCount = edges.Count;
        VisibleNodeCount = nodes.Count;
        VisibleEdgeCount = edges.Count;
        State = nodes.Count == 0 ? GraphDataState.Empty : GraphDataState.Ready;
    }

    public GraphData(
        Guid scanId,
        Guid? projectId,
        GraphDataState state,
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphEdge> edges,
        string? statusMessage = null)
        : this(nodes, edges)
    {
        ScanId = scanId;
        ProjectId = projectId;
        State = state;
        StatusMessage = statusMessage;
    }

    public Guid ScanId { get; init; }
    public Guid? ProjectId { get; init; }
    public AnalysisStatus? AnalysisStatus { get; init; }
    public GraphDataState State { get; init; }
    public GraphDataState Status => State;
    public string? StatusMessage { get; init; }
    public string? Message => StatusMessage;
    public bool IsPartial => State == GraphDataState.Partial;
    public bool IsStale => State == GraphDataState.Stale;
    public bool IsEmpty => State == GraphDataState.Empty;
    public bool IsMissing => State == GraphDataState.Missing;

    /// <summary>Nodes after all query filters have been applied.</summary>
    public IReadOnlyList<GraphNode> Nodes { get; init; }

    /// <summary>Edges after all query filters have been applied.</summary>
    public IReadOnlyList<GraphEdge> Edges { get; init; }

    public string? SelectedNodeId { get; init; }
    public int TotalNodeCount { get; init; }
    public int TotalEdgeCount { get; init; }
    public int VisibleNodeCount { get; init; }
    public int VisibleEdgeCount { get; init; }
    public int PersistedNodeCount { get; init; }
    public int PersistedEdgeCount { get; init; }
    public int ValidEdgeCount { get; init; }
    public int UnresolvedDependencyCount { get; init; }
    public int MissingSourceFileCount { get; init; }
    public int DuplicateEdgeCount { get; init; }
    public int SelfEdgeCount { get; init; }
    public DateTimeOffset? AnalysisCompletedAtUtc { get; init; }

    public static GraphData Missing(Guid scanId, string message)
        => new GraphData(Array.Empty<GraphNode>(), Array.Empty<GraphEdge>()) with
        {
            ScanId = scanId,
            State = GraphDataState.Missing,
            StatusMessage = message,
            VisibleNodeCount = 0,
            VisibleEdgeCount = 0,
            TotalNodeCount = 0,
            TotalEdgeCount = 0
        };

    public static GraphData Failed(Guid scanId, string message)
        => new GraphData(Array.Empty<GraphNode>(), Array.Empty<GraphEdge>()) with
        {
            ScanId = scanId,
            State = GraphDataState.Failed,
            StatusMessage = message,
            VisibleNodeCount = 0,
            VisibleEdgeCount = 0,
            TotalNodeCount = 0,
            TotalEdgeCount = 0
        };
}

/// <summary>Selection state used by depth, direction, and neighbor highlighting filters.</summary>
public sealed record GraphSelection
{
    public GraphSelection(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("A selected node identifier is required.", nameof(nodeId));
        }

        NodeId = nodeId.Trim();
        if (Guid.TryParse(NodeId, out var symbolId))
        {
            SymbolId = symbolId;
        }
    }

    public GraphSelection(Guid symbolId)
    {
        if (symbolId == Guid.Empty)
        {
            throw new ArgumentException("A selected symbol identifier is required.", nameof(symbolId));
        }

        SymbolId = symbolId;
        NodeId = symbolId.ToString("D");
    }

    public string NodeId { get; init; }
    public Guid? SymbolId { get; init; }
}

/// <summary>Read-only query snapshot returned by the infrastructure graph repository.</summary>
public sealed record GraphQuerySnapshot
{
    public GraphQuerySnapshot(
        Guid scanId,
        Scan? scan,
        Project? project,
        IReadOnlyList<SourceFile> sourceFiles,
        IReadOnlyList<Symbol> symbols,
        IReadOnlyList<Dependency> dependencies,
        IReadOnlyList<Finding>? findings = null)
    {
        ScanId = scanId;
        Scan = scan;
        Project = project;
        SourceFiles = sourceFiles ?? throw new ArgumentNullException(nameof(sourceFiles));
        Symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        Findings = findings ?? Array.Empty<Finding>();
    }

    public GraphQuerySnapshot(
        Scan scan,
        Project project,
        IReadOnlyList<SourceFile> sourceFiles,
        IReadOnlyList<Symbol> symbols,
        IReadOnlyList<Dependency> dependencies,
        IReadOnlyList<Finding>? findings = null)
        : this(scan?.Id ?? Guid.Empty, scan, project, sourceFiles, symbols, dependencies, findings)
    {
    }

    public Guid ScanId { get; }
    public Scan? Scan { get; }
    public Project? Project { get; }
    public IReadOnlyList<SourceFile> SourceFiles { get; }
    public IReadOnlyList<Symbol> Symbols { get; }
    public IReadOnlyList<Dependency> Dependencies { get; }
    public IReadOnlyList<Finding> Findings { get; }
}

/// <summary>Persistence boundary for a scan-scoped, read-only graph snapshot.</summary>
public interface IGraphQueryRepository
{
    Task<GraphQuerySnapshot?> GetSnapshotAsync(Guid scanId, CancellationToken cancellationToken = default);
}

/// <summary>Builds filtered graph data from a graph query repository.</summary>
public interface IGraphDataProvider
{
    Task<GraphData> GetAsync(GraphQueryOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Calculates temporary, deterministic coordinates without changing persisted analysis facts.</summary>
public interface IGraphLayoutService
{
    GraphData Layout(GraphData graph, GraphLayoutOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Legacy graph contract retained for callers from the initial scaffold.</summary>
public sealed record DependencyGraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

/// <summary>Legacy dependency-only builder contract retained for compatibility.</summary>
public interface IDependencyGraphBuilder
{
    Task<DependencyGraph> BuildAsync(IReadOnlyCollection<Dependency> dependencies, CancellationToken cancellationToken = default);
}
