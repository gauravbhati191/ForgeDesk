using ForgeDesk.Application;
using ForgeDesk.Analysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.Infrastructure;

/// <summary>Registers persistence and filesystem services for the desktop composition root.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddForgeDeskInfrastructure(
        this IServiceCollection services,
        Action<ForgeDeskInfrastructureOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new ForgeDeskInfrastructureOptions();
        configure?.Invoke(options);
        var pathProvider = new DatabasePathProvider(options.DatabasePath);

        services.AddSingleton<IDatabasePathProvider>(pathProvider);
        services.AddDbContext<ForgeDeskDbContext>(builder =>
        {
            var connectionString = options.ConnectionString ?? pathProvider.ConnectionString;
            builder.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(ForgeDeskDbContext).Assembly.GetName().Name!));
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IScanRepository, ScanRepository>();
        services.AddScoped<ISettingRepository, SettingRepository>();
        services.AddScoped<IFileDiscoveryService, FileDiscoveryService>();
        services.AddScoped<IProjectValidationService, ProjectValidationService>();
        services.AddScoped<IProjectOnboardingStore, ProjectOnboardingStore>();
        services.AddScoped<IProjectOnboardingService, ProjectOnboardingService>();
        services.AddScoped<IScanLifecycleService, ScanLifecycleService>();
        services.AddScoped<IThemeSettingsService, ThemeSettingsService>();
        services.AddScoped<IAnalysisRepository, AnalysisRepository>();
        services.AddScoped<ForgeDesk.Application.IAnalysisEngine, RoslynAnalysisEngine>();
        services.AddScoped<IAnalysisService, AnalysisService>();
        services.AddScoped<DatabaseInitializer>();
        return services;
    }
}
