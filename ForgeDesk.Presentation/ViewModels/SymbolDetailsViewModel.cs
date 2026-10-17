using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Presentation.Models;

namespace ForgeDesk.Presentation.ViewModels;

/// <summary>Details and actions for the currently selected persisted symbol.</summary>
public sealed partial class SymbolDetailsViewModel : ObservableObject
{
    private readonly SourceViewerViewModel _sourceViewer;
    private string? _projectRootPath;

    public SymbolDetailsViewModel(SourceViewerViewModel sourceViewer)
    {
        _sourceViewer = sourceViewer ?? throw new ArgumentNullException(nameof(sourceViewer));
    }

    public event EventHandler? IncomingRequested;

    public event EventHandler? OutgoingRequested;

    public event EventHandler? RelatedFindingsRequested;

    public bool HasSelection => SelectedSymbol is not null;

    public string SourcePathLabel => SelectedSymbol?.LocationLabel ?? "No source location selected";

    [ObservableProperty]
    private AnalysisSymbolItem? _selectedSymbol;

    [ObservableProperty]
    private int _incomingCount;

    [ObservableProperty]
    private int _outgoingCount;

    [ObservableProperty]
    private int _relatedFindingCount;

    [ObservableProperty]
    private string _stateMessage = "Select a graph node to inspect its persisted metadata.";

    public string SymbolName => SelectedSymbol?.Name ?? "No symbol selected";

    public string SymbolKindLabel => SelectedSymbol?.KindLabel ?? "—";

    public string FullyQualifiedName => SelectedSymbol?.FullyQualifiedName ?? "—";

    public string NamespaceLabel
        => SelectedSymbol is null
            ? "Not available in persisted data"
            : SelectedSymbol.NamespaceName is { } namespaceName
                ? SelectedSymbol.ContainingTypeName is { } containingType
                    ? $"{namespaceName} · {containingType}"
                    : namespaceName
                : SelectedSymbol.ContainingTypeName ?? "Not available in persisted data";

    [RelayCommand(CanExecute = nameof(CanOpenSource))]
    private async Task OpenSourceAsync()
    {
        if (SelectedSymbol is null || string.IsNullOrWhiteSpace(_projectRootPath))
        {
            return;
        }

        await _sourceViewer
            .OpenAsync(_projectRootPath, SelectedSymbol.RelativePath, SelectedSymbol.StartLine)
            .ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void ShowIncoming() => IncomingRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void ShowOutgoing() => OutgoingRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void ViewRelatedFindings() => RelatedFindingsRequested?.Invoke(this, EventArgs.Empty);

    public void Apply(
        AnalysisSymbolItem? symbol,
        string? projectRootPath,
        int incomingCount,
        int outgoingCount,
        int relatedFindingCount)
    {
        _projectRootPath = projectRootPath;
        SelectedSymbol = symbol;
        IncomingCount = incomingCount;
        OutgoingCount = outgoingCount;
        RelatedFindingCount = relatedFindingCount;
        StateMessage = symbol is null
            ? "Select a graph node to inspect its persisted metadata."
            : "Persisted symbol metadata loaded from the selected analysis scan.";
        RaiseSelectionProperties();
    }

    partial void OnSelectedSymbolChanged(AnalysisSymbolItem? value)
    {
        OnPropertyChanged(nameof(SymbolName));
        OnPropertyChanged(nameof(SymbolKindLabel));
        OnPropertyChanged(nameof(FullyQualifiedName));
        OnPropertyChanged(nameof(NamespaceLabel));
        OnPropertyChanged(nameof(SourcePathLabel));
    }

    private bool CanOpenSource()
        => SelectedSymbol is not null
            && !string.IsNullOrWhiteSpace(_projectRootPath)
            && !string.IsNullOrWhiteSpace(SelectedSymbol.RelativePath);

    private bool CanUseSelection() => SelectedSymbol is not null;

    private void RaiseSelectionProperties()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SourcePathLabel));
        OpenSourceCommand.NotifyCanExecuteChanged();
        ShowIncomingCommand.NotifyCanExecuteChanged();
        ShowOutgoingCommand.NotifyCanExecuteChanged();
        ViewRelatedFindingsCommand.NotifyCanExecuteChanged();
    }

}
