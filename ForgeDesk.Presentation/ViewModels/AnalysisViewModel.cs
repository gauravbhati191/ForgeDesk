using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Domain;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Presentation.ViewModels;

public enum AnalysisDisplayState
{
    NoProject,
    Loading,
    Empty,
    Ready,
    Partial,
    Error,
    Cancelled
}

/// <summary>Starts and coordinates project analysis state for all Phase 2 analysis pages.</summary>
public sealed partial class AnalysisViewModel : ObservableObject
{
    private readonly IProjectAnalysisService _analysisService;
    private readonly ILogger<AnalysisViewModel> _logger;
    private CancellationTokenSource? _operationCancellation;
    private Guid? _selectedProjectId;
    private Guid? _selectedScanId;
    private ScanStatus? _selectedScanStatus;
    private AnalysisStatus? _selectedAnalysisStatus;

    public AnalysisViewModel(
        IProjectAnalysisService analysisService,
        ILogger<AnalysisViewModel> logger)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<AnalysisFindingItem> Findings { get; } = [];

    public bool HasProject => _selectedProjectId is not null;

    public bool HasFindings => Findings.Count > 0;

    public bool ShowNoProject => State == AnalysisDisplayState.NoProject;

    public bool ShowLoading => State == AnalysisDisplayState.Loading;

    public bool ShowEmpty => State == AnalysisDisplayState.Empty;

    public bool ShowResults => State is AnalysisDisplayState.Ready or AnalysisDisplayState.Partial;

    public bool ShowPartial => State == AnalysisDisplayState.Partial;

    public bool ShowError => State == AnalysisDisplayState.Error;

    public bool ShowCancelled => State == AnalysisDisplayState.Cancelled;

    public bool ShowFindingsEmpty => ShowResults && !HasFindings;

    [ObservableProperty]
    private AnalysisDisplayState _state = AnalysisDisplayState.NoProject;

    [ObservableProperty]
    private string _stateMessage = "Open an onboarded project to view persisted analysis.";

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _selectedProjectName;

    [ObservableProperty]
    private string _scanStatusLabel = "No scan";

    [ObservableProperty]
    private int _filesDiscovered;

    [ObservableProperty]
    private int _filesProcessed;

    [ObservableProperty]
    private int _symbolCount;

    [ObservableProperty]
    private int _dependencyCount;

    [ObservableProperty]
    private int _findingCount;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Full persisted read model shared by the graph and issue explorer pages.</summary>
    [ObservableProperty]
    private ProjectAnalysisSnapshot? _workspaceSnapshot;

    public string SelectedProjectLabel
    {
        get
        {
            var projectName = SelectedProjectName;
            return string.IsNullOrWhiteSpace(projectName) ? "No project selected" : projectName;
        }
    }

    public string FilesSummary => FilesDiscovered == 0
        ? "No source-file progress recorded"
        : $"{FilesProcessed} of {FilesDiscovered} discovered files processed";

    public string ResultsSummary => HasFindings
        ? $"{FindingCount} persisted finding{(FindingCount == 1 ? string.Empty : "s")}" 
        : "No persisted findings recorded";

