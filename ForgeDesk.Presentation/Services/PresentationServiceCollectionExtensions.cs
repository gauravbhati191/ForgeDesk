using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.Presentation.Services;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddForgeDeskPresentation(this IServiceCollection services)
    {
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<ProjectsViewModel>();
        services.AddSingleton<AnalysisViewModel>();
        services.AddSingleton<SourceViewerViewModel>();
        services.AddSingleton<SymbolDetailsViewModel>();
        services.AddSingleton<DependencyGraphViewModel>();
        services.AddSingleton<IssueExplorerViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<UnavailablePageViewModelFactory>();
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }
}
