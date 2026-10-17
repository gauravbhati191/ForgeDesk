using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Domain;
using ForgeDesk.Graph;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.Logging;

using CoreGraphData = ForgeDesk.Graph.GraphData;
using CoreGraphDataState = ForgeDesk.Graph.GraphDataState;
using CoreGraphFilterOptions = ForgeDesk.Graph.GraphFilterOptions;
using CoreGraphQueryOptions = ForgeDesk.Graph.GraphQueryOptions;
using CoreGraphSelection = ForgeDesk.Graph.GraphSelection;

namespace ForgeDesk.Presentation.ViewModels;

public enum GraphDisplayState
{
    NoProject,
    Loading,
    Empty,
    Ready,
    Partial,
    Error,
    Cancelled
}

/// <summary>Coordinates the interactive projection of persisted symbol/dependency rows.</summary>
public sealed partial class DependencyGraphViewModel : ObservableObject
{
    private readonly AnalysisViewModel _analysisViewModel;
    private readonly IProjectGraphService _graphService;
    private readonly ILogger<DependencyGraphViewModel> _logger;
    private CancellationTokenSource? _projectionCancellation;
    private long _projectionVersion;
    private bool _isNormalizingFilters;

    public DependencyGraphViewModel(
        AnalysisViewModel analysisViewModel,
        IProjectGraphService graphService,
        SourceViewerViewModel sourceViewer,
        SymbolDetailsViewModel details,
        ILogger<DependencyGraphViewModel> logger)
    {
        _analysisViewModel = analysisViewModel ?? throw new ArgumentNullException(nameof(analysisViewModel));
        _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
        SourceViewer = sourceViewer ?? throw new ArgumentNullException(nameof(sourceViewer));
        Details = details ?? throw new ArgumentNullException(nameof(details));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _analysisViewModel.PropertyChanged += OnAnalysisPropertyChanged;
        Details.IncomingRequested += OnIncomingRequested;
        Details.OutgoingRequested += OnOutgoingRequested;
        Details.RelatedFindingsRequested += OnRelatedFindingsRequested;
    }

    public SourceViewerViewModel SourceViewer { get; }

    public SymbolDetailsViewModel Details { get; }

    public IReadOnlyList<string> SymbolKindOptions { get; } =
        new[] { "All", nameof(SymbolKind.Namespace), nameof(SymbolKind.Class), nameof(SymbolKind.Struct), nameof(SymbolKind.Interface), nameof(SymbolKind.Enum), nameof(SymbolKind.Record), nameof(SymbolKind.Method), nameof(SymbolKind.Property), nameof(SymbolKind.Field), nameof(SymbolKind.Event) };

    public IReadOnlyList<string> RelationshipOptions { get; private set; } = new[] { "All" };

    public IReadOnlyList<GraphDirection> DirectionOptions { get; } = Enum.GetValues<GraphDirection>();

    public bool HasProjection => Projection is not null;

    public bool ShowNoProject => State == GraphDisplayState.NoProject;

    public bool ShowLoading => State == GraphDisplayState.Loading;

    public bool ShowEmpty => State == GraphDisplayState.Empty;

    public bool ShowResults => State is GraphDisplayState.Ready or GraphDisplayState.Partial;

    public bool ShowPartial => State == GraphDisplayState.Partial;

    public bool ShowError => State == GraphDisplayState.Error;

    public bool ShowCancelled => State == GraphDisplayState.Cancelled;

    public bool ShowFilteredEmpty => ShowResults && Projection is not null && TotalNodeCount > 0 && VisibleNodeCount == 0;

    public int VisibleNodeCount => Projection?.VisibleNodeCount ?? 0;

    public int VisibleEdgeCount => Projection?.VisibleEdgeCount ?? 0;

    public int TotalNodeCount => Projection?.TotalNodeCount ?? 0;

    public int TotalEdgeCount => Projection?.TotalEdgeCount ?? 0;

    public string CountSummary => Projection is null
        ? "No graph data loaded"
        : $"{VisibleNodeCount:N0} of {TotalNodeCount:N0} nodes · {VisibleEdgeCount:N0} of {TotalEdgeCount:N0} edges visible";

