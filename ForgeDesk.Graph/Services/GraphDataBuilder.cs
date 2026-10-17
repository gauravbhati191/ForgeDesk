using ForgeDesk.Domain;

namespace ForgeDesk.Graph;

/// <summary>Transforms persisted scan rows into a filtered graph read model.</summary>
public sealed class GraphDataBuilder
{
    public GraphData Build(
        GraphQuerySnapshot snapshot,
        GraphQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot.ScanId != options.ScanId)
        {
            return GraphData.Missing(options.ScanId, "The graph snapshot does not belong to the requested scan.");
        }

        if (snapshot.Scan is null || snapshot.Project is null)
        {
            return GraphData.Missing(options.ScanId, "The requested scan or project no longer exists.");
        }

        if (options.ProjectId is Guid projectId && projectId != snapshot.Project.Id)
        {
            return GraphData.Missing(options.ScanId, "The requested scan does not belong to the selected project.");
        }

        var filters = options.EffectiveFilters;
        filters.Validate();

        var sourceFilesById = snapshot.SourceFiles
            .Where(sourceFile => sourceFile.ScanId == snapshot.ScanId)
            .GroupBy(sourceFile => sourceFile.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var symbols = snapshot.Symbols
            .Where(symbol => symbol.ScanId == snapshot.ScanId)
            .OrderBy(symbol => symbol.Id)
            .ToArray();
        var relatedFindingCounts = snapshot.Findings
            .Where(finding => finding.ScanId == snapshot.ScanId && finding.SymbolId is not null)
            .GroupBy(finding => finding.SymbolId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var nodesById = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = CreateNode(symbol, sourceFilesById.GetValueOrDefault(symbol.SourceFileId));
            // Malformed imported data must not cause one row to overwrite another node.
            // Persisted Symbol IDs are the stable identity; a duplicate row is retained only
            // once in the graph read model.
            nodesById.TryAdd(
                node.Id,
                node with { RelatedFindingCount = relatedFindingCounts.GetValueOrDefault(symbol.Id) });
        }

        var duplicateCount = 0;
        var selfEdgeCount = 0;
        var unresolvedCount = 0;
        var missingSourceFileCount = symbols.Count(symbol => !sourceFilesById.ContainsKey(symbol.SourceFileId));
        var candidateEdges = new List<GraphEdge>();
        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        var orderedDependencies = snapshot.Dependencies
            .Where(dependency => dependency.ScanId == snapshot.ScanId)
            .OrderBy(dependency => dependency.SourceSymbolId)
            .ThenBy(dependency => dependency.TargetSymbolId)
            .ThenBy(dependency => dependency.RelationshipType, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.Id)
            .ToArray();

        foreach (var dependency in orderedDependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dependency.SourceSymbolId is not Guid sourceSymbolId
                || dependency.TargetSymbolId is not Guid targetSymbolId
                || !nodesById.ContainsKey(sourceSymbolId.ToString("D"))
                || !nodesById.ContainsKey(targetSymbolId.ToString("D")))
            {
                unresolvedCount++;
                continue;
            }

            var sourceId = sourceSymbolId.ToString("D");
            var targetId = targetSymbolId.ToString("D");
            if (StringComparer.Ordinal.Equals(sourceId, targetId))
            {
                selfEdgeCount++;
                if (filters.SelfEdgePolicy == GraphSelfEdgePolicy.Exclude)
                {
                    continue;
                }
            }

            var relationship = dependency.RelationshipType?.Trim();
            if (string.IsNullOrWhiteSpace(relationship))
            {
                unresolvedCount++;
                continue;
            }

            if (!dependency.IsResolved)
            {
                unresolvedCount++;
            }
            var key = $"{sourceId}\u001f{targetId}\u001f{relationship.ToUpperInvariant()}";
            if (filters.DuplicateEdgePolicy == GraphDuplicateEdgePolicy.Collapse
                && !duplicateKeys.Add(key))
            {
                duplicateCount++;
                continue;
            }

            candidateEdges.Add(new GraphEdge(
                sourceId,
                targetId,
                relationship,
                ParseDependencyKind(relationship),
                dependency.Id,
                dependency.ScanId,
                dependency.IsResolved));
        }

        var totalNodeCount = nodesById.Count;
        var totalEdgeCount = orderedDependencies.Length;
        var visibleNodes = nodesById.Values
            .Where(node => MatchesNode(node, filters))
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(node => node.Id, node => node, StringComparer.Ordinal);
        var visibleEdges = candidateEdges
            .Where(edge => MatchesRelationship(edge, filters))
            .Where(edge => visibleNodes.ContainsKey(edge.FromId) && visibleNodes.ContainsKey(edge.ToId))
            .ToList();

