using ForgeDesk.Application;
using ForgeDesk.Domain;
using ForgeDesk.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ForgeDesk.IntegrationTests;

public sealed class InfrastructurePersistenceTests
{
    [Fact]
    public async Task SQLite_migrations_persist_projects_discovery_and_reopen_without_duplicates()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var projectRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(projectRoot.Path, "Program.cs"), "class Program {}");
        File.WriteAllText(Path.Combine(projectRoot.Path, "App.csproj"), "<Project />");
        var projectRepository = new ProjectRepository(harness.Context);
        var discovery = new FileDiscoveryService();
        var onboarding = new ProjectOnboardingService(
            projectRepository,
            new ProjectValidationService(discovery),
            discovery,
            new ProjectOnboardingStore(harness.Context),
            new FixedClock(DateTimeOffset.UtcNow));

        var first = await onboarding.OnboardAsync(new ProjectOnboardingRequest("First", projectRoot.Path));
        var second = await onboarding.OnboardAsync(new ProjectOnboardingRequest("Second", projectRoot.Path + Path.DirectorySeparatorChar + "."));

        Assert.False(first.ReusedExistingProject);
        Assert.True(second.ReusedExistingProject);
        Assert.Equal(first.Project.Id, second.Project.Id);
        Assert.Single(await projectRepository.ListAsync());
        Assert.Equal(2, await harness.Context.Scans.CountAsync());
        Assert.Equal(4, await harness.Context.SourceFiles.CountAsync());
        Assert.Equal(ScanStatus.Completed, second.Scan.Status);
        Assert.Equal(1, second.SourceFileCount);
        Assert.Equal(1, second.SupportedProjectFileCount);
    }

    [Fact]
    public async Task SQLite_persists_scan_lifecycle_transitions_and_cascade_relationships()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        using var projectRoot = new TemporaryDirectory();
        var project = Project.Create("Scannable", projectRoot.Path, ProjectType.SourceFolder);
        await harness.Context.Projects.AddAsync(project);
        await harness.Context.SaveChangesAsync();
        var scans = new ScanRepository(harness.Context);
        var lifecycle = new ScanLifecycleService(new ProjectRepository(harness.Context), scans);

        var queued = await lifecycle.QueueAsync(project.Id, "test");
        var running = await lifecycle.StartAsync(queued.Id);
        var completed = await lifecycle.CompleteAsync(running.Id, 4, 4);

        var stored = await harness.Context.Scans.SingleAsync(scan => scan.Id == queued.Id);
        Assert.Equal(ScanStatus.Completed, stored.Status);
        Assert.Equal(4, stored.FilesDiscovered);
        Assert.Equal(4, completed.FilesProcessed);
        Assert.NotNull(stored.StartedAtUtc);
        Assert.NotNull(stored.CompletedAtUtc);

        harness.Context.Projects.Remove(project);
        await harness.Context.SaveChangesAsync();
        Assert.Equal(0, await harness.Context.Scans.CountAsync());
    }

    [Fact]
    public async Task SQLite_persists_theme_settings_as_an_upsert_and_recent_removal_keeps_history()
    {
        await using var harness = await SqliteTestHarness.CreateAsync();
        var repository = new SettingRepository(harness.Context);
        var settings = new ThemeSettingsService(repository);

        await settings.SetThemeAsync(ThemeMode.Dark);
        await settings.SetThemeAsync(ThemeMode.Light);
        var stored = await harness.Context.Settings.SingleAsync(setting => setting.Key == Setting.ThemeKey);
        Assert.Equal("Light", stored.Value);
        Assert.Equal(ThemeMode.Light, await settings.GetThemeAsync());
        Assert.Equal(1, await harness.Context.Settings.CountAsync());

        using var root = new TemporaryDirectory();
        var project = Project.Create("Recent", root.Path);
        project.MarkOpened(DateTimeOffset.UtcNow);
        await harness.Context.Projects.AddAsync(project);
        await harness.Context.SaveChangesAsync();
        await new ProjectRepository(harness.Context).RemoveFromRecentAsync(project.NormalizedRootPath);
        Assert.Empty(await new ProjectRepository(harness.Context).ListRecentAsync());
        Assert.Equal(1, await harness.Context.Projects.CountAsync());
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("forgedesk-integration-").FullName;
    public string Path { get; }
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
