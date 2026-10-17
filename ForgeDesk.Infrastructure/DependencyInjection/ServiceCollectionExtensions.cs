using ForgeDesk.Application;
using ForgeDesk.Analysis;
using ForgeDesk.Graph;
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
        services.AddSingleton<IDatabaseBackupService, SqliteDatabaseBackupService>();
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
        services.AddScoped<IAnalysisReportDataSource, AnalysisReportDataSource>();
        services.AddScoped<ForgeDesk.Application.IAnalysisEngine, RoslynAnalysisEngine>();
        services.AddScoped<IAnalysisService, AnalysisService>();
        services.AddScoped<IAnalysisReportService, AnalysisReportService>();
        services.AddScoped<IAnalysisComparisonService, AnalysisComparisonService>();
        services.AddScoped<IGraphQueryRepository, GraphQueryRepository>();
        services.AddScoped<GraphDataBuilder>();
        services.AddScoped<IGraphLayoutService, DeterministicGraphLayoutService>();
        services.AddScoped<IGraphDataProvider, GraphDataProvider>();
        services.AddScoped<GraphQueryService>();
        services.AddScoped<IGraphQueryService>(provider => provider.GetRequiredService<GraphQueryService>());
        services.AddScoped<IGraphService>(provider => provider.GetRequiredService<GraphQueryService>());
        services.AddScoped<IGitRepositoryReader, GitProcessRepository>();
        services.AddScoped<IGitRepositoryService, GitRepositoryService>();
        services.AddScoped<DatabaseInitializer>();
        return services;
    }
}