        var requestedSelection = options.EffectiveSelectedNodeId;
        var selectedNodeId = requestedSelection is not null && visibleNodes.ContainsKey(requestedSelection)
            ? requestedSelection
            : null;

        if (selectedNodeId is not null)
        {
            var depth = filters.EffectiveDepth;
            if (depth is > 0)
            {
                var neighborhood = FindNeighborhood(
                    selectedNodeId,
                    visibleEdges,
                    filters.Direction,
                    depth.Value,
                    cancellationToken);
                visibleNodes = visibleNodes
                    .Where(pair => neighborhood.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                visibleEdges = visibleEdges
                    .Where(edge => neighborhood.Contains(edge.FromId) && neighborhood.Contains(edge.ToId))
                    .ToList();
            }
            else
            {
                visibleEdges = ApplyDirection(visibleEdges, selectedNodeId, filters.Direction);
            }
        }

        if (!filters.IncludeIsolatedNodes)
        {
            var connected = visibleEdges
                .SelectMany(edge => new[] { edge.FromId, edge.ToId })
                .ToHashSet(StringComparer.Ordinal);
            visibleNodes = visibleNodes
                .Where(pair => connected.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            visibleEdges = visibleEdges
                .Where(edge => visibleNodes.ContainsKey(edge.FromId) && visibleNodes.ContainsKey(edge.ToId))
                .ToList();
            if (selectedNodeId is not null && !visibleNodes.ContainsKey(selectedNodeId))
            {
                selectedNodeId = null;
            }
        }

        var neighbors = selectedNodeId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : visibleEdges
                .Where(edge => StringComparer.Ordinal.Equals(edge.FromId, selectedNodeId)
                    || StringComparer.Ordinal.Equals(edge.ToId, selectedNodeId))
                .SelectMany(edge => new[]
                {
                    StringComparer.Ordinal.Equals(edge.FromId, selectedNodeId) ? edge.ToId : edge.FromId
                })
                .ToHashSet(StringComparer.Ordinal);

        var filteredEdges = visibleEdges
            .Select(edge => edge with
            {
                IsIncoming = selectedNodeId is not null && StringComparer.Ordinal.Equals(edge.ToId, selectedNodeId),
                IsOutgoing = selectedNodeId is not null && StringComparer.Ordinal.Equals(edge.FromId, selectedNodeId),
                IsHighlighted = selectedNodeId is not null
                    && (StringComparer.Ordinal.Equals(edge.FromId, selectedNodeId)
                        || StringComparer.Ordinal.Equals(edge.ToId, selectedNodeId))
            })
            .OrderBy(edge => edge.FromId, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToId, StringComparer.Ordinal)
            .ThenBy(edge => edge.RelationshipType, StringComparer.Ordinal)
            .ThenBy(edge => edge.DependencyId)
            .ToArray();

        var incomingCounts = filteredEdges
            .GroupBy(edge => edge.ToId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var outgoingCounts = filteredEdges
            .GroupBy(edge => edge.FromId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var filteredNodes = visibleNodes.Values
            .Select(node => node with
            {
                IsSelected = selectedNodeId is not null && StringComparer.Ordinal.Equals(node.Id, selectedNodeId),
                IsNeighbor = filters.HighlightSelectedNeighbors && neighbors.Contains(node.Id),
                IsHighlighted = (selectedNodeId is not null && StringComparer.Ordinal.Equals(node.Id, selectedNodeId))
                    || (filters.HighlightSelectedNeighbors && neighbors.Contains(node.Id)),
                IsIsolated = !incomingCounts.ContainsKey(node.Id) && !outgoingCounts.ContainsKey(node.Id),
                IncomingDependencyCount = incomingCounts.GetValueOrDefault(node.Id),
                OutgoingDependencyCount = outgoingCounts.GetValueOrDefault(node.Id)
            })
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var state = DetermineState(
            snapshot.Scan,
            symbols.Length,
            orderedDependencies.Length,
            unresolvedCount,
            missingSourceFileCount);
        var message = BuildStatusMessage(
            snapshot.Scan,
            state,
            symbols.Length,
            orderedDependencies.Length,
            unresolvedCount,
            missingSourceFileCount);
        return new GraphData(
            snapshot.ScanId,
            snapshot.Project.Id,
            state,
            filteredNodes,
            filteredEdges,
            message)
        {
            AnalysisStatus = snapshot.Scan.AnalysisStatus,
            SelectedNodeId = selectedNodeId,
            TotalNodeCount = totalNodeCount,
            TotalEdgeCount = totalEdgeCount,
            VisibleNodeCount = filteredNodes.Length,
            VisibleEdgeCount = filteredEdges.Length,
            PersistedNodeCount = symbols.Length,
            PersistedEdgeCount = orderedDependencies.Length,
            ValidEdgeCount = candidateEdges.Count,
            UnresolvedDependencyCount = unresolvedCount,
            MissingSourceFileCount = missingSourceFileCount,
            DuplicateEdgeCount = duplicateCount,
            SelfEdgeCount = selfEdgeCount,
            AnalysisCompletedAtUtc = snapshot.Scan.AnalysisCompletedAtUtc
        };
    }

    /// <summary>Convenience overload for deterministic unit tests and in-memory callers.</summary>
    public GraphData Build(
        Guid scanId,
        Scan scan,
        Project project,
        IReadOnlyList<SourceFile> sourceFiles,
        IReadOnlyList<Symbol> symbols,
        IReadOnlyList<Dependency> dependencies,
        GraphQueryOptions? options = null,
        CancellationToken cancellationToken = default)
        => Build(
            new GraphQuerySnapshot(scanId, scan, project, sourceFiles, symbols, dependencies),
            options ?? new GraphQueryOptions(scanId),
            cancellationToken);

    public Task<GraphData> BuildAsync(
        GraphQuerySnapshot snapshot,
        GraphQueryOptions options,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Build(snapshot, options, cancellationToken));

    private static GraphNode CreateNode(Symbol symbol, SourceFile? sourceFile)
    {
        var qualifiedName = ExtractQualifiedName(symbol.SymbolKey);
        var containing = FindContainingName(qualifiedName, symbol.Name);
        return new GraphNode(symbol.Id.ToString("D"), symbol.Name, symbol.Kind)
        {
            SymbolId = symbol.Id,
            ScanId = symbol.ScanId,
            SymbolKey = symbol.SymbolKey,
            FullyQualifiedName = qualifiedName,
            Namespace = FindNamespaceName(qualifiedName, symbol.Kind),
            ContainingType = symbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event
                ? containing
                : null,
            RelativePath = sourceFile?.RelativePath,
            StartLine = symbol.StartLine,
            EndLine = symbol.EndLine
        };
    }

    private static bool MatchesNode(GraphNode node, GraphFilterOptions filters)
    {
        var search = filters.EffectiveSearch;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            if (!Contains(node.Label, value)
                && !Contains(node.FullyQualifiedName, value)
                && !Contains(node.RelativePath, value)
                && !Contains(node.SymbolKey, value))
            {
                return false;
            }
        }

        var kinds = filters.EffectiveSymbolKinds;
        if (kinds is not null && kinds.Count > 0 && !kinds.Contains(node.Kind))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filters.Namespace)
            && (node.Namespace is null
                || (!StringComparer.OrdinalIgnoreCase.Equals(node.Namespace, filters.Namespace.Trim())
                    && !node.Namespace.StartsWith(filters.Namespace.Trim() + ".", StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesRelationship(GraphEdge edge, GraphFilterOptions filters)
    {
        var relationshipTypes = filters.EffectiveRelationshipTypes;
        if (relationshipTypes is not null
            && relationshipTypes.Count > 0
            && !relationshipTypes.Any(type => StringComparer.OrdinalIgnoreCase.Equals(type?.Trim(), edge.RelationshipType)))
        {
            return false;
        }

        var relationshipKinds = filters.EffectiveRelationshipKinds;
        return relationshipKinds is null
            || relationshipKinds.Count == 0
            || relationshipKinds.Contains(edge.Kind);
    }

    private static List<GraphEdge> ApplyDirection(
        IEnumerable<GraphEdge> edges,
        string selectedNodeId,
        GraphDirection direction)
        => direction switch
        {
            GraphDirection.Incoming => edges
                .Where(edge => StringComparer.Ordinal.Equals(edge.ToId, selectedNodeId))
                .ToList(),
            GraphDirection.Outgoing => edges
                .Where(edge => StringComparer.Ordinal.Equals(edge.FromId, selectedNodeId))
                .ToList(),
            _ => edges.ToList()
        };

    private static HashSet<string> FindNeighborhood(
        string selectedNodeId,
        IReadOnlyCollection<GraphEdge> edges,
        GraphDirection direction,
        int depth,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { selectedNodeId };
        if (depth == 0)
        {
            return result;
        }

        var frontier = new HashSet<string>(StringComparer.Ordinal) { selectedNodeId };
        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edge in edges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var nodeId in frontier)
                {
                    if (direction is GraphDirection.Both or GraphDirection.Outgoing
                        && StringComparer.Ordinal.Equals(edge.FromId, nodeId))
                    {
                        next.Add(edge.ToId);
                    }

                    if (direction is GraphDirection.Both or GraphDirection.Incoming
                        && StringComparer.Ordinal.Equals(edge.ToId, nodeId))
                    {
                        next.Add(edge.FromId);
                    }
                }
            }

            next.ExceptWith(result);
            result.UnionWith(next);
            frontier = next;
        }

        return result;
    }

    private static GraphDataState DetermineState(
        Scan scan,
        int symbolCount,
        int dependencyCount,
        int unresolvedCount,
        int missingSourceFileCount)
    {
        if (scan.AnalysisStatus == AnalysisStatus.Completed)
        {
            if (scan.AnalysisSymbolCount != symbolCount || scan.AnalysisDependencyCount != dependencyCount)
            {
                return GraphDataState.Stale;
            }

            if (unresolvedCount > 0 || missingSourceFileCount > 0)
            {
                return symbolCount == 0 ? GraphDataState.Empty : GraphDataState.Partial;
            }

            return symbolCount == 0 ? GraphDataState.Empty : GraphDataState.Ready;
        }

        if (scan.AnalysisStatus is AnalysisStatus.Running or AnalysisStatus.Failed or AnalysisStatus.Cancelled)
        {
            return GraphDataState.Partial;
        }

        return symbolCount == 0 ? GraphDataState.Empty : GraphDataState.Partial;
    }

    private static string BuildStatusMessage(
        Scan scan,
        GraphDataState state,
        int symbolCount,
        int dependencyCount,
        int unresolvedCount,
        int missingSourceFileCount)
        => state switch
        {
            GraphDataState.Empty when scan.AnalysisStatus == AnalysisStatus.NotStarted
                => "No analysis has been run for this scan.",
            GraphDataState.Empty => "The analysis completed without persisted symbols.",
            GraphDataState.Partial when scan.AnalysisStatus == AnalysisStatus.Running
                => "Analysis is still running; the graph is a partial snapshot.",
            GraphDataState.Partial when scan.AnalysisStatus == AnalysisStatus.Cancelled
                => "Analysis was cancelled; the graph contains partial results.",
            GraphDataState.Partial when scan.AnalysisStatus == AnalysisStatus.Failed
                => $"Analysis failed; the graph contains partial results. {scan.AnalysisErrorMessage}".Trim(),
            GraphDataState.Partial when unresolvedCount > 0
                => $"The graph contains {unresolvedCount} unresolved or malformed dependency row(s).",
            GraphDataState.Partial when missingSourceFileCount > 0
                => $"The graph contains {missingSourceFileCount} symbol(s) whose source-file metadata is missing.",
            GraphDataState.Stale
                => $"Persisted graph rows ({symbolCount} symbols, {dependencyCount} dependencies) do not match the completed analysis counts.",
            _ => null!
        } ?? "Graph data loaded.";

    private static DependencyKind ParseDependencyKind(string relationshipType)
        => Enum.TryParse<DependencyKind>(relationshipType, ignoreCase: true, out var kind)
            ? kind
            : DependencyKind.Unknown;

    private static string? ExtractQualifiedName(string symbolKey)
    {
        if (string.IsNullOrWhiteSpace(symbolKey))
        {
            return null;
        }

        var slash = symbolKey.LastIndexOf('/');
        var identity = slash >= 0 ? symbolKey[(slash + 1)..] : symbolKey;
        var colon = identity.IndexOf(':');
        if (colon < 0 || colon == identity.Length - 1)
        {
            return null;
        }

        var qualified = identity[(colon + 1)..];
        var separator = qualified.IndexOf('|');
        if (separator >= 0)
        {
            qualified = qualified[..separator];
        }

        return qualified.StartsWith("global::", StringComparison.Ordinal)
            ? qualified[8..]
            : qualified;
    }

    private static string? FindContainingName(string? qualifiedName, string name)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return null;
        }

        var marker = $".{name}";
        var index = qualifiedName.LastIndexOf(marker, StringComparison.Ordinal);
        return index > 0 ? qualifiedName[..index] : null;
    }

    private static string? FindNamespaceName(string? qualifiedName, SymbolKind kind)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return null;
        }

        if (kind == SymbolKind.Namespace)
        {
            return qualifiedName;
        }

        var segments = qualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1)
        {
            return null;
        }

        var isMember = kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event;
        var namespaceLength = isMember ? segments.Length - 2 : segments.Length - 1;
        return namespaceLength > 0
            ? string.Join('.', segments.Take(namespaceLength))
            : null;
    }

    private static bool Contains(string? value, string search)
        => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
}
