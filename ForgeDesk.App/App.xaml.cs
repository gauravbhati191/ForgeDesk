using System.IO;
using System.Windows;
using System.Windows.Threading;
using ForgeDesk.App.Composition;
using ForgeDesk.App.Services;
using ForgeDesk.Infrastructure;
using ForgeDesk.Presentation.Contracts;
using ForgeDesk.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using WpfApplication = System.Windows.Application;

namespace ForgeDesk.App;

public partial class App : WpfApplication
{
    private IHost? _host;
    private IThemePreferenceStore? _themePreferenceStore;
    private IThemeResourceManager? _themeResourceManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSimpleConsole(options =>
                    {
                        options.SingleLine = true;
                        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
                    });
                    var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgeDesk", "logs", "forgedesk.log");
                    logging.AddProvider(new FileLoggerProvider(logPath));
                })
                .ConfigureServices(services => services.AddForgeDeskApp(this))
                .Build();

            await _host.StartAsync().ConfigureAwait(true);
            await using (var scope = _host.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync().ConfigureAwait(true);
            }

            _themePreferenceStore = _host.Services.GetRequiredService<IThemePreferenceStore>();
            _themeResourceManager = _host.Services.GetRequiredService<IThemeResourceManager>();
            _themePreferenceStore.PreferenceChanged += OnThemePreferenceChanged;
            _themeResourceManager.Apply(await _themePreferenceStore.LoadAsync().ConfigureAwait(true));

            var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogCritical(exception, "ForgeDesk could not start.");
            MessageBox.Show(
                "ForgeDesk could not start because its local database or application services could not be initialized. Review the application log for details.",
                "ForgeDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_themePreferenceStore is not null)
        {
            _themePreferenceStore.PreferenceChanged -= OnThemePreferenceChanged;
        }
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync().ConfigureAwait(true);
            }
            finally
            {
                _host.Dispose();
            }
        }
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        base.OnExit(e);
    }

    private void OnThemePreferenceChanged(object? sender, ThemePreferenceChangedEventArgs eventArgs)
        => _themeResourceManager?.Apply(eventArgs.Theme);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(eventArgs.Exception, "An unhandled UI exception occurred.");
        MessageBox.Show("ForgeDesk encountered an unexpected UI error. The operation was stopped; your project files were not modified.", "ForgeDesk", MessageBoxButton.OK, MessageBoxImage.Error);
        eventArgs.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(eventArgs.Exception, "An unobserved task exception occurred.");
        eventArgs.SetObserved();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
        => _host?.Services.GetService<ILogger<App>>()?.LogCritical(eventArgs.ExceptionObject as Exception, "An unhandled application exception occurred.");
}
