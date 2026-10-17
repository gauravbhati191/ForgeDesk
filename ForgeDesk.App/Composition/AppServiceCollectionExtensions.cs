using System.Windows;
using ForgeDesk.App.Services;
using ForgeDesk.Infrastructure;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.Services;
using ForgeDesk.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.App.Composition;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddForgeDeskApp(this IServiceCollection services, System.Windows.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        services.AddForgeDeskInfrastructure();
        services.AddForgeDeskPresentation();
        services.AddSingleton<IProjectFolderPicker, WindowsFolderPicker>();
        services.AddSingleton<IProjectWorkspaceService, ApplicationProjectWorkspaceService>();
        services.AddSingleton<IProjectAnalysisService, PersistedProjectAnalysisService>();
        services.AddSingleton<IProjectGraphService, PersistedProjectGraphService>();
        services.AddSingleton<IProjectReportService, PersistedProjectReportService>();
        services.AddSingleton<IReportPathPicker, WindowsReportPathPicker>();
        services.AddSingleton<ISourceNavigationService, SafeSourceNavigationService>();
        services.AddSingleton<IThemePreferenceStore, ApplicationThemePreferenceStore>();
        services.AddSingleton<IThemeResourceManager>(_ => new WpfThemeResourceManager(application));
        services.AddSingleton<MainWindow>();
        return services;
    }
}
