using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Presentation.ViewModels;

public enum SourceViewerDisplayState
{
    Empty,
    Loading,
    Ready,
    Error,
    Cancelled
}

/// <summary>Loads and presents a safe, read-only source preview for a persisted location.</summary>
public sealed partial class SourceViewerViewModel : ObservableObject
{
    private readonly ISourceNavigationService _navigationService;
    private readonly ILogger<SourceViewerViewModel> _logger;
    private CancellationTokenSource? _operationCancellation;
    private long _operationVersion;
    private string? _projectRootPath;
    private string? _relativePath;
    private int? _requestedLine;

    public SourceViewerViewModel(
        ISourceNavigationService navigationService,
        ILogger<SourceViewerViewModel> logger)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<SourcePreviewLine> Lines { get; } = [];

    public bool HasPreview => State == SourceViewerDisplayState.Ready;

    public bool ShowEmpty => State == SourceViewerDisplayState.Empty;

    public bool ShowLoading => State == SourceViewerDisplayState.Loading;

    public bool ShowError => State == SourceViewerDisplayState.Error;

    public bool ShowCancelled => State == SourceViewerDisplayState.Cancelled;

    [ObservableProperty]
    private SourceViewerDisplayState _state = SourceViewerDisplayState.Empty;

    [ObservableProperty]
    private string _stateMessage = "Select a symbol or finding to preview its source.";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string? _fullPath;

    [ObservableProperty]
    private int _totalLineCount;

    [ObservableProperty]
    private bool _isTruncated;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public string LineCountLabel => TotalLineCount == 0
        ? string.Empty
        : IsTruncated
            ? $"Showing a bounded preview of {TotalLineCount:N0} lines"
            : $"{TotalLineCount:N0} lines";

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operationCancellation?.Cancel();

    [RelayCommand]
    private void Clear()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _projectRootPath = null;
        _relativePath = null;
        _requestedLine = null;
        Lines.Clear();
        FilePath = null;
        FullPath = null;
        TotalLineCount = 0;
        IsTruncated = false;
        ErrorMessage = null;
        State = SourceViewerDisplayState.Empty;
        StateMessage = "Select a symbol or finding to preview its source.";
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(LineCountLabel));
    }

    public async Task OpenAsync(
        string? projectRootPath,
        string? relativePath,
        int? selectedLine = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRootPath) || string.IsNullOrWhiteSpace(relativePath))
        {
            Clear();
            State = SourceViewerDisplayState.Error;
            StateMessage = "The selected result has no navigable source location.";
            ErrorMessage = "A project root and relative source path are required.";
            RaiseStateProperties();
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = source;
        var version = Interlocked.Increment(ref _operationVersion);
        _projectRootPath = projectRootPath;
        _relativePath = relativePath;
        _requestedLine = selectedLine;
        State = SourceViewerDisplayState.Loading;
        StateMessage = "Validating and loading source preview...";
        ErrorMessage = null;
        Lines.Clear();
        FilePath = relativePath.Replace('\\', '/');
        FullPath = null;
        TotalLineCount = 0;
        IsTruncated = false;
        RaiseStateProperties();

        try
        {
            var result = await _navigationService
                .LoadPreviewAsync(projectRootPath, relativePath, selectedLine, source.Token)
                .ConfigureAwait(true);

            if (!IsCurrent(version, source))
            {
                return;
            }

            if (!result.Succeeded)
            {
                State = SourceViewerDisplayState.Error;
                StateMessage = "The source preview could not be loaded.";
                ErrorMessage = result.ErrorMessage;
                RaiseStateProperties();
                return;
            }

            Lines.Clear();
            foreach (var line in FilterLines(result.Lines, SearchText))
            {
                Lines.Add(line);
            }

            FilePath = result.RelativePath;
            FullPath = result.FullPath;
            TotalLineCount = result.TotalLineCount;
            IsTruncated = result.IsTruncated;
            State = SourceViewerDisplayState.Ready;
            StateMessage = result.SelectedLine is null
                ? "Read-only source preview loaded."
                : $"Source preview loaded at line {result.SelectedLine}.";
            ErrorMessage = null;
            RaiseStateProperties();
        }
        catch (OperationCanceledException)
        {
            if (!IsCurrent(version, source))
            {
                return;
            }

            State = SourceViewerDisplayState.Cancelled;
            StateMessage = "Source preview loading was cancelled.";
            ErrorMessage = null;
            RaiseStateProperties();
        }
        catch (Exception exception)
        {
            if (!IsCurrent(version, source))
            {
                return;
            }

            _logger.LogError(exception, "Unable to load source preview for {RelativePath}.", relativePath);
            State = SourceViewerDisplayState.Error;
            StateMessage = "The source preview could not be loaded.";
            ErrorMessage = "The local source file could not be read. Check that it still exists and is accessible.";
            RaiseStateProperties();
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, source))
            {
                _operationCancellation = null;
                CancelCommand.NotifyCanExecuteChanged();
            }

            source.Dispose();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (State != SourceViewerDisplayState.Ready || _projectRootPath is null || _relativePath is null)
        {
            return;
        }

        _ = ReloadSearchAsync();
    }

    partial void OnStateChanged(SourceViewerDisplayState value) => RaiseStateProperties();

    partial void OnTotalLineCountChanged(int value) => OnPropertyChanged(nameof(LineCountLabel));

    partial void OnIsTruncatedChanged(bool value) => OnPropertyChanged(nameof(LineCountLabel));

    private async Task ReloadSearchAsync()
    {
        if (_projectRootPath is null || _relativePath is null)
        {
            return;
        }

        // Re-load through the same validated service so a search never turns into
        // an unvalidated file read after the project changes.
        await OpenAsync(_projectRootPath, _relativePath, _requestedLine).ConfigureAwait(true);
    }

    private static IEnumerable<SourcePreviewLine> FilterLines(
        IReadOnlyList<SourcePreviewLine> lines,
        string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return lines;
        }

        return lines.Where(line => line.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsCurrent(long version, CancellationTokenSource source)
        => version == Volatile.Read(ref _operationVersion)
            && ReferenceEquals(_operationCancellation, source)
            && !source.IsCancellationRequested;

    private bool CanCancel() => _operationCancellation is not null;

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowCancelled));
        CancelCommand.NotifyCanExecuteChanged();
    }
}
