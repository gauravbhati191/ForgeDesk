using ForgeDesk.Domain;

namespace ForgeDesk.Graph;

/// <summary>Graph filters applied to a selected scan after its persisted rows are loaded.</summary>
public sealed record GraphFilterOptions
{
    public GraphFilterOptions()
    {
    }

    public GraphFilterOptions(
        string? search = null,
        IEnumerable<SymbolKind>? symbolKinds = null,
        IEnumerable<string>? relationshipTypes = null,
        GraphDirection direction = GraphDirection.Both,
        int? neighborhoodDepth = null,
        bool includeIsolatedNodes = true)
    {
        Search = search;
        SymbolKinds = symbolKinds?.ToArray();
        RelationshipTypes = relationshipTypes?.ToArray();
        Direction = direction;
        NeighborhoodDepth = neighborhoodDepth;
        IncludeIsolatedNodes = includeIsolatedNodes;
    }

    /// <summary>Case-insensitive substring matched against symbol name, qualified name, and path.</summary>
    public string? Search { get; init; }

    /// <summary>Alias accepted by callers that use the UI vocabulary.</summary>
    public string? SearchTerm { get; init; }

    /// <summary>Alias accepted by callers that model the search box explicitly.</summary>
    public string? SearchText { get; init; }

    public IReadOnlyCollection<SymbolKind>? SymbolKinds { get; init; }
    public IReadOnlyCollection<SymbolKind>? Kinds { get; init; }
    public IReadOnlyCollection<SymbolKind>? KindFilter { get; init; }
    public IReadOnlyCollection<string>? RelationshipTypes { get; init; }
    public IReadOnlyCollection<string>? Relationships { get; init; }
    public IReadOnlyCollection<string>? RelationshipFilter { get; init; }
    public IReadOnlyCollection<DependencyKind>? RelationshipKinds { get; init; }
    public IReadOnlyCollection<DependencyKind>? DependencyKinds { get; init; }
    public GraphDirection Direction { get; init; } = GraphDirection.Both;
    public int? NeighborhoodDepth { get; init; }
    public int? Depth { get; init; }
    public string? Namespace { get; init; }
    public bool IncludeIsolatedNodes { get; init; } = true;
    public GraphSelfEdgePolicy SelfEdgePolicy { get; init; } = GraphSelfEdgePolicy.Exclude;
    public GraphDuplicateEdgePolicy DuplicateEdgePolicy { get; init; } = GraphDuplicateEdgePolicy.Collapse;
    public bool HighlightSelectedNeighbors { get; init; } = true;

    internal string? EffectiveSearch
        => string.IsNullOrWhiteSpace(Search)
            ? (string.IsNullOrWhiteSpace(SearchTerm) ? SearchText : SearchTerm)
            : Search;

    internal IReadOnlyCollection<SymbolKind>? EffectiveSymbolKinds
        => SymbolKinds ?? Kinds ?? KindFilter;

    internal IReadOnlyCollection<string>? EffectiveRelationshipTypes
        => RelationshipTypes ?? Relationships ?? RelationshipFilter;

    internal IReadOnlyCollection<DependencyKind>? EffectiveRelationshipKinds
        => RelationshipKinds ?? DependencyKinds;

    internal int? EffectiveDepth => NeighborhoodDepth ?? Depth;

    internal void Validate()
    {
        if (!Enum.IsDefined(Direction))
        {
            throw new ArgumentOutOfRangeException(nameof(Direction), "The graph direction is not supported.");
        }

        if (EffectiveDepth is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(NeighborhoodDepth), "Graph neighborhood depth cannot be negative.");
        }

        if (!Enum.IsDefined(SelfEdgePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(SelfEdgePolicy), "The self-edge policy is not supported.");
        }

        if (!Enum.IsDefined(DuplicateEdgePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(DuplicateEdgePolicy), "The duplicate-edge policy is not supported.");
        }
    }
}

/// <summary>Options for a scan-scoped graph query.</summary>
public sealed record GraphQueryOptions
{
    public GraphQueryOptions()
    {
    }

    public GraphQueryOptions(Guid scanId, GraphFilterOptions? filters = null, GraphSelection? selection = null)
    {
        ScanId = scanId;
        Filters = filters;
        Selection = selection;
    }

