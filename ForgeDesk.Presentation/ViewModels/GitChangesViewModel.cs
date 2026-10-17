using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Application;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Presentation.ViewModels;

public enum GitChangesDisplayState
{
    NoProject,
    Loading,
    NotRepository,
    Clean,
    Dirty,
    Unreadable,
    Empty,
    Error,
    Cancelled
}

/// <summary>
/// Presents the read-only Git status and bounded history for the analysis workspace.
/// </summary>
public sealed partial class GitChangesViewModel : ObservableObject, IDisposable
{
    private static readonly GitInspectionOptions InspectionOptions = new()
    {
        MaxChangedFiles = 2_000,
        MaxRecentCommits = 25,
        MaxChangedFilesPerCommit = 200,
        IncludeHistory = true
    };

    private readonly AnalysisViewModel _analysisViewModel;
    private readonly IGitRepositoryPresentationService _gitService;
    private readonly ILogger<GitChangesViewModel> _logger;
    private CancellationTokenSource? _loadCancellation;
    private long _loadVersion;
    private bool _disposed;

    public GitChangesViewModel(
        AnalysisViewModel analysisViewModel,
        IGitRepositoryPresentationService gitService,
        ILogger<GitChangesViewModel> logger)
    {
        _analysisViewModel = analysisViewModel ?? throw new ArgumentNullException(nameof(analysisViewModel));
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _analysisViewModel.PropertyChanged += OnAnalysisPropertyChanged;
        HandleWorkspaceSnapshotChanged(_analysisViewModel.WorkspaceSnapshot);
    }

    public AnalysisViewModel Analysis => _analysisViewModel;

    public ObservableCollection<GitChangeGroupItem> ChangeGroups { get; } = [];

    public ObservableCollection<GitCommitItem> RecentCommits { get; } = [];

    public ObservableCollection<GitCommitFileItem> SelectedCommitFiles { get; } = [];

    public bool ShowNoProject => State == GitChangesDisplayState.NoProject;

    public bool ShowLoading => State == GitChangesDisplayState.Loading;

    public bool ShowNotRepository => State == GitChangesDisplayState.NotRepository;

    public bool ShowClean => State == GitChangesDisplayState.Clean;

    public bool ShowDirty => State == GitChangesDisplayState.Dirty;

    public bool ShowUnreadable => State == GitChangesDisplayState.Unreadable;

    public bool ShowEmpty => State == GitChangesDisplayState.Empty;

    public bool ShowError => State == GitChangesDisplayState.Error;

    public bool ShowCancelled => State == GitChangesDisplayState.Cancelled;

    public bool HasRepositoryMetadata => RepositoryInfo is not null;

    public bool HasRepository => State is GitChangesDisplayState.Clean
        or GitChangesDisplayState.Dirty
        or GitChangesDisplayState.Empty;

    public bool HasChanges => TotalChangeCount > 0;

    public bool HasHistory => RecentCommits.Count > 0;

    public bool HasSelectedCommit => SelectedCommit is not null;

    public bool ShowChangeGroups => HasRepository && HasChanges;

    public bool ShowCleanWorkspace => State == GitChangesDisplayState.Clean && !HasChanges;

    public bool ShowHistoryEmpty => HasRepository && !HasHistory;

    public bool ShowSelectedCommitFiles => HasSelectedCommit;

    public bool ShowHistoryTruncated => HistoryTruncated;

    public bool ShowChangesTruncated => ChangesTruncated;

    public string ProjectRootLabel => _analysisViewModel.WorkspaceSnapshot?.RootPath
        ?? "No project root selected";

    public string StatusSummary => State switch
    {
        GitChangesDisplayState.NoProject => "No project selected",
        GitChangesDisplayState.Loading => "Loading local Git status and history",
        GitChangesDisplayState.NotRepository => "Not a Git repository",
        GitChangesDisplayState.Clean => "Working tree clean",
        GitChangesDisplayState.Dirty => $"{TotalChangeCount:N0} working-tree {(TotalChangeCount == 1 ? "path" : "paths")} changed",
        GitChangesDisplayState.Unreadable => "Repository unreadable",
        GitChangesDisplayState.Empty => "Repository has no commits or changes",
        GitChangesDisplayState.Cancelled => "Git refresh cancelled",
        _ => "Git status unavailable"
    };

    public string ChangeCountLabel => TotalChangeCount == 0
        ? "No working-tree changes"
        : $"{TotalChangeCount:N0} {(TotalChangeCount == 1 ? "path" : "paths")} reported by Git";

    public string HistoryCountLabel => RecentCommits.Count == 0
        ? "No recent commits"
        : $"{RecentCommits.Count:N0} recent {(RecentCommits.Count == 1 ? "commit" : "commits")} shown";

