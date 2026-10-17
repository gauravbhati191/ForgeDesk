using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Presentation.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(
        HomeViewModel home,
        ProjectsViewModel projects,
        AnalysisViewModel analysis,
        DependencyGraphViewModel dependencyGraph,
        IssueExplorerViewModel issueExplorer,
        GitChangesViewModel gitChanges,
        ReportsViewModel reports,
        SettingsViewModel settings,
        ILogger<MainWindowViewModel> logger)
    {
        Home = home;
        Projects = projects;
        Analysis = analysis;
        DependencyGraph = dependencyGraph;
        IssueExplorer = issueExplorer;
        GitChanges = gitChanges;
        Reports = reports;
        Settings = settings;
        _logger = logger;

        NavigationItems = new[]
        {
            new NavigationItem(NavigationPage.Home, "Home", "Icon.Home"),
            new NavigationItem(NavigationPage.Projects, "Projects", "Icon.Projects"),
            new NavigationItem(NavigationPage.CodeExplorer, "Code Explorer", "Icon.Code"),
            new NavigationItem(NavigationPage.Dependencies, "Dependencies", "Icon.Dependencies"),
            new NavigationItem(NavigationPage.CodeHealth, "Code Health", "Icon.Health"),
            new NavigationItem(NavigationPage.GitChanges, "Git Changes", "Icon.Git"),
            new NavigationItem(NavigationPage.Reports, "Reports", "Icon.Reports"),
            new NavigationItem(NavigationPage.Settings, "Settings", "Icon.Settings")
        };

        Projects.PropertyChanged += OnProjectsPropertyChanged;
        Projects.ProjectOpened += OnProjectOpened;
        DependencyGraph.RelatedFindingsRequested += OnGraphRelatedFindingsRequested;
        IssueExplorer.NavigateToGraphRequested += OnIssueNavigateToGraphRequested;

        CurrentPage = NavigationPage.Home;
        CurrentPageViewModel = Home;
    }

    public HomeViewModel Home { get; }

    public ProjectsViewModel Projects { get; }

    public AnalysisViewModel Analysis { get; }

    public DependencyGraphViewModel DependencyGraph { get; }

    public IssueExplorerViewModel IssueExplorer { get; }

    public GitChangesViewModel GitChanges { get; }

    public ReportsViewModel Reports { get; }

    public SettingsViewModel Settings { get; }

    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationPage _currentPage;

    [ObservableProperty]
    private object? _currentPageViewModel;

    [ObservableProperty]
    private string? _currentProjectPath;

    [ObservableProperty]
    private Guid? _currentProjectId;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private string? _errorMessage;

    public string PageTitle => NavigationItems.First(item => item.Page == CurrentPage).Label;

    public string PageSubtitle => CurrentPage switch
    {
        NavigationPage.Home => "A clear starting point for your workspace.",
        NavigationPage.Projects => "Choose, reopen, and organize local project folders.",
        NavigationPage.CodeExplorer => "Review persisted symbols and source locations for the selected project.",
        NavigationPage.Dependencies => "Explore persisted dependency relationships for the selected project.",
        NavigationPage.CodeHealth => "Search and navigate persisted code-health findings for the selected project.",
        NavigationPage.GitChanges => "Inspect local Git status and bounded commit history without mutating the repository.",
        NavigationPage.Reports => "Export and compare reports from explicitly selected persisted analysis runs.",
        NavigationPage.Settings => "Adjust the shell to fit your workspace.",
        _ => "Phase 4 developer workflow surface."
    };

    public string ProjectContextLabel => string.IsNullOrWhiteSpace(CurrentProjectPath)
        ? "No project open"
        : $"Project: {GetProjectName(CurrentProjectPath)}";

    public async Task InitializeAsync()
    {
        try
        {
            await Projects.LoadRecentProjectsAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to initialize the ForgeDesk shell.");
            ErrorMessage = "The workspace could not be initialized.";
        }
    }

    [RelayCommand]
    private void Navigate(NavigationPage page)
    {
        CurrentPage = page;
    }

    partial void OnCurrentPageChanged(NavigationPage value)
    {
        CurrentPageViewModel = value switch
        {
            NavigationPage.Home => Home,
            NavigationPage.Projects => Projects,
            NavigationPage.CodeExplorer => Analysis,
            NavigationPage.Dependencies => DependencyGraph,
            NavigationPage.CodeHealth => IssueExplorer,
            NavigationPage.GitChanges => GitChanges,
            NavigationPage.Reports => Reports,
            NavigationPage.Settings => Settings,
            _ => Home
        };

        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    partial void OnCurrentProjectPathChanged(string? value)
    {
        OnPropertyChanged(nameof(ProjectContextLabel));
    }

    private void OnProjectsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        switch (eventArgs.PropertyName)
        {
            case nameof(ProjectsViewModel.IsBusy):
                IsBusy = Projects.IsBusy;
                break;
            case nameof(ProjectsViewModel.StatusMessage):
                StatusMessage = Projects.StatusMessage;
                break;
            case nameof(ProjectsViewModel.ErrorMessage):
                ErrorMessage = Projects.ErrorMessage;
                break;
        }
    }

    private void OnProjectOpened(object? sender, ProjectOpenedEventArgs eventArgs)
    {
        CurrentProjectPath = eventArgs.Project.FolderPath;
        CurrentProjectId = eventArgs.Project.Project?.Id;
        StatusMessage = $"{eventArgs.Project.DisplayName} is ready.";
        ErrorMessage = null;

        if (eventArgs.Project.Project is { } project)
        {
            _ = Analysis.LoadForProjectAsync(project, eventArgs.Project.Scan?.Id);
        }
        else
        {
            Analysis.ClearSelection();
        }
    }

    private void OnGraphRelatedFindingsRequested(object? sender, EventArgs eventArgs)
    {
        if (sender is DependencyGraphViewModel graphViewModel
            && graphViewModel.SelectedNodeId is Guid symbolId)
        {
            IssueExplorer.ShowFindingsForSymbol(symbolId);
        }

        CurrentPage = NavigationPage.CodeHealth;
    }

    private void OnIssueNavigateToGraphRequested(object? sender, EventArgs eventArgs)
        => CurrentPage = NavigationPage.Dependencies;

    private static string GetProjectName(string path)
    {
        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(name) ? trimmedPath : name;
    }
}