    public string SelectedNodeLabel => SelectedNode?.DisplayName ?? "No node selected";

    public DependencyGraphEdgeItem? SelectedEdge
        => Projection?.Edges.FirstOrDefault(edge => edge.Id == SelectedEdgeId);

    public string SelectedEdgeLabel => SelectedEdge is null
        ? "No edge selected"
        : $"{SelectedEdge.RelationshipType}: {SelectedEdge.FromName} → {SelectedEdge.ToName}";

    public event EventHandler? RelatedFindingsRequested;

    [ObservableProperty]
    private DependencyGraphProjection? _projection;

    [ObservableProperty]
    private GraphDisplayState _state = GraphDisplayState.NoProject;

    [ObservableProperty]
    private string _stateMessage = "Open an onboarded project and complete analysis to view dependencies.";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _symbolKindFilter = "All";

    [ObservableProperty]
    private string _relationshipFilter = "All";

    [ObservableProperty]
    private GraphDirection _direction = GraphDirection.Both;

    [ObservableProperty]
    private int _neighborhoodDepth;

    [ObservableProperty]
    private bool _showIsolatedNodes = true;

    [ObservableProperty]
    private Guid? _selectedNodeId;

    [ObservableProperty]
    private Guid? _selectedEdgeId;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _fitRequest;

    [ObservableProperty]
    private int _resetRequest;

    public AnalysisViewModel Analysis => _analysisViewModel;