    public string SelectedCommitLabel => SelectedCommit is null
        ? "No commit selected"
        : $"Selected commit {SelectedCommit.IdLabel}";

    public string SelectedCommitFilesMessage => SelectedCommit is null
        ? "Select a recent commit to inspect its bounded changed-file list."
        : !SelectedCommit.ChangedFilesAvailable
            ? "Changed files are unavailable for this commit. Git did not return a safe file list."
            : SelectedCommit.ChangedFiles.Count == 0
                ? "This commit has no changed files in the bounded history result."
                : SelectedCommit.ChangedFilesTruncated
                    ? "Only the bounded changed-file list is shown for this commit."
                    : "Changed files for the selected commit.";

    [ObservableProperty]
    private GitChangesDisplayState _state = GitChangesDisplayState.NoProject;

    [ObservableProperty]
    private string _stateMessage = "Open an onboarded project to inspect its local Git repository.";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private GitRepositoryInfoItem? _repositoryInfo;

    [ObservableProperty]
    private GitCommitItem? _selectedCommit;

    [ObservableProperty]
    private int _totalChangeCount;

    [ObservableProperty]
    private bool _changesTruncated;

    [ObservableProperty]
    private bool _historyTruncated;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        var rootPath = _analysisViewModel.WorkspaceSnapshot?.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            SetStateForMissingWorkspace();
            return;
        }

        await LoadForRootAsync(rootPath).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        var source = _loadCancellation;
        if (source is null)
        {
            return;
        }

        source.Cancel();
        if (IsCurrent(source, Volatile.Read(ref _loadVersion)))
        {
            IsBusy = false;
            State = GitChangesDisplayState.Cancelled;
            StateMessage = "Git refresh was cancelled. No repository data was changed.";
            ErrorMessage = null;
        }
    }

    partial void OnStateChanged(GitChangesDisplayState value)
    {
        OnPropertyChanged(nameof(ShowNoProject));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowNotRepository));
        OnPropertyChanged(nameof(ShowClean));
        OnPropertyChanged(nameof(ShowDirty));
        OnPropertyChanged(nameof(ShowUnreadable));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowCancelled));
        OnPropertyChanged(nameof(HasRepository));
        OnPropertyChanged(nameof(ShowChangeGroups));
        OnPropertyChanged(nameof(ShowCleanWorkspace));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
        OnPropertyChanged(nameof(StatusSummary));
        RefreshCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnRepositoryInfoChanged(GitRepositoryInfoItem? value)
    {
        OnPropertyChanged(nameof(HasRepositoryMetadata));
    }

    partial void OnSelectedCommitChanged(GitCommitItem? value)
    {
        SelectedCommitFiles.Clear();
        if (value is not null)
        {
            foreach (var file in value.ChangedFiles)
            {
                SelectedCommitFiles.Add(file);
            }
        }

        OnPropertyChanged(nameof(HasSelectedCommit));
        OnPropertyChanged(nameof(ShowSelectedCommitFiles));
        OnPropertyChanged(nameof(SelectedCommitLabel));
        OnPropertyChanged(nameof(SelectedCommitFilesMessage));
    }

    partial void OnTotalChangeCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ShowChangeGroups));
        OnPropertyChanged(nameof(ShowCleanWorkspace));
        OnPropertyChanged(nameof(ChangeCountLabel));
        OnPropertyChanged(nameof(StatusSummary));
    }

    partial void OnChangesTruncatedChanged(bool value)
        => OnPropertyChanged(nameof(ShowChangesTruncated));

    partial void OnHistoryTruncatedChanged(bool value)
        => OnPropertyChanged(nameof(ShowHistoryTruncated));

    private void OnAnalysisPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(AnalysisViewModel.WorkspaceSnapshot))
        {
            HandleWorkspaceSnapshotChanged(_analysisViewModel.WorkspaceSnapshot);
            return;
        }

        if (eventArgs.PropertyName == nameof(AnalysisViewModel.State)
            && _analysisViewModel.WorkspaceSnapshot is null)
        {
            SetStateForMissingWorkspace();
        }
    }

    private void HandleWorkspaceSnapshotChanged(ProjectAnalysisSnapshot? snapshot)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.RootPath))
        {
            SetStateForMissingWorkspace();
            return;
        }

        _ = LoadForRootAsync(snapshot.RootPath);
    }

    private async Task LoadForRootAsync(string rootPath)
    {
        var operation = BeginLoad();
        try
        {
            var snapshot = await _gitService
                .InspectAsync(rootPath, InspectionOptions, operation.Source.Token)
                .ConfigureAwait(true);

            if (!CanApply(operation.Source, operation.Version))
            {
                return;
            }

            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            if (!IsCurrent(operation.Source, operation.Version))
            {
                return;
            }

            State = GitChangesDisplayState.Cancelled;
            StateMessage = "Git refresh was cancelled. No repository data was changed.";
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            if (!IsCurrent(operation.Source, operation.Version))
            {
                return;
            }

            if (operation.Source.IsCancellationRequested)
            {
                State = GitChangesDisplayState.Cancelled;
                StateMessage = "Git refresh was cancelled. No repository data was changed.";
                ErrorMessage = null;
                return;
            }

            _logger.LogError(exception, "Unable to load Git status for the selected analysis workspace.");
            ClearRepositoryData();
            State = GitChangesDisplayState.Error;
            StateMessage = "The local Git repository could not be inspected.";
            ErrorMessage = "Refresh the page and try again. If the repository remains unavailable, check that Git is installed and the project folder is accessible.";
        }
        finally
        {
            EndLoad(operation.Source, operation.Version);
        }
    }

    private LoadOperation BeginLoad()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();

        var source = new CancellationTokenSource();
        _loadCancellation = source;
        ClearRepositoryData();
        State = GitChangesDisplayState.Loading;
        StateMessage = "Loading local Git status and bounded recent history...";
        ErrorMessage = null;
        IsBusy = true;
        return new LoadOperation(source, version);
    }

    private void EndLoad(CancellationTokenSource source, long version)
    {
        if (IsCurrent(source, version))
        {
            _loadCancellation = null;
            IsBusy = false;
            RefreshCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }

        source.Dispose();
    }

    private void ApplySnapshot(GitRepositorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var selectedCommitId = SelectedCommit?.Id;
        RepositoryInfo = snapshot.RepositoryRootPath is null && snapshot.State == GitRepositoryState.NotRepository
            ? null
            : new GitRepositoryInfoItem(snapshot);

        var mappedChanges = snapshot.Changes
            .Select(change => new GitChangeItem(change))
            .ToArray();
        var groups = BuildChangeGroups(snapshot.Changes, mappedChanges);
        ChangeGroups.Clear();
        foreach (var group in groups)
        {
            ChangeGroups.Add(group);
        }

        TotalChangeCount = snapshot.Changes.Count;
        ChangesTruncated = snapshot.ChangesTruncated;

        RecentCommits.Clear();
        foreach (var commit in snapshot.RecentCommits)
        {
            RecentCommits.Add(new GitCommitItem(commit));
        }

        HistoryTruncated = snapshot.HistoryTruncated;
        SelectedCommit = selectedCommitId is null
            ? null
            : RecentCommits.FirstOrDefault(commit => string.Equals(commit.Id, selectedCommitId, StringComparison.OrdinalIgnoreCase));

        ErrorMessage = snapshot.ErrorMessage;
        State = DetermineState(snapshot);
        StateMessage = MessageFor(snapshot, State);
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
        OnPropertyChanged(nameof(HistoryCountLabel));
        OnPropertyChanged(nameof(ProjectRootLabel));
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(ShowSelectedCommitFiles));
    }

    private void SetStateForMissingWorkspace()
    {
        var previous = _loadCancellation;
        _loadCancellation = null;
        Interlocked.Increment(ref _loadVersion);
        previous?.Cancel();
        previous?.Dispose();

        ClearRepositoryData();
        IsBusy = false;
        ErrorMessage = _analysisViewModel.ErrorMessage;
        State = _analysisViewModel.State switch
        {
            AnalysisDisplayState.Loading => GitChangesDisplayState.Loading,
            AnalysisDisplayState.Cancelled => GitChangesDisplayState.Cancelled,
            AnalysisDisplayState.Error => GitChangesDisplayState.Error,
            AnalysisDisplayState.NoProject => GitChangesDisplayState.NoProject,
            _ => GitChangesDisplayState.NoProject
        };
        StateMessage = _analysisViewModel.State switch
        {
            AnalysisDisplayState.Loading => "Waiting for the selected project workspace snapshot before reading Git.",
            AnalysisDisplayState.Cancelled => "The project workspace refresh was cancelled; Git was not read.",
            AnalysisDisplayState.Error => "The selected project workspace could not be loaded, so Git status is unavailable.",
            _ => "Open an onboarded project to inspect its local Git repository."
        };
        OnPropertyChanged(nameof(ProjectRootLabel));
    }

    private void ClearRepositoryData()
    {
        RepositoryInfo = null;
        ChangeGroups.Clear();
        RecentCommits.Clear();
        SelectedCommit = null;
        SelectedCommitFiles.Clear();
        TotalChangeCount = 0;
        ChangesTruncated = false;
        HistoryTruncated = false;
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
        OnPropertyChanged(nameof(HistoryCountLabel));
        OnPropertyChanged(nameof(HasSelectedCommit));
        OnPropertyChanged(nameof(ShowSelectedCommitFiles));
        OnPropertyChanged(nameof(SelectedCommitLabel));
        OnPropertyChanged(nameof(SelectedCommitFilesMessage));
    }

    private static GitChangesDisplayState DetermineState(GitRepositorySnapshot snapshot)
    {
        if (snapshot.State == GitRepositoryState.NotRepository)
        {
            return GitChangesDisplayState.NotRepository;
        }

        if (snapshot.State == GitRepositoryState.Unreadable)
        {
            return GitChangesDisplayState.Unreadable;
        }

        if (snapshot.State == GitRepositoryState.Clean
            && snapshot.Changes.Count == 0
            && snapshot.RecentCommits.Count == 0
            && snapshot.HeadCommit is null)
        {
            return GitChangesDisplayState.Empty;
        }

        return snapshot.State == GitRepositoryState.Dirty
            ? GitChangesDisplayState.Dirty
            : GitChangesDisplayState.Clean;
    }

    private static string MessageFor(GitRepositorySnapshot snapshot, GitChangesDisplayState state)
        => state switch
        {
            GitChangesDisplayState.NotRepository => "The selected project is not inside a Git repository. No local status or history is available.",
            GitChangesDisplayState.Unreadable => "Git metadata was found, but the repository could not be read safely.",
            GitChangesDisplayState.Empty => "The repository is valid but has no commits and no working-tree changes yet.",
            GitChangesDisplayState.Dirty => "The local working tree has changes. Review the grouped paths below; ForgeDesk will not modify them.",
            GitChangesDisplayState.Clean => "The local working tree is clean. Recent local history is shown below when available.",
            _ => "Git repository data is unavailable."
        };

    private static IReadOnlyList<GitChangeGroupItem> BuildChangeGroups(
        IReadOnlyList<GitFileChange> changes,
        IReadOnlyList<GitChangeItem> mappedChanges)
    {
        var grouped = new Dictionary<GitChangeGroupKind, List<GitChangeItem>>();

        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            var item = mappedChanges[index];
            var assigned = false;

            if (change.IsConflicted)
            {
                Add(GitChangeGroupKind.Conflicted, item);
                assigned = true;
            }
            else
            {
                if (change.IsStaged)
                {
                    Add(GitChangeGroupKind.Staged, item);
                    assigned = true;
                }

                if (change.IsUnstaged && !change.IsUntracked)
                {
                    Add(GitChangeGroupKind.Unstaged, item);
                    assigned = true;
                }

                if (change.IsUntracked)
                {
                    Add(GitChangeGroupKind.Untracked, item);
                    assigned = true;
                }
            }

            if (!assigned)
            {
                Add(GitChangeGroupKind.Other, item);
            }

            void Add(GitChangeGroupKind kind, GitChangeItem value)
            {
                if (!grouped.TryGetValue(kind, out var values))
                {
                    values = [];
                    grouped.Add(kind, values);
                }

                values.Add(value);
            }
        }

        var order = new[]
        {
            GitChangeGroupKind.Conflicted,
            GitChangeGroupKind.Staged,
            GitChangeGroupKind.Unstaged,
            GitChangeGroupKind.Untracked,
            GitChangeGroupKind.Other
        };

        return order
            .Where(grouped.ContainsKey)
            .Select(kind => new GitChangeGroupItem(
                kind,
                grouped[kind]
                    .OrderBy(change => change.RelativePath, StringComparer.OrdinalIgnoreCase)))
            .ToArray();
    }

    private bool CanRefresh()
        => !_disposed
            && !IsBusy
            && !string.IsNullOrWhiteSpace(_analysisViewModel.WorkspaceSnapshot?.RootPath);

    private bool CanCancel() => !_disposed && IsBusy;

    private bool IsCurrent(CancellationTokenSource source, long version)
        => ReferenceEquals(_loadCancellation, source)
            && version == Volatile.Read(ref _loadVersion);

    private bool CanApply(CancellationTokenSource source, long version)
        => IsCurrent(source, version) && !source.IsCancellationRequested;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _analysisViewModel.PropertyChanged -= OnAnalysisPropertyChanged;
        var source = _loadCancellation;
        _loadCancellation = null;
        Interlocked.Increment(ref _loadVersion);
        source?.Cancel();
        source?.Dispose();
    }

    private readonly record struct LoadOperation(CancellationTokenSource Source, long Version);
}
