# ForgeDesk architecture

## Dependency direction

```text
ForgeDesk.App (WPF composition root)
        |
        +--> ForgeDesk.Presentation (MVVM views/viewmodels/contracts)
        |          |
        |          +--> ForgeDesk.Application (use cases/contracts)
        |
        +--> ForgeDesk.Infrastructure (EF Core/filesystem implementations)
                   |
                   +--> ForgeDesk.Application
                   +--> ForgeDesk.Domain

ForgeDesk.Application --> ForgeDesk.Domain
ForgeDesk.Analysis --> ForgeDesk.Domain       (contracts only)
ForgeDesk.Graph --> ForgeDesk.Domain          (contracts only)
ForgeDesk.Domain --> no UI, EF Core, or infrastructure
```

The application layer does not know about WPF. Infrastructure does not decide navigation or presentation state. The composition root is the only place where concrete WPF, application, and infrastructure services are assembled.

## Project responsibilities

### ForgeDesk.App

`App.xaml.cs` builds a `Host`, adds logging and services through `AddForgeDeskApp`, initializes `DatabaseInitializer`, loads the saved theme, resolves `MainWindow`, and starts the viewmodel. It also owns `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, and `AppDomain.UnhandledException` boundaries. `MainWindow.xaml` is a visual shell only: the sidebar selects a `NavigationPage`, the content control renders a viewmodel data template, and no business workflow lives in code-behind.

The application project contains `ApplicationProjectWorkspaceService`, which creates a short-lived DI scope per database operation. This is important because `DbContext` and application repositories are scoped while the long-lived WPF viewmodels are singletons.

### ForgeDesk.Presentation

The presentation layer uses `ObservableObject`, `[ObservableProperty]`, and `[RelayCommand]` from CommunityToolkit.Mvvm. `MainWindowViewModel` owns navigation state and project context. `ProjectsViewModel` guards against concurrent operations, exposes busy/error/status state, reports file discovery progress, and uses a cancellation source. `SettingsViewModel` uses an injected theme preference contract. Views bind to commands and state; code-behind is limited to `InitializeComponent`.

`Resources/Themes/Dark.xaml` and `Light.xaml` provide actual control brushes. `WpfThemeResourceManager` replaces the active dictionary in `Application.Resources.MergedDictionaries`, so controls and views consume the selected theme through `DynamicResource`.

### ForgeDesk.Application

`ProjectValidationService` uses the discovery boundary to classify a folder as a solution, C# project, or source folder. `ProjectOnboardingService` then:

1. validates the request;
2. finds an existing project by normalized root path;
3. reuses or creates the project and updates `LastOpenedAtUtc`;
4. creates a pending scan and saves it;
5. transitions the scan to running;
6. discovers eligible metadata with progress and cancellation;
7. creates `SourceFile` metadata records without reading contents into the database;
8. records discovered/processed counts;
9. completes the scan and updates `LastScannedAtUtc` in a transaction;
10. persists failed/cancelled scan state when the operation does not complete.

`IScanLifecycleService` exposes the same guarded state transitions for future scan commands. `IThemeSettingsService` maps the `Settings` key/value record to a typed `ThemeMode`.

### ForgeDesk.Domain

Domain entities use UTC timestamps, explicit factory methods, private setters, and invariant checks. `Scan` only allows Pending → Running → Completed/Failed/Cancelled, and counts cannot be negative or processed above discovered. `PathNormalization` provides lexical identity and relative-path traversal checks. No entity calls EF Core or WPF.

### ForgeDesk.Infrastructure

`ForgeDeskDbContext` is the single EF Core context. `Persistence/Configurations` centralizes table names, enum conversions, lengths, foreign keys, cascades, nullable future references, and indexes. `ProjectOnboardingStore` uses explicit transactions for the begin and completion phases. `DatabaseInitializer` calls `MigrateAsync`, not `EnsureCreated`.

`FileDiscoveryService` enumerates one directory at a time, skips configured generated folders, catches inaccessible directory/file metadata errors, checks reparse-point targets against the selected physical root, de-duplicates logical and physical paths, and checks the cancellation token during traversal. The service reports progress but does not persist contents.

## Startup flow

```text
App.OnStartup
  -> Host.CreateDefaultBuilder
  -> AddForgeDeskApp
       -> AddForgeDeskInfrastructure
       -> AddForgeDeskPresentation
       -> register Windows picker, DB workspace adapter, theme resource manager
  -> Host.StartAsync
  -> DatabaseInitializer.InitializeAsync
       -> ensure LocalApplicationData/ForgeDesk exists
       -> apply checked-in EF migrations
  -> load Settings[ui.theme]
  -> apply Dark.xaml or Light.xaml
  -> resolve MainWindow + MainWindowViewModel
  -> show window
  -> load persisted recent projects asynchronously
```

Startup errors are logged and shown as a clear message before the process exits. The database is never created in the source repository by default.

## MVVM and concurrency

Long-running operations are `Task`-returning commands. `ProjectsViewModel.IsBusy` prevents conflicting operations, while `OperationScope` owns a `CancellationTokenSource`. `FileDiscoveryProgress` updates the status text without blocking the WPF dispatcher. Database scopes are short-lived and disposed after each workspace operation. The main window remains responsive because discovery and EF calls are awaited rather than executed synchronously on the UI thread.

## Testing seams

`IClock`, `IProjectRepository`, `IProjectOnboardingStore`, `IFileDiscoveryService`, `IProjectFolderPicker`, `IProjectWorkspaceService`, and `IThemePreferenceStore` provide meaningful seams for deterministic tests. Unit tests use in-memory doubles for application boundaries and temporary directories. Integration tests use the real EF Core SQLite context and the checked-in migration in a temporary database.