    public DependencyGraphNodeItem? SelectedNode
        => Projection?.Nodes.FirstOrDefault(node => node.Id == SelectedNodeId);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_analysisViewModel.RefreshAnalysisCommand.CanExecute(null))
        {
            await _analysisViewModel.RefreshAnalysisCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SymbolKindFilter = "All";
        RelationshipFilter = "All";
        Direction = GraphDirection.Both;
        NeighborhoodDepth = 0;
        ShowIsolatedNodes = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedNodeId = null;
        SelectedEdgeId = null;
        FitRequest++;
    }

    [RelayCommand]
    private void FitGraph() => FitRequest++;

    [RelayCommand]
    private void ResetView() => ResetRequest++;

    [RelayCommand(CanExecute = nameof(CanOpenSelectedSource))]
    private async Task OpenSelectedSourceAsync()
        => await Details.OpenSourceCommand.ExecuteAsync(null).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanSelectNode))]
    private void ShowIncoming() => Direction = GraphDirection.Incoming;

    [RelayCommand(CanExecute = nameof(CanSelectNode))]
    private void ShowOutgoing() => Direction = GraphDirection.Outgoing;

    [RelayCommand(CanExecute = nameof(CanSelectNode))]
    private void FocusSelected()
    {
        if (SelectedNodeId is not null)
        {
            FitRequest++;
        }
    }

    public void SelectNode(Guid? nodeId)
    {
        SelectedEdgeId = null;
        SelectedNodeId = nodeId;
    }

    public void SelectEdge(Guid? edgeId)
    {
        SelectedNodeId = null;
        SelectedEdgeId = edgeId;
        if (edgeId is null || Projection is null)
        {
            return;
        }

        var edge = Projection.Edges.FirstOrDefault(item => item.Id == edgeId);
        if (edge is not null)
        {
            StateMessage = $"{edge.RelationshipType}: {edge.FromName} → {edge.ToName}";
        }
    }

    partial void OnSelectedNodeIdChanged(Guid? value)
    {
        SelectedEdgeId = null;
        ApplyDetails();
        OpenSelectedSourceCommand.NotifyCanExecuteChanged();
        ShowIncomingCommand.NotifyCanExecuteChanged();
        ShowOutgoingCommand.NotifyCanExecuteChanged();
        FocusSelectedCommand.NotifyCanExecuteChanged();
        ScheduleProjection();
    }

    partial void OnSelectedEdgeIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(SelectedNodeLabel));
        OnPropertyChanged(nameof(SelectedEdge));
        OnPropertyChanged(nameof(SelectedEdgeLabel));
    }

    partial void OnSearchTextChanged(string value) => ScheduleProjection();

    partial void OnSymbolKindFilterChanged(string value) => ScheduleProjection();

    partial void OnRelationshipFilterChanged(string value)
    {
        if (!_isNormalizingFilters)
        {
            ScheduleProjection();
        }
    }

    partial void OnDirectionChanged(GraphDirection value) => ScheduleProjection();

    partial void OnNeighborhoodDepthChanged(int value)
    {
        if (value < 0)
        {
            NeighborhoodDepth = 0;
            return;
        }

        ScheduleProjection();
    }

    partial void OnShowIsolatedNodesChanged(bool value) => ScheduleProjection();

    partial void OnProjectionChanged(DependencyGraphProjection? value)
    {
        RelationshipOptions = BuildRelationshipOptions(_analysisViewModel.WorkspaceSnapshot);
        OnPropertyChanged(nameof(RelationshipOptions));
        OnPropertyChanged(nameof(HasProjection));
        OnPropertyChanged(nameof(ShowFilteredEmpty));
        OnPropertyChanged(nameof(VisibleNodeCount));
        OnPropertyChanged(nameof(VisibleEdgeCount));
        OnPropertyChanged(nameof(TotalNodeCount));
        OnPropertyChanged(nameof(TotalEdgeCount));
        OnPropertyChanged(nameof(CountSummary));
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(SelectedNodeLabel));
        OnPropertyChanged(nameof(SelectedEdge));
        OnPropertyChanged(nameof(SelectedEdgeLabel));
        if (SelectedNodeId is not null && value?.Nodes.All(node => node.Id != SelectedNodeId) == true)
        {
            SelectedNodeId = null;
        }

        ApplyDetails();
        OpenSelectedSourceCommand.NotifyCanExecuteChanged();
        ShowIncomingCommand.NotifyCanExecuteChanged();
        ShowOutgoingCommand.NotifyCanExecuteChanged();
        FocusSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnStateChanged(GraphDisplayState value)
    {
        OnPropertyChanged(nameof(ShowNoProject));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowPartial));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowCancelled));
        OnPropertyChanged(nameof(ShowFilteredEmpty));
    }

    private void OnAnalysisPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AnalysisViewModel.WorkspaceSnapshot) or nameof(AnalysisViewModel.State))
        {
            ScheduleProjection();
        }
    }

    private void ScheduleProjection()
    {
        var snapshot = _analysisViewModel.WorkspaceSnapshot;
        var version = Interlocked.Increment(ref _projectionVersion);
        _projectionCancellation?.Cancel();
        _projectionCancellation?.Dispose();
        _projectionCancellation = null;

        if (snapshot is null)
        {
            Projection = null;
            State = _analysisViewModel.State switch
            {
                AnalysisDisplayState.NoProject => GraphDisplayState.NoProject,
                AnalysisDisplayState.Loading => GraphDisplayState.Loading,
                AnalysisDisplayState.Cancelled => GraphDisplayState.Cancelled,
                AnalysisDisplayState.Error => GraphDisplayState.Error,
                _ => GraphDisplayState.Empty
            };
            StateMessage = _analysisViewModel.StateMessage;
            ErrorMessage = _analysisViewModel.ErrorMessage;
            IsBusy = false;
            ApplyDetails();
            return;
        }

        if (snapshot.ScanId is not Guid scanId)
        {
            Projection = null;
            State = GraphDisplayState.Empty;
            StateMessage = "The selected project has no persisted analysis scan.";
            ErrorMessage = null;
            IsBusy = false;
            ApplyDetails();
            return;
        }

        var source = new CancellationTokenSource();
        _projectionCancellation = source;
        var relationshipOptions = BuildRelationshipOptions(snapshot);
        if (!relationshipOptions.Contains(RelationshipFilter, StringComparer.OrdinalIgnoreCase))
        {
            _isNormalizingFilters = true;
            RelationshipFilter = "All";
            _isNormalizingFilters = false;
        }
        IsBusy = true;
        State = GraphDisplayState.Loading;
        StateMessage = "Building a filtered dependency graph...";
        ErrorMessage = null;
        var coreDirection = Direction switch
        {
            GraphDirection.Incoming => ForgeDesk.Graph.GraphDirection.Incoming,
            GraphDirection.Outgoing => ForgeDesk.Graph.GraphDirection.Outgoing,
            _ => ForgeDesk.Graph.GraphDirection.Both
        };
        var symbolKind = ParseSymbolKind(SymbolKindFilter);
        var options = new CoreGraphQueryOptions(
            scanId,
            new CoreGraphFilterOptions(
                search: SearchText,
                symbolKinds: symbolKind is null ? null : new[] { symbolKind.Value },
                relationshipTypes: RelationshipFilter == "All" ? null : new[] { RelationshipFilter },
                direction: coreDirection,
                neighborhoodDepth: NeighborhoodDepth == 0 ? null : NeighborhoodDepth,
                includeIsolatedNodes: ShowIsolatedNodes),
            SelectedNodeId is Guid selectedId ? new CoreGraphSelection(selectedId) : null);
        _ = BuildProjectionAsync(snapshot, options, source, version);
    }

    private async Task BuildProjectionAsync(
        ProjectAnalysisSnapshot snapshot,
        CoreGraphQueryOptions options,
        CancellationTokenSource source,
        long version)
    {
        try
        {
            var graphData = await _graphService
                .LoadAsync(snapshot.ProjectId, options, source.Token)
                .ConfigureAwait(true);

            var projection = MapGraphData(graphData);

            if (!IsCurrent(source, version))
            {
                return;
            }

            Projection = projection;
            State = graphData is null || graphData.State == CoreGraphDataState.Missing
                ? GraphDisplayState.Error
                : graphData.TotalNodeCount == 0
                ? GraphDisplayState.Empty
                : graphData.State is CoreGraphDataState.Partial or CoreGraphDataState.Stale
                    ? GraphDisplayState.Partial
                    : graphData.State == CoreGraphDataState.Failed
                        ? GraphDisplayState.Error
                        : GraphDisplayState.Ready;
            StateMessage = graphData?.StatusMessage
                ?? (graphData is null
                    ? "The persisted graph query did not return data."
                    : graphData.TotalNodeCount == 0
                    ? "The selected analysis scan has no persisted symbols."
                    : graphData.TotalEdgeCount == 0
                        ? "Symbols are persisted, but this scan has no supported dependency edges."
                        : "Persisted symbols and dependency relationships are ready.");
            ErrorMessage = graphData is not null && graphData.State is (CoreGraphDataState.Missing or CoreGraphDataState.Failed)
                ? graphData.StatusMessage
                : null;
        }
        catch (OperationCanceledException)
        {
            if (!IsCurrent(source, version))
            {
                return;
            }

            State = GraphDisplayState.Cancelled;
            StateMessage = "Graph loading was cancelled.";
        }
        catch (Exception exception)
        {
            if (!IsCurrent(source, version))
            {
                return;
            }

            _logger.LogError(exception, "Unable to load the dependency graph for project {ProjectId}, scan {ScanId}.", snapshot.ProjectId, snapshot.ScanId);
            Projection = null;
            State = GraphDisplayState.Error;
            StateMessage = "The dependency graph could not be built from persisted data.";
            ErrorMessage = "Refresh the analysis results and try again.";
        }
        finally
        {
            if (IsCurrent(source, version))
            {
                _projectionCancellation = null;
                IsBusy = false;
            }

            source.Dispose();
        }
    }

    private void ApplyDetails()
    {
        var snapshot = _analysisViewModel.WorkspaceSnapshot;
        var selected = snapshot?.PersistedSymbols.FirstOrDefault(symbol => symbol.Id == SelectedNodeId);
        var incoming = selected is null
            ? 0
            : snapshot!.PersistedDependencies.Count(dependency => dependency.TargetSymbolId == selected.Id);
        var outgoing = selected is null
            ? 0
            : snapshot!.PersistedDependencies.Count(dependency => dependency.SourceSymbolId == selected.Id);
        var findings = selected is null
            ? 0
            : snapshot!.Findings.Count(finding => finding.SymbolId == selected.Id);
        Details.Apply(selected, snapshot?.RootPath, incoming, outgoing, findings);
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(SelectedNodeLabel));
    }

    private static DependencyGraphProjection MapGraphData(CoreGraphData? graphData)
    {
        if (graphData is null)
        {
            return new DependencyGraphProjection(
                Array.Empty<DependencyGraphNodeItem>(),
                Array.Empty<DependencyGraphEdgeItem>(),
                0,
                0,
                0,
                true,
                "The persisted graph query did not return data.");
        }

        var nodeNames = graphData.Nodes
            .Where(node => Guid.TryParse(node.Id, out _))
            .ToDictionary(node => Guid.Parse(node.Id), node => node.Label);
        var nodes = graphData.Nodes
            .Where(node => Guid.TryParse(node.Id, out _))
            .Select(node => new DependencyGraphNodeItem(
                Guid.Parse(node.Id),
                node.DisplayName,
                node.SymbolKey ?? node.FullyQualifiedName ?? node.DisplayName,
                node.SymbolKind,
                node.RelativePath,
                node.StartLine,
                node.EndLine,
                node.X,
                node.Y,
                node.IsSelected,
                node.IsNeighbor,
                node.IsIsolated))
            .ToArray();
        var edges = graphData.Edges
            .Where(edge => Guid.TryParse(edge.FromId, out _) && Guid.TryParse(edge.ToId, out _))
            .Select(edge => new DependencyGraphEdgeItem(
                edge.DependencyId ?? DeterministicEdgeId(edge),
                Guid.Parse(edge.FromId),
                Guid.Parse(edge.ToId),
                edge.RelationshipType,
                edge.IsResolved,
                edge.IsHighlighted,
                nodeNames.GetValueOrDefault(Guid.Parse(edge.FromId), edge.FromId),
                nodeNames.GetValueOrDefault(Guid.Parse(edge.ToId), edge.ToId)))
            .ToArray();
        var isPartial = graphData.State is CoreGraphDataState.Partial or CoreGraphDataState.Stale
            || graphData.UnresolvedDependencyCount > 0;
        return new DependencyGraphProjection(
            nodes,
            edges,
            graphData.TotalNodeCount,
            graphData.TotalEdgeCount,
            graphData.UnresolvedDependencyCount,
            isPartial,
            graphData.StatusMessage);
    }

    private static Guid DeterministicEdgeId(GraphEdge edge)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{edge.ScanId:D}|{edge.FromId}|{edge.ToId}|{edge.RelationshipType}"));
        return new Guid(bytes[..16]);
    }

    private bool IsCurrent(CancellationTokenSource source, long version)
        => ReferenceEquals(_projectionCancellation, source)
            && version == Volatile.Read(ref _projectionVersion)
            && !source.IsCancellationRequested;

    private bool CanOpenSelectedSource() => Details.OpenSourceCommand.CanExecute(null);

    private bool CanSelectNode() => SelectedNodeId is not null;

    private void OnIncomingRequested(object? sender, EventArgs args) => ShowIncomingCommand.Execute(null);

    private void OnOutgoingRequested(object? sender, EventArgs args) => ShowOutgoingCommand.Execute(null);

    private void OnRelatedFindingsRequested(object? sender, EventArgs args)
        => RelatedFindingsRequested?.Invoke(this, EventArgs.Empty);

    private static SymbolKind? ParseSymbolKind(string value)
        => value is "All" or "" ? null : Enum.TryParse<SymbolKind>(value, out var kind) ? kind : null;

    private static IReadOnlyList<string> BuildRelationshipOptions(ProjectAnalysisSnapshot? snapshot)
        => new[] { "All" }
            .Concat(snapshot?.PersistedDependencies
                .Select(dependency => dependency.RelationshipType)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                ?? Enumerable.Empty<string>())
            .ToArray();
}
