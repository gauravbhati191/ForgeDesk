using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Application;
using ForgeDesk.Domain;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Presentation.ViewModels;

/// <summary>Coordinates selected-run report generation, safe exports, and read-only comparison.</summary>
public sealed partial class ReportsViewModel : ObservableObject, IDisposable
{
    private readonly AnalysisViewModel _analysisViewModel;
    private readonly IProjectReportService _reportService;
    private readonly IReportPathPicker _pathPicker;
    private readonly ILogger<ReportsViewModel> _logger;
    private CancellationTokenSource? _operationCancellation;
    private long _operationVersion;
    private bool _disposed;

    public ReportsViewModel(
        AnalysisViewModel analysisViewModel,
        IProjectReportService reportService,
        IReportPathPicker pathPicker,
        ILogger<ReportsViewModel> logger)
    {
        _analysisViewModel = analysisViewModel ?? throw new ArgumentNullException(nameof(analysisViewModel));
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _pathPicker = pathPicker ?? throw new ArgumentNullException(nameof(pathPicker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _analysisViewModel.PropertyChanged += OnAnalysisPropertyChanged;
        ApplyMissingWorkspaceState();
    }

    public AnalysisViewModel Analysis => _analysisViewModel;

    public ObservableCollection<ReportRunItem> AvailableRuns { get; } = [];

    public bool ShowNoProject => State == ReportsDisplayState.NoProject;

    public bool ShowLoading => State == ReportsDisplayState.Loading;

    public bool ShowEmpty => State == ReportsDisplayState.Empty;

    public bool ShowResults => State == ReportsDisplayState.Ready;

    public bool ShowError => State == ReportsDisplayState.Error;

    public bool ShowCancelled => State == ReportsDisplayState.Cancelled;

    public bool HasReport => Report is not null;

    public bool HasComparison => Comparison is not null;

    public bool CanCompareRuns => SelectedBaselineRun is not null
        && CurrentScanId is Guid currentScanId
        && SelectedBaselineRun.ScanId != currentScanId
        && !IsBusy;

    public Guid? CurrentScanId => _analysisViewModel.WorkspaceSnapshot?.ScanId;

    public string CurrentScanLabel => CurrentScanId is Guid scanId
        ? $"Current run: {scanId:D}"
        : "No current analysis run selected";

    public string ReportSummaryLabel => Report is null
        ? "No report built"
        : $"{Report.Summary.SourceFileCount:N0} source files · {FormatCount(Report.Summary.SymbolCount)} symbols · {FormatCount(Report.Summary.DependencyCount)} dependencies · {FormatCount(Report.Summary.FindingCount)} findings";

    public string ReportStatusLabel => Report is null
        ? "No report available"
        : $"{Report.Scan.AnalysisStatus} · scan {Report.Scan.ScanId:D}";

    public string SeveritySummaryLabel => Report?.Summary.FindingsBySeverity.Count is not > 0
        ? "No severity groups"
        : string.Join(" · ", Report.Summary.FindingsBySeverity.Select(pair => $"{pair.Key}: {pair.Value:N0}"));

    public string LimitationsLabel => Report is null
        ? "No limitations reported"
        : Report.Limitations.Count == 0
            ? "No report limitations"
            : $"{Report.Limitations.Count:N0} limitation{(Report.Limitations.Count == 1 ? string.Empty : "s")}";

    public string GitContextLabel => Report?.GitContext is not { } git
        ? "Git context not included"
        : git.State switch
        {
            GitRepositoryState.NotRepository => "No local Git repository detected",
            GitRepositoryState.Unreadable => "Git repository unreadable",
            _ => $"Git {git.BranchName ?? (git.IsDetached ? "detached HEAD" : "no branch")} · {(git.IsDirty ? "dirty" : "clean")}"
        };

    public string ComparisonSummaryLabel => Comparison is null
        ? "No comparison built"
        : $"{Comparison.NewCount:N0} new · {Comparison.RemovedCount:N0} removed · {Comparison.ChangedCount:N0} changed · {Comparison.UnchangedCount:N0} unchanged";

    public string ComparisonReliabilityLabel => Comparison is null
        ? string.Empty
        : Comparison.IsReliable ? "Comparison is reliable for the available metadata." : "Comparison is limited; review the reported limitations before drawing conclusions.";

    [ObservableProperty]
    private ReportsDisplayState _state = ReportsDisplayState.NoProject;

    [ObservableProperty]
    private string _stateMessage = "Open an onboarded project to generate a report.";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private AnalysisReportModel? _report;

    [ObservableProperty]
    private AnalysisComparisonResult? _comparison;

    [ObservableProperty]
    private ReportRunItem? _selectedBaselineRun;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
        => await LoadWorkspaceAsync().ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanBuildReport))]
    private async Task BuildReportAsync()
        => await LoadWorkspaceAsync().ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportJsonAsync()
        => await ExportAsync(AnalysisReportFormat.Json).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportHtmlAsync()
        => await ExportAsync(AnalysisReportFormat.Html).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportCsvAsync()
        => await ExportAsync(AnalysisReportFormat.Csv).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        var snapshot = _analysisViewModel.WorkspaceSnapshot;
        if (snapshot?.ProjectId is not Guid projectId
            || snapshot.ScanId is not Guid currentScanId
            || SelectedBaselineRun is not { } baseline
            || baseline.ScanId == currentScanId)
        {
            return;
        }

        var operation = BeginOperation("Comparing the selected persisted analysis runs...");
        try
        {
            var comparison = await _reportService
                .CompareAsync(projectId, baseline.ScanId, currentScanId, cancellationToken: operation.Source.Token)
                .ConfigureAwait(true);
            if (!CanApply(operation.Source, operation.Version))
            {
                return;
            }

            Comparison = comparison;
            State = ReportsDisplayState.Ready;
            StateMessage = comparison.IsReliable
                ? "The selected analysis runs were compared without detected compatibility limitations."
                : "The comparison completed with limitations; review them before interpreting changes.";
            ErrorMessage = null;
            RaiseComputedProperties();
        }
        catch (OperationCanceledException)
        {
            ApplyCancellation(operation.Source, operation.Version, "Analysis comparison was cancelled.");
        }
        catch (Exception exception)
        {
            ApplyError(operation.Source, operation.Version, exception, "The selected analysis runs could not be compared.");
        }
        finally
        {
            EndOperation(operation.Source, operation.Version);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operationCancellation?.Cancel();

    partial void OnStateChanged(ReportsDisplayState value)
    {
        OnPropertyChanged(nameof(ShowNoProject));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowCancelled));
        RaiseCommandState();
    }

    partial void OnIsBusyChanged(bool value) => RaiseCommandState();

    partial void OnReportChanged(AnalysisReportModel? value)
    {
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(ReportSummaryLabel));
        OnPropertyChanged(nameof(ReportStatusLabel));
        OnPropertyChanged(nameof(SeveritySummaryLabel));
        OnPropertyChanged(nameof(LimitationsLabel));
        OnPropertyChanged(nameof(GitContextLabel));
        RaiseCommandState();
    }

    partial void OnComparisonChanged(AnalysisComparisonResult? value)
    {
        OnPropertyChanged(nameof(HasComparison));
        OnPropertyChanged(nameof(ComparisonSummaryLabel));
        OnPropertyChanged(nameof(ComparisonReliabilityLabel));
    }

    partial void OnSelectedBaselineRunChanged(ReportRunItem? value)
    {
        OnPropertyChanged(nameof(CanCompareRuns));
        CompareCommand.NotifyCanExecuteChanged();
    }

    private void OnAnalysisPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AnalysisViewModel.WorkspaceSnapshot))
        {
            _ = LoadWorkspaceAsync();
        }
        else if (args.PropertyName == nameof(AnalysisViewModel.State)
            && _analysisViewModel.WorkspaceSnapshot is null)
        {
            ApplyMissingWorkspaceState();
        }
    }

    private async Task LoadWorkspaceAsync()
    {
        var snapshot = _analysisViewModel.WorkspaceSnapshot;
        if (snapshot?.ProjectId is not Guid projectId || snapshot.ScanId is not Guid currentScanId)
        {
            ApplyMissingWorkspaceState();
            return;
        }

        var operation = BeginOperation("Loading persisted analysis runs and report data...");
        try
        {
            var runsTask = _reportService.ListRunsAsync(projectId, operation.Source.Token);
            var reportTask = _reportService.BuildAsync(
                projectId,
                currentScanId,
                cancellationToken: operation.Source.Token);
            await Task.WhenAll(runsTask, reportTask).ConfigureAwait(true);
            if (!CanApply(operation.Source, operation.Version))
            {
                return;
            }

            AvailableRuns.Clear();
            foreach (var run in runsTask.Result)
            {
                AvailableRuns.Add(new ReportRunItem(run, currentScanId));
            }

            if (SelectedBaselineRun is not null
                && AvailableRuns.All(run => run.ScanId != SelectedBaselineRun.ScanId))
            {
                SelectedBaselineRun = null;
            }

            Report = reportTask.Result;
            Comparison = null;
            State = ReportsDisplayState.Ready;
            StateMessage = string.Equals(Report.Scan.AnalysisStatus, nameof(AnalysisStatus.Completed), StringComparison.Ordinal)
                ? "The selected persisted analysis run is ready to preview or export."
                : "The report was built from an incomplete analysis run; review its limitations.";
            ErrorMessage = null;
            RaiseComputedProperties();
        }
        catch (OperationCanceledException)
        {
            ApplyCancellation(operation.Source, operation.Version, "Report loading was cancelled.");
        }
        catch (Exception exception)
        {
            ApplyError(operation.Source, operation.Version, exception, "The persisted report could not be built.");
        }
        finally
        {
            EndOperation(operation.Source, operation.Version);
        }
    }

    private async Task ExportAsync(AnalysisReportFormat format)
    {
        var snapshot = _analysisViewModel.WorkspaceSnapshot;
        if (snapshot?.ProjectId is not Guid projectId || snapshot.ScanId is not Guid scanId)
        {
            return;
        }

        var report = Report;
        if (report is null || report.Scan.ScanId != scanId)
        {
            await LoadWorkspaceAsync().ConfigureAwait(true);
            report = Report;
        }

        if (report is null)
        {
            return;
        }

        var suggestedFileName = AnalysisReportFileNames.Create(report.Project.Name, scanId, format);
        var outputPath = _pathPicker.PickSavePath(suggestedFileName, format);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            StateMessage = "Report export cancelled; no file was written.";
            return;
        }

        var operation = BeginOperation($"Exporting {format} report...");
        try
        {
            var result = await _reportService
                .ExportAsync(
                    projectId,
                    scanId,
                    format,
                    outputPath,
                    cancellationToken: operation.Source.Token)
                .ConfigureAwait(true);
            if (!CanApply(operation.Source, operation.Version))
            {
                return;
            }

            Report = result.Report;
            State = ReportsDisplayState.Ready;
            StateMessage = $"Exported {format} report to {result.OutputPath}.";
            ErrorMessage = null;
        }
        catch (OperationCanceledException)
        {
            ApplyCancellation(operation.Source, operation.Version, "Report export was cancelled; no completed export was reported.");
        }
        catch (Exception exception)
        {
            ApplyError(operation.Source, operation.Version, exception, "The report could not be exported.");
        }
        finally
        {
            EndOperation(operation.Source, operation.Version);
        }
    }

    private LoadOperation BeginOperation(string message)
    {
        var version = Interlocked.Increment(ref _operationVersion);
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        var source = new CancellationTokenSource();
        _operationCancellation = source;
        IsBusy = true;
        State = ReportsDisplayState.Loading;
        StateMessage = message;
        ErrorMessage = null;
        return new LoadOperation(source, version);
    }

    private void EndOperation(CancellationTokenSource source, long version)
    {
        if (CanApply(source, version))
        {
            _operationCancellation = null;
            IsBusy = false;
            RaiseCommandState();
        }

        source.Dispose();
    }

    private void ApplyCancellation(CancellationTokenSource source, long version, string message)
    {
        if (!IsCurrent(source, version))
        {
            return;
        }

        State = ReportsDisplayState.Cancelled;
        StateMessage = message;
        ErrorMessage = null;
    }

    private void ApplyError(CancellationTokenSource source, long version, Exception exception, string message)
    {
        if (!IsCurrent(source, version))
        {
            return;
        }

        if (exception is OperationCanceledException || source.IsCancellationRequested)
        {
            ApplyCancellation(source, version, "Report operation was cancelled.");
            return;
        }

        _logger.LogError(exception, "Unable to complete a ForgeDesk report operation for project {ProjectId}.", _analysisViewModel.WorkspaceSnapshot?.ProjectId);
        State = ReportsDisplayState.Error;
        StateMessage = message;
        ErrorMessage = exception is AnalysisReportExportException
            ? exception.Message
            : "Refresh the selected project and try again. Details were written to the application log.";
    }

    private void ApplyMissingWorkspaceState()
    {
        var source = _operationCancellation;
        _operationCancellation = null;
        Interlocked.Increment(ref _operationVersion);
        source?.Cancel();
        source?.Dispose();
        AvailableRuns.Clear();
        Report = null;
        Comparison = null;
        SelectedBaselineRun = null;
        IsBusy = false;
        ErrorMessage = _analysisViewModel.ErrorMessage;
        State = _analysisViewModel.State switch
        {
            AnalysisDisplayState.Loading => ReportsDisplayState.Loading,
            AnalysisDisplayState.Cancelled => ReportsDisplayState.Cancelled,
            AnalysisDisplayState.Error => ReportsDisplayState.Error,
            _ => ReportsDisplayState.NoProject
        };
        StateMessage = _analysisViewModel.State switch
        {
            AnalysisDisplayState.Loading => "Waiting for the selected project analysis workspace.",
            AnalysisDisplayState.Cancelled => "The project analysis refresh was cancelled; no report was built.",
            AnalysisDisplayState.Error => "The selected project analysis could not be loaded, so reports are unavailable.",
            _ => "Open an onboarded project with a persisted analysis run to generate reports."
        };
        RaiseComputedProperties();
    }

    private bool CanRefresh() => !_disposed && !IsBusy && _analysisViewModel.WorkspaceSnapshot?.ProjectId is not null;

    private bool CanBuildReport() => CanRefresh() && CurrentScanId is not null;

    private bool CanExport() => !_disposed && !IsBusy && Report is not null && CurrentScanId is not null;

    private bool CanCompare() => !_disposed && CanCompareRuns;

    private bool CanCancel() => !_disposed && IsBusy;

    private bool IsCurrent(CancellationTokenSource source, long version)
        => ReferenceEquals(_operationCancellation, source)
            && version == Volatile.Read(ref _operationVersion);

    private bool CanApply(CancellationTokenSource source, long version)
        => IsCurrent(source, version) && !source.IsCancellationRequested;

    private void RaiseCommandState()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        BuildReportCommand.NotifyCanExecuteChanged();
        ExportJsonCommand.NotifyCanExecuteChanged();
        ExportHtmlCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
        CompareCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCompareRuns));
        OnPropertyChanged(nameof(CurrentScanLabel));
    }

    private void RaiseComputedProperties()
    {
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(HasComparison));
        OnPropertyChanged(nameof(ReportSummaryLabel));
        OnPropertyChanged(nameof(ReportStatusLabel));
        OnPropertyChanged(nameof(SeveritySummaryLabel));
        OnPropertyChanged(nameof(LimitationsLabel));
        OnPropertyChanged(nameof(GitContextLabel));
        OnPropertyChanged(nameof(ComparisonSummaryLabel));
        OnPropertyChanged(nameof(ComparisonReliabilityLabel));
        RaiseCommandState();
    }

    private static string FormatCount(int? value)
        => value?.ToString("N0") ?? "not available";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _analysisViewModel.PropertyChanged -= OnAnalysisPropertyChanged;
        var source = _operationCancellation;
        _operationCancellation = null;
        Interlocked.Increment(ref _operationVersion);
        source?.Cancel();
        source?.Dispose();
    }

    private readonly record struct LoadOperation(CancellationTokenSource Source, long Version);
}
