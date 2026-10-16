using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Presentation.ViewModels;

public sealed partial class ProjectsViewModel : ObservableObject
{
    private readonly IProjectFolderPicker _folderPicker;
    private readonly IProjectWorkspaceService _workspaceService;
    private readonly ILogger<ProjectsViewModel> _logger;
    private CancellationTokenSource? _operationCancellation;

    public ProjectsViewModel(IProjectFolderPicker folderPicker, IProjectWorkspaceService workspaceService, ILogger<ProjectsViewModel> logger)
    {
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];
    public bool HasRecentProjects => RecentProjects.Count > 0;
    public event EventHandler<ProjectOpenedEventArgs>? ProjectOpened;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "No project selected.";

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    public async Task LoadRecentProjectsAsync()
    {
        if (IsBusy) return;
        using var operation = BeginOperation("Loading recent projects...");
        try
        {
            var projects = await _workspaceService.LoadRecentProjectsAsync(operation.Token).ConfigureAwait(true);
            ReplaceRecentProjects(projects);
            StatusMessage = projects.Count == 0 ? "No recent projects yet." : $"{projects.Count} recent project{(projects.Count == 1 ? string.Empty : "s")}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Loading recent projects was cancelled.";
        }
        catch (Exception exception)
        {
            LogAndShowError(exception, "Unable to load recent projects.", "Recent projects could not be loaded.");
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (IsBusy) return;
        using var operation = BeginOperation("Choose a project folder...");
        try
        {
            var folderPath = await _folderPicker.PickFolderAsync(operation.Token).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                StatusMessage = "No project folder selected.";
                return;
            }
            await OpenFolderAsync(folderPath, operation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Opening the project was cancelled.";
        }
        catch (Exception exception)
        {
            LogAndShowError(exception, "Unable to choose a project folder.", "The project folder could not be opened.");
        }
    }

    [RelayCommand]
    private async Task OpenRecentProjectAsync(RecentProjectItem? project)
    {
        if (project is null || IsBusy) return;
        using var operation = BeginOperation($"Opening {project.DisplayName}...");
        try
        {
            await OpenFolderAsync(project.Path, operation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Opening the project was cancelled.";
        }
        catch (Exception exception)
        {
            LogAndShowError(exception, "Unable to open recent project.", "The recent project could not be opened.");
        }
    }

    [RelayCommand]
    private async Task RemoveRecentProjectAsync(RecentProjectItem? project)
    {
        if (project is null || IsBusy) return;
        using var operation = BeginOperation($"Removing {project.DisplayName} from recent projects...");
        try
        {
            await _workspaceService.RemoveRecentProjectAsync(project.Path, operation.Token).ConfigureAwait(true);
            RecentProjects.Remove(project);
            OnPropertyChanged(nameof(HasRecentProjects));
            StatusMessage = HasRecentProjects ? "Recent project removed." : "No recent projects yet.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Removing the recent project was cancelled.";
        }
        catch (Exception exception)
        {
            LogAndShowError(exception, "Unable to remove recent project.", "The recent project could not be removed.");
        }
    }

    [RelayCommand]
    private void CancelOperation() => _operationCancellation?.Cancel();

    private async Task OpenFolderAsync(string folderPath, CancellationToken cancellationToken)
    {
        var progress = new Progress<ForgeDesk.Application.FileDiscoveryProgress>(update =>
        {
            StatusMessage = update.FilesDiscovered == 0
                ? "Discovering project files..."
                : $"Discovering project files... {update.FilesDiscovered} found";
        });
        var result = await _workspaceService.OpenProjectAsync(folderPath, progress, cancellationToken).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            ErrorMessage = result.Message ?? "The selected folder is not a supported C# project.";
            StatusMessage = "The folder was not opened.";
            return;
        }

        var projects = await _workspaceService.LoadRecentProjectsAsync(cancellationToken).ConfigureAwait(true);
        ReplaceRecentProjects(projects);
        ErrorMessage = null;
        StatusMessage = result.EffectiveWarnings.Count == 0
            ? $"{result.DisplayName} is ready. {result.SourceFileCount} C# source file(s) discovered."
            : $"{result.DisplayName} is ready with {result.EffectiveWarnings.Count} warning(s).";
        ProjectOpened?.Invoke(this, new ProjectOpenedEventArgs(result));
    }

    private OperationScope BeginOperation(string message)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = message;
        return new OperationScope(this, _operationCancellation);
    }

    private void EndOperation(CancellationTokenSource source)
    {
        if (ReferenceEquals(_operationCancellation, source))
        {
            _operationCancellation = null;
        }
        source.Dispose();
        IsBusy = false;
    }

    private void ReplaceRecentProjects(IEnumerable<RecentProjectItem> projects)
    {
        RecentProjects.Clear();
        foreach (var project in projects) RecentProjects.Add(project);
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    private void LogAndShowError(Exception exception, string logMessage, string userMessage)
    {
        _logger.LogError(exception, logMessage);
        ErrorMessage = exception is InvalidOperationException ? exception.Message : userMessage;
        StatusMessage = "The operation did not complete.";
    }

    private sealed class OperationScope : IDisposable
    {
        private readonly ProjectsViewModel _owner;
        private readonly CancellationTokenSource _source;
        public OperationScope(ProjectsViewModel owner, CancellationTokenSource source) { _owner = owner; _source = source; }
        public CancellationToken Token => _source.Token;
        public void Dispose() => _owner.EndOperation(_source);
    }
}