    [RelayCommand(CanExecute = nameof(CanRefreshAnalysis))]
    private async Task RefreshAnalysisAsync()
    {
        await LoadSelectedProjectAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanStartAnalysis))]
    private async Task StartAnalysisAsync()
    {
        if (_selectedProjectId is not Guid projectId || _selectedScanId is not Guid scanId)
        {
            State = AnalysisDisplayState.Error;
            StateMessage = "Choose an onboarded project with a completed discovery scan first.";
            ErrorMessage = "No completed scan is selected for analysis.";
            return;
        }

        using var operation = BeginOperation("Starting semantic analysis...");
        try
        {
            var snapshot = await _analysisService
                .AnalyzeAsync(projectId, scanId, CreateProgress(operation), operation.Token)
                .ConfigureAwait(true);

            if (!ReferenceEquals(_operationCancellation, operation.Source))
            {
                return;
            }

            if (snapshot is null)
            {
                State = AnalysisDisplayState.Error;
                StateMessage = "The selected scan is no longer available for analysis.";
                ErrorMessage = "The selected project or scan could not be found in local persistence.";
                return;
            }

            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            if (!ReferenceEquals(_operationCancellation, operation.Source))
            {
                return;
            }

            State = AnalysisDisplayState.Cancelled;
            StateMessage = "Semantic analysis was cancelled.";
            ErrorMessage = null;
            ProgressMessage = "Analysis cancelled. Persisted status was retained.";
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_operationCancellation, operation.Source))
            {
                return;
            }

            _logger.LogError(exception, "Unable to analyze project {ProjectId}, scan {ScanId}.", projectId, scanId);
            State = AnalysisDisplayState.Error;
            StateMessage = "Semantic analysis could not be started.";
            ErrorMessage = "Analysis failed before a persisted terminal status could be read. Check the application log for details.";
        }
    }

    [RelayCommand]
    private void CancelAnalysis() => _operationCancellation?.Cancel();

    public async Task LoadForProjectAsync(ForgeDesk.Application.ProjectDto project, Guid? scanId = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        _selectedProjectId = project.Id;
        _selectedScanId = scanId;
        _selectedScanStatus = null;
        _selectedAnalysisStatus = null;
        SelectedProjectName = project.Name;
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(SelectedProjectLabel));
        RefreshAnalysisCommand.NotifyCanExecuteChanged();
        StartAnalysisCommand.NotifyCanExecuteChanged();
        await LoadSelectedProjectAsync().ConfigureAwait(true);
    }

    public void ClearSelection()
    {
        var operation = _operationCancellation;
        _operationCancellation = null;
        operation?.Cancel();
        IsBusy = false;
        _selectedProjectId = null;
        _selectedScanId = null;
        _selectedScanStatus = null;
        _selectedAnalysisStatus = null;
        SelectedProjectName = null;
        WorkspaceSnapshot = null;
        ResetResults();
        State = AnalysisDisplayState.NoProject;
        StateMessage = "Open an onboarded project to view persisted analysis.";
        ErrorMessage = null;
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(SelectedProjectLabel));
        RefreshAnalysisCommand.NotifyCanExecuteChanged();
        StartAnalysisCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadSelectedProjectAsync()
    {
        if (_selectedProjectId is not Guid projectId)
        {
            ClearSelection();
            return;
        }

        using var operation = BeginOperation();
        try
        {
            var snapshot = await _analysisService
                .LoadAsync(projectId, CreateProgress(operation), operation.Token)
                .ConfigureAwait(true);

            if (!ReferenceEquals(_operationCancellation, operation.Source))
            {
                return;
            }

            if (snapshot is null)
            {
                State = AnalysisDisplayState.Error;
                StateMessage = "The selected project is no longer available.";
                ErrorMessage = "The selected project could not be found in local persistence.";
                return;
            }

            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            if (!ReferenceEquals(_operationCancellation, operation.Source))
            {
                return;
            }

            State = AnalysisDisplayState.Cancelled;
            StateMessage = "Refreshing persisted analysis was cancelled.";
            ErrorMessage = null;
            ProgressMessage = "Refresh cancelled. Persisted project data was not changed.";
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_operationCancellation, operation.Source))
            {
                return;
            }

            _logger.LogError(exception, "Unable to load persisted analysis for project {ProjectId}.", projectId);
            State = AnalysisDisplayState.Error;
            StateMessage = "The persisted analysis could not be loaded.";
            ErrorMessage = "Analysis data could not be read. Check the application log for details.";
        }
    }

    private IProgress<AnalysisProgress> CreateProgress(OperationScope operation)
        => new Progress<AnalysisProgress>(update =>
        {
            if (!ReferenceEquals(_operationCancellation, operation.Source))
            {
                return;
            }

            ProgressMessage = update.Detail;
            ProgressPercent = update.Percent;
        });

    private void ApplySnapshot(ProjectAnalysisSnapshot snapshot)
    {
        WorkspaceSnapshot = snapshot;
        _selectedScanId = snapshot.ScanId;
        _selectedScanStatus = snapshot.ScanStatus;
        _selectedAnalysisStatus = snapshot.AnalysisStageStatus;
        SelectedProjectName = snapshot.ProjectName;
        ScanStatusLabel = $"Scan: {snapshot.ScanStatusLabel} · Analysis: {snapshot.AnalysisStatusLabel}";
        FilesDiscovered = snapshot.FilesDiscovered;
        FilesProcessed = snapshot.FilesProcessed;
        SymbolCount = snapshot.SymbolCount;
        DependencyCount = snapshot.DependencyCount;
        FindingCount = snapshot.FindingCount;
        Findings.Clear();
        foreach (var finding in snapshot.Findings)
        {
            Findings.Add(finding);
        }
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(FilesSummary));
        OnPropertyChanged(nameof(ResultsSummary));
        StartAnalysisCommand.NotifyCanExecuteChanged();

        ErrorMessage = null;
        if (snapshot.ScanId is null)
        {
            State = AnalysisDisplayState.Empty;
            StateMessage = "This project has no persisted scan yet.";
            return;
        }

        if (snapshot.ScanStatus == ForgeDesk.Domain.ScanStatus.Failed && !snapshot.HasPersistedResults)
        {
            State = AnalysisDisplayState.Error;
            StateMessage = "The latest project scan failed before analysis results were available.";
            ErrorMessage = snapshot.ScanErrorMessage ?? "The latest scan did not complete.";
            return;
        }

        if (snapshot.IsPartial)
        {
            State = AnalysisDisplayState.Partial;
            var partialMessage = snapshot.HasPersistedResults
                ? "Some persisted analysis results are available, but the latest discovery scan is not complete."
                : "The latest discovery scan is incomplete, so analysis cannot start yet.";
            StateMessage = string.IsNullOrWhiteSpace(snapshot.ScanErrorMessage)
                ? partialMessage
                : $"{partialMessage} {snapshot.ScanErrorMessage}";
            return;
        }

        if (snapshot.AnalysisStageStatus == ForgeDesk.Domain.AnalysisStatus.Failed)
        {
            State = snapshot.HasPersistedResults ? AnalysisDisplayState.Partial : AnalysisDisplayState.Error;
            var failureMessage = snapshot.AnalysisErrorMessage ?? "The analysis attempt did not complete.";
            StateMessage = snapshot.HasPersistedResults
                ? $"The latest analysis attempt failed; previously persisted results are shown. {failureMessage}"
                : "The latest scan completed, but semantic analysis failed.";
            ErrorMessage = failureMessage;
            return;
        }

        if (snapshot.AnalysisStageStatus == ForgeDesk.Domain.AnalysisStatus.Cancelled)
        {
            State = snapshot.HasPersistedResults ? AnalysisDisplayState.Partial : AnalysisDisplayState.Cancelled;
            StateMessage = snapshot.HasPersistedResults
                ? "The latest analysis attempt was cancelled; previously persisted results are shown."
                : "The latest analysis attempt was cancelled before results were persisted.";
            return;
        }

        if (snapshot.AnalysisStageStatus == ForgeDesk.Domain.AnalysisStatus.Running)
        {
            State = AnalysisDisplayState.Partial;
            StateMessage = snapshot.HasPersistedResults
                ? "Analysis is running; previously persisted results are shown."
                : "Analysis is running and results are not complete yet.";
            return;
        }

        if ((snapshot.AnalysisStageStatus is null or ForgeDesk.Domain.AnalysisStatus.NotStarted)
            && snapshot.HasPersistedResults)
        {
            State = AnalysisDisplayState.Partial;
            StateMessage = "Persisted analysis rows exist, but the scan status does not mark analysis as completed. Start analysis to refresh the result.";
            return;
        }

        if (!snapshot.HasPersistedResults)
        {
            State = AnalysisDisplayState.Empty;
            StateMessage = snapshot.AnalysisStageStatus == ForgeDesk.Domain.AnalysisStatus.NotStarted
                ? "The latest completed discovery scan has not been analyzed yet."
                : "The latest completed analysis has no persisted symbols, dependencies, or findings.";
            return;
        }

        State = AnalysisDisplayState.Ready;
        StateMessage = "Persisted analysis results loaded for the latest completed scan.";
    }

    private OperationScope BeginOperation(string message = "Loading persisted analysis...")
    {
        _operationCancellation?.Cancel();
        var source = new CancellationTokenSource();
        _operationCancellation = source;
        ResetResults();
        State = AnalysisDisplayState.Loading;
        StateMessage = message;
        ProgressMessage = "Preparing analysis data...";
        ProgressPercent = 0;
        ErrorMessage = null;
        IsBusy = true;
        WorkspaceSnapshot = null;
        RefreshAnalysisCommand.NotifyCanExecuteChanged();
        StartAnalysisCommand.NotifyCanExecuteChanged();
        return new OperationScope(this, source);
    }

    private void EndOperation(CancellationTokenSource source)
    {
        if (ReferenceEquals(_operationCancellation, source))
        {
            _operationCancellation = null;
            IsBusy = false;
            RefreshAnalysisCommand.NotifyCanExecuteChanged();
            StartAnalysisCommand.NotifyCanExecuteChanged();
        }

        source.Dispose();
    }

    private void ResetResults()
    {
        FilesDiscovered = 0;
        FilesProcessed = 0;
        SymbolCount = 0;
        DependencyCount = 0;
        FindingCount = 0;
        ScanStatusLabel = "No scan";
        Findings.Clear();
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(FilesSummary));
        OnPropertyChanged(nameof(ResultsSummary));
    }

    private bool CanRefreshAnalysis() => _selectedProjectId is not null && !IsBusy;

    private bool CanStartAnalysis()
        => _selectedProjectId is not null
            && _selectedScanId is not null
            && _selectedScanStatus == ScanStatus.Completed
            && _selectedAnalysisStatus != AnalysisStatus.Running
            && !IsBusy;

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(ShowNoProject));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowPartial));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowCancelled));
        OnPropertyChanged(nameof(ShowFindingsEmpty));
    }

    partial void OnStateChanged(AnalysisDisplayState value) => RaiseStateProperties();

    partial void OnIsBusyChanged(bool value)
    {
        RefreshAnalysisCommand.NotifyCanExecuteChanged();
        StartAnalysisCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProjectNameChanged(string? value) => OnPropertyChanged(nameof(SelectedProjectLabel));

    private sealed class OperationScope : IDisposable
    {
        private readonly AnalysisViewModel _owner;
        public OperationScope(AnalysisViewModel owner, CancellationTokenSource source)
        {
            _owner = owner;
            Source = source;
        }

        public CancellationTokenSource Source { get; }
        public CancellationToken Token => Source.Token;
        public void Dispose() => _owner.EndOperation(Source);
    }
}
