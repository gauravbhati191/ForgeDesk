using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Presentation.Models;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Presentation.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly UnavailablePageViewModelFactory _unavailablePageFactory;

    public MainWindowViewModel(
        HomeViewModel home,
        ProjectsViewModel projects,
        AnalysisViewModel analysis,
        SettingsViewModel settings,
        UnavailablePageViewModelFactory unavailablePageFactory,
        ILogger<MainWindowViewModel> logger)
    {
        Home = home;
        Projects = projects;
        Analysis = analysis;
        Settings = settings;
        _unavailablePageFactory = unavailablePageFactory;
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

        CurrentPage = NavigationPage.Home;
        CurrentPageViewModel = Home;
    }

    public HomeViewModel Home { get; }

    public ProjectsViewModel Projects { get; }

    public AnalysisViewModel Analysis { get; }

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
        NavigationPage.Dependencies => "Review persisted dependency relationships for the selected project.",
        NavigationPage.CodeHealth => "Review persisted code-health findings for the selected project.",
        NavigationPage.Settings => "Adjust the shell to fit your workspace.",
        _ => "Phase 2 capability surface."
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
            NavigationPage.Dependencies => Analysis,
            NavigationPage.CodeHealth => Analysis,
            NavigationPage.Settings => Settings,
            NavigationPage.GitChanges => _unavailablePageFactory.Create(
                "Git Changes",
                "Version-control changes"),
            NavigationPage.Reports => _unavailablePageFactory.Create(
                "Reports",
                "Analysis reports"),
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

    private static string GetProjectName(string path)
    {
        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(name) ? trimmedPath : name;
    }
}
