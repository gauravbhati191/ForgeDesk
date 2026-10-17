using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Domain;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Presentation.ViewModels;

public enum FindingSortOption
{
    Severity,
    File,
    Rule,
    SourceLine
}

public enum IssueExplorerDisplayState
{
    NoProject,
    Loading,
    Empty,
    Ready,
    Partial,
    Error,
    Cancelled
}

/// <summary>Filters, sorts, and connects persisted findings to graph and source views.</summary>
public sealed partial class IssueExplorerViewModel : ObservableObject
{
    private readonly AnalysisViewModel _analysisViewModel;
    private readonly DependencyGraphViewModel _graphViewModel;
    private readonly SourceViewerViewModel _sourceViewer;
    private readonly ILogger<IssueExplorerViewModel> _logger;
    private CancellationTokenSource? _filterCancellation;
    private long _filterVersion;
    private bool _isNormalizingFilters;

    public IssueExplorerViewModel(
        AnalysisViewModel analysisViewModel,
        DependencyGraphViewModel graphViewModel,
        SourceViewerViewModel sourceViewer,
        ILogger<IssueExplorerViewModel> logger)
    {
        _analysisViewModel = analysisViewModel ?? throw new ArgumentNullException(nameof(analysisViewModel));
        _graphViewModel = graphViewModel ?? throw new ArgumentNullException(nameof(graphViewModel));
        _sourceViewer = sourceViewer ?? throw new ArgumentNullException(nameof(sourceViewer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _analysisViewModel.PropertyChanged += OnAnalysisPropertyChanged;
    }

    public ObservableCollection<AnalysisFindingItem> VisibleFindings { get; } = [];

    public SourceViewerViewModel SourceViewer => _sourceViewer;

    public IReadOnlyList<string> SeverityOptions { get; } =
        new[] { "All", nameof(FindingSeverity.Info), nameof(FindingSeverity.Warning), nameof(FindingSeverity.Error), nameof(FindingSeverity.Critical) };

    public IReadOnlyList<FindingSortOption> SortOptions { get; } = Enum.GetValues<FindingSortOption>();

    public IReadOnlyList<string> RuleOptions { get; private set; } = new[] { "All" };

    public IReadOnlyList<string> FileOptions { get; private set; } = new[] { "All" };

    public bool ShowNoProject => State == IssueExplorerDisplayState.NoProject;

    public bool ShowLoading => State == IssueExplorerDisplayState.Loading;

    public bool ShowEmpty => State == IssueExplorerDisplayState.Empty;

    public bool ShowResults => State is IssueExplorerDisplayState.Ready or IssueExplorerDisplayState.Partial;

    public bool ShowPartial => State == IssueExplorerDisplayState.Partial;

    public bool ShowError => State == IssueExplorerDisplayState.Error;

    public bool ShowCancelled => State == IssueExplorerDisplayState.Cancelled;

    public bool ShowFilteredEmpty => ShowResults && TotalFindingCount > 0 && FilteredFindingCount == 0;

    public string CountSummary => $"{FilteredFindingCount:N0} of {TotalFindingCount:N0} persisted finding{(TotalFindingCount == 1 ? string.Empty : "s")} visible";

    public string SelectedFindingLocation => SelectedFinding?.LocationLabel ?? "No finding selected";

    public string SelectedFindingDetails => SelectedFinding is null
        ? "Select an issue to inspect its message and source location."
        : $"{SelectedFinding.SeverityLabel} · {SelectedFinding.RuleId} · {SelectedFinding.Message}";

    public bool HasRelatedSymbolFilter => RelatedSymbolId is not null;

    public string RelatedSymbolLabel
    {
        get
        {
            if (RelatedSymbolId is not Guid symbolId)
            {
                return string.Empty;
            }

            var symbol = _analysisViewModel.WorkspaceSnapshot?.PersistedSymbols
                .FirstOrDefault(item => item.Id == symbolId);
            return symbol is null
                ? "Related symbol"
                : $"Related to: {symbol.Name}";
        }
    }

    [ObservableProperty]
    private IssueExplorerDisplayState _state = IssueExplorerDisplayState.NoProject;

    [ObservableProperty]
    private string _stateMessage = "Open an onboarded project to view persisted findings.";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _severityFilter = "All";

    [ObservableProperty]
    private string _ruleFilter = "All";

    [ObservableProperty]
    private string _fileFilter = "All";

    [ObservableProperty]
    private FindingSortOption _sortBy = FindingSortOption.Severity;

    [ObservableProperty]
    private AnalysisFindingItem? _selectedFinding;

    [ObservableProperty]
    private int _totalFindingCount;

    [ObservableProperty]
    private int _filteredFindingCount;

    [ObservableProperty]
    private Guid? _relatedSymbolId;

    [ObservableProperty]
    private bool _isBusy;

    public AnalysisViewModel Analysis => _analysisViewModel;

    public event EventHandler? NavigateToGraphRequested;

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
        SeverityFilter = "All";
        RuleFilter = "All";
        FileFilter = "All";
        SortBy = FindingSortOption.Severity;
        RelatedSymbolId = null;
    }

    /// <summary>Shows findings associated with a selected graph symbol.</summary>
    public void ShowFindingsForSymbol(Guid symbolId)
    {
        if (symbolId == Guid.Empty)
        {
            return;
        }

        RelatedSymbolId = symbolId;
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedSource))]
    private async Task OpenSelectedSourceAsync()
    {
        if (SelectedFinding is null)
        {
            return;
        }

        await _sourceViewer
            .OpenAsync(_analysisViewModel.WorkspaceSnapshot?.RootPath, SelectedFinding.RelativePath, SelectedFinding.StartLine)
            .ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToGraph))]
    private void NavigateToGraph()
    {
        if (SelectedFinding?.SymbolId is Guid symbolId)
        {
            _graphViewModel.SelectNode(symbolId);
            NavigateToGraphRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnSelectedFindingChanged(AnalysisFindingItem? value)
    {
        OnPropertyChanged(nameof(SelectedFindingLocation));
        OnPropertyChanged(nameof(SelectedFindingDetails));
        OpenSelectedSourceCommand.NotifyCanExecuteChanged();
        NavigateToGraphCommand.NotifyCanExecuteChanged();
    }

    partial void OnRelatedSymbolIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(HasRelatedSymbolFilter));
        OnPropertyChanged(nameof(RelatedSymbolLabel));
        if (!_isNormalizingFilters)
        {
            ScheduleFilter();
        }
    }

    partial void OnSearchTextChanged(string value) => ScheduleFilter();

    partial void OnSeverityFilterChanged(string value) => ScheduleFilter();

    partial void OnRuleFilterChanged(string value)
    {
        if (!_isNormalizingFilters)
        {
            ScheduleFilter();
        }
    }

    partial void OnFileFilterChanged(string value)
    {
        if (!_isNormalizingFilters)
        {
            ScheduleFilter();
        }
    }

    partial void OnSortByChanged(FindingSortOption value) => ScheduleFilter();

    partial void OnStateChanged(IssueExplorerDisplayState value)
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

    partial void OnTotalFindingCountChanged(int value)
    {
        OnPropertyChanged(nameof(CountSummary));
        OnPropertyChanged(nameof(ShowFilteredEmpty));
    }

    partial void OnFilteredFindingCountChanged(int value)
    {
        OnPropertyChanged(nameof(CountSummary));
        OnPropertyChanged(nameof(ShowFilteredEmpty));
    }

    private void OnAnalysisPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AnalysisViewModel.WorkspaceSnapshot) or nameof(AnalysisViewModel.State))
        {
            ScheduleFilter();
        }
    }

    private void ScheduleFilter()
    {
        var snapshot = _analysisViewModel.WorkspaceSnapshot;
        var version = Interlocked.Increment(ref _filterVersion);
        _filterCancellation?.Cancel();
        _filterCancellation?.Dispose();
        _filterCancellation = null;

        if (snapshot is null)
        {
            VisibleFindings.Clear();
            TotalFindingCount = 0;
            FilteredFindingCount = 0;
            RuleOptions = new[] { "All" };
            FileOptions = new[] { "All" };
            OnPropertyChanged(nameof(RuleOptions));
            OnPropertyChanged(nameof(FileOptions));
            SelectedFinding = null;
            State = _analysisViewModel.State switch
            {
                AnalysisDisplayState.NoProject => IssueExplorerDisplayState.NoProject,
                AnalysisDisplayState.Loading => IssueExplorerDisplayState.Loading,
                AnalysisDisplayState.Cancelled => IssueExplorerDisplayState.Cancelled,
                AnalysisDisplayState.Error => IssueExplorerDisplayState.Error,
                _ => IssueExplorerDisplayState.Empty
            };
            StateMessage = _analysisViewModel.StateMessage;
            ErrorMessage = _analysisViewModel.ErrorMessage;
            IsBusy = false;
            return;
        }

        if (RelatedSymbolId is Guid relatedSymbolId
            && snapshot.PersistedSymbols.All(symbol => symbol.Id != relatedSymbolId))
        {
            _isNormalizingFilters = true;
            RelatedSymbolId = null;
            _isNormalizingFilters = false;
        }

        var source = new CancellationTokenSource();
        _filterCancellation = source;
        IsBusy = true;
        State = IssueExplorerDisplayState.Loading;
        StateMessage = "Filtering persisted findings...";
        ErrorMessage = null;
        OnPropertyChanged(nameof(RelatedSymbolLabel));
        UpdateFilterOptions(snapshot);
        _ = FilterAsync(snapshot, source, version);
    }

    private async Task FilterAsync(
        ProjectAnalysisSnapshot snapshot,
        CancellationTokenSource source,
        long version)
    {
        try
        {
            var result = await Task.Run(
                    () => FilterFindings(snapshot.Findings, SearchText, SeverityFilter, RuleFilter, FileFilter, SortBy, RelatedSymbolId, source.Token),
                    source.Token)
                .ConfigureAwait(true);
            if (!IsCurrent(source, version))
            {
                return;
            }

            VisibleFindings.Clear();
            foreach (var finding in result)
            {
                VisibleFindings.Add(finding);
            }

            TotalFindingCount = snapshot.Findings.Count;
            FilteredFindingCount = result.Count;
            var analysisNotStarted = snapshot.AnalysisStageStatus is null or AnalysisStatus.NotStarted;
            State = snapshot.Findings.Count == 0
                ? analysisNotStarted || snapshot.AnalysisStageStatus == AnalysisStatus.Completed && !snapshot.IsPartial
                    ? IssueExplorerDisplayState.Empty
                    : IssueExplorerDisplayState.Partial
                : snapshot.IsPartial || snapshot.AnalysisStageStatus != AnalysisStatus.Completed
                    ? IssueExplorerDisplayState.Partial
                    : IssueExplorerDisplayState.Ready;
            StateMessage = snapshot.Findings.Count == 0
                ? analysisNotStarted
                    ? "No semantic analysis has been run for this scan."
                    : snapshot.AnalysisStageStatus == AnalysisStatus.Completed && !snapshot.IsPartial
                        ? "The selected analysis completed without persisted findings."
                        : "The analysis is incomplete; no persisted findings are currently available."
                : result.Count == 0
                    ? "No persisted findings match the current filters."
                    : "Persisted findings are ready for inspection.";
            ErrorMessage = null;
            if (SelectedFinding is not null && result.All(item => item.Id != SelectedFinding.Id))
            {
                SelectedFinding = null;
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsCurrent(source, version))
            {
                return;
            }

            State = IssueExplorerDisplayState.Cancelled;
            StateMessage = "Finding filtering was cancelled.";
        }
        catch (Exception exception)
        {
            if (!IsCurrent(source, version))
            {
                return;
            }

            _logger.LogError(exception, "Unable to filter findings for project {ProjectId}.", snapshot.ProjectId);
            State = IssueExplorerDisplayState.Error;
            StateMessage = "The persisted findings could not be filtered.";
            ErrorMessage = "Clear the filters or refresh the analysis results and try again.";
        }
        finally
        {
            if (IsCurrent(source, version))
            {
                _filterCancellation = null;
                IsBusy = false;
            }

            source.Dispose();
        }
    }

    private void UpdateFilterOptions(ProjectAnalysisSnapshot snapshot)
    {
        RuleOptions = new[] { "All" }
            .Concat(snapshot.Findings.Select(finding => finding.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        FileOptions = new[] { "All" }
            .Concat(snapshot.Findings.Select(finding => finding.RelativePath).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (!RuleOptions.Contains(RuleFilter, StringComparer.OrdinalIgnoreCase))
        {
            _isNormalizingFilters = true;
            RuleFilter = "All";
            _isNormalizingFilters = false;
        }

        if (!FileOptions.Contains(FileFilter, StringComparer.OrdinalIgnoreCase))
        {
            _isNormalizingFilters = true;
            FileFilter = "All";
            _isNormalizingFilters = false;
        }
        OnPropertyChanged(nameof(RuleOptions));
        OnPropertyChanged(nameof(FileOptions));
    }

    private static IReadOnlyList<AnalysisFindingItem> FilterFindings(
        IReadOnlyList<AnalysisFindingItem> findings,
        string searchText,
        string severityFilter,
        string ruleFilter,
        string fileFilter,
        FindingSortOption sortBy,
        Guid? relatedSymbolId,
        CancellationToken cancellationToken)
    {
        var query = findings.AsEnumerable();
        if (Enum.TryParse<FindingSeverity>(severityFilter, out var severity))
        {
            query = query.Where(finding => finding.Severity == severity);
        }

        if (!string.Equals(ruleFilter, "All", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ruleFilter))
        {
            query = query.Where(finding => string.Equals(finding.RuleId, ruleFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(fileFilter, "All", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(fileFilter))
        {
            query = query.Where(finding => string.Equals(finding.RelativePath, fileFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(finding => finding.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || finding.RuleId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || (finding.RelativePath?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false)
                || (finding.SymbolName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (relatedSymbolId is Guid symbolId)
        {
            query = query.Where(finding => finding.SymbolId == symbolId);
        }

        query = sortBy switch
        {
            FindingSortOption.File => query.OrderBy(finding => finding.RelativePath ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(finding => finding.StartLine),
            FindingSortOption.Rule => query.OrderBy(finding => finding.RuleId, StringComparer.OrdinalIgnoreCase).ThenBy(finding => finding.RelativePath ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            FindingSortOption.SourceLine => query.OrderBy(finding => finding.StartLine ?? int.MaxValue).ThenBy(finding => finding.RelativePath ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(finding => finding.Severity).ThenBy(finding => finding.RelativePath ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(finding => finding.StartLine ?? int.MaxValue)
        };

        var output = new List<AnalysisFindingItem>();
        foreach (var finding in query)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Add(finding);
        }

        return output;
    }

    private bool IsCurrent(CancellationTokenSource source, long version)
        => ReferenceEquals(_filterCancellation, source)
            && version == Volatile.Read(ref _filterVersion)
            && !source.IsCancellationRequested;

    private bool CanOpenSelectedSource()
        => SelectedFinding is not null
            && !string.IsNullOrWhiteSpace(SelectedFinding.RelativePath)
            && !string.IsNullOrWhiteSpace(_analysisViewModel.WorkspaceSnapshot?.RootPath);

    private bool CanNavigateToGraph() => SelectedFinding?.SymbolId is not null;
}