    public GraphQueryOptions(
        Guid scanId,
        string? search,
        IEnumerable<SymbolKind>? symbolKinds = null,
        IEnumerable<string>? relationshipTypes = null,
        GraphDirection direction = GraphDirection.Both,
        int? neighborhoodDepth = null,
        bool includeIsolatedNodes = true,
        string? selectedNodeId = null)
        : this(
            scanId,
            new GraphFilterOptions(search, symbolKinds, relationshipTypes, direction, neighborhoodDepth, includeIsolatedNodes),
            selectedNodeId is null ? null : new GraphSelection(selectedNodeId))
    {
    }

    public Guid ScanId { get; init; }
    public Guid? ProjectId { get; init; }
    public GraphFilterOptions? Filters { get; init; }
    public GraphFilterOptions? FilterOptions { get; init; }
    public GraphSelection? Selection { get; init; }
    public string? SelectedNodeId { get; init; }
    public Guid? SelectedSymbolId { get; init; }

    // Direct aliases keep simple callers from having to allocate a nested filter object.
    public string? Search { get; init; }
    public string? SearchText { get; init; }
    public IReadOnlyCollection<SymbolKind>? SymbolKinds { get; init; }
    public IReadOnlyCollection<SymbolKind>? Kinds { get; init; }
    public IReadOnlyCollection<SymbolKind>? KindFilter { get; init; }
    public IReadOnlyCollection<string>? RelationshipTypes { get; init; }
    public IReadOnlyCollection<string>? Relationships { get; init; }
    public IReadOnlyCollection<string>? RelationshipFilter { get; init; }
    public IReadOnlyCollection<DependencyKind>? RelationshipKinds { get; init; }
    public IReadOnlyCollection<DependencyKind>? DependencyKinds { get; init; }
    public GraphDirection? Direction { get; init; }
    public int? NeighborhoodDepth { get; init; }
    public int? Depth { get; init; }
    public bool? IncludeIsolatedNodes { get; init; }
    public bool? ShowIsolatedNodes { get; init; }
    public string? Namespace { get; init; }
    public string? NamespaceFilter { get; init; }
    public GraphSelfEdgePolicy? SelfEdgePolicy { get; init; }
    public GraphDuplicateEdgePolicy? DuplicateEdgePolicy { get; init; }
    public bool? HighlightSelectedNeighbors { get; init; }

    internal GraphFilterOptions EffectiveFilters
    {
        get
        {
            var source = Filters ?? FilterOptions ?? new GraphFilterOptions();
            return source with
            {
                Search = Search ?? SearchText ?? source.EffectiveSearch,
                SymbolKinds = SymbolKinds ?? Kinds ?? KindFilter ?? source.EffectiveSymbolKinds,
                RelationshipTypes = RelationshipTypes ?? Relationships ?? RelationshipFilter ?? source.EffectiveRelationshipTypes,
                RelationshipKinds = RelationshipKinds ?? DependencyKinds ?? source.EffectiveRelationshipKinds,
                Direction = Direction ?? source.Direction,
                NeighborhoodDepth = NeighborhoodDepth ?? Depth ?? source.NeighborhoodDepth ?? source.Depth,
                IncludeIsolatedNodes = IncludeIsolatedNodes ?? ShowIsolatedNodes ?? source.IncludeIsolatedNodes,
                Namespace = Namespace ?? NamespaceFilter ?? source.Namespace,
                SelfEdgePolicy = SelfEdgePolicy ?? source.SelfEdgePolicy,
                DuplicateEdgePolicy = DuplicateEdgePolicy ?? source.DuplicateEdgePolicy,
                HighlightSelectedNeighbors = HighlightSelectedNeighbors ?? source.HighlightSelectedNeighbors
            };
        }
    }

    internal string? EffectiveSelectedNodeId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SelectedNodeId))
            {
                return SelectedNodeId.Trim();
            }

            if (SelectedSymbolId is Guid symbolId && symbolId != Guid.Empty)
            {
                return symbolId.ToString("D");
            }

            return Selection?.NodeId;
        }
    }

    internal void Validate()
    {
        if (ScanId == Guid.Empty)
        {
            throw new ArgumentException("A scan identifier is required.", nameof(ScanId));
        }

        EffectiveFilters.Validate();
    }
}

/// <summary>Spacing settings for deterministic graph coordinates.</summary>
public sealed record GraphLayoutOptions
{
    public double HorizontalSpacing { get; init; } = 220;
    public double VerticalSpacing { get; init; } = 120;
    public int? Columns { get; init; }
    public double OriginX { get; init; } = 0;
    public double OriginY { get; init; } = 0;

    internal void Validate()
    {
        if (HorizontalSpacing <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HorizontalSpacing));
        }

        if (VerticalSpacing <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VerticalSpacing));
        }

        if (Columns is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Columns));
        }
    }
}
