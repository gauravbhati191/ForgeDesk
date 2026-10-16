using ForgeDesk.Application;
using ForgeDesk.Domain;
using ForgeDesk.Infrastructure;

namespace ForgeDesk.UnitTests;

public sealed class ApplicationServiceTests
{
    [Fact]
    public async Task Onboarding_reuses_a_duplicate_normalized_root_and_persists_discovery_metadata()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Main.cs"), "class Main {}");
        File.WriteAllText(Path.Combine(directory.Path, "Demo.csproj"), "<Project />");
        var projects = new InMemoryProjectRepository();
        var store = new InMemoryProjectOnboardingStore(projects);
        var discovery = new FileDiscoveryService();
        var onboarding = new ProjectOnboardingService(projects, new ProjectValidationService(discovery), discovery, store, new FixedClock(DateTimeOffset.UtcNow));

        var first = await onboarding.OnboardAsync(new ProjectOnboardingRequest("One", directory.Path));
        var second = await onboarding.OnboardAsync(new ProjectOnboardingRequest("Two", directory.Path + Path.DirectorySeparatorChar + "."));

        Assert.False(first.ReusedExistingProject);
        Assert.True(second.ReusedExistingProject);
        Assert.Equal(first.Project.Id, second.Project.Id);
        Assert.Single(await projects.ListAsync());
        Assert.Equal(1, second.SourceFileCount);
        Assert.Equal(1, second.SupportedProjectFileCount);
        Assert.Equal(ScanStatus.Completed, second.Scan.Status);
    }

    [Fact]
    public async Task Onboarding_rejects_missing_or_empty_roots_before_persistence()
    {
        using var directory = new TemporaryDirectory();
        var projects = new InMemoryProjectRepository();
        var discovery = new FileDiscoveryService();
        var onboarding = new ProjectOnboardingService(projects, new ProjectValidationService(discovery), discovery, new InMemoryProjectOnboardingStore(projects));

        await Assert.ThrowsAsync<ProjectValidationException>(() => onboarding.OnboardAsync(
            new ProjectOnboardingRequest("Missing", Path.Combine(directory.Path, "missing"))));

        using var empty = new TemporaryDirectory();
        var exception = await Assert.ThrowsAsync<ProjectValidationException>(() => onboarding.OnboardAsync(new ProjectOnboardingRequest("Empty", empty.Path)));
        Assert.Contains("No supported", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await projects.ListAsync());
    }

    [Fact]
    public async Task Scan_lifecycle_persists_pending_running_completed_and_counts()
    {
        using var directory = new TemporaryDirectory();
        var projects = new InMemoryProjectRepository();
        var scans = new InMemoryScanRepository();
        var project = Project.Create("Project", directory.Path);
        await projects.AddAsync(project);
        var lifecycle = new ScanLifecycleService(projects, scans, new FixedClock(DateTimeOffset.UtcNow));

        var queued = await lifecycle.QueueAsync(project.Id, "test");
        var running = await lifecycle.StartAsync(queued.Id);
        var completed = await lifecycle.CompleteAsync(running.Id, 8, 8);

        Assert.Equal(ScanStatus.Pending, queued.Status);
        Assert.Equal(ScanStatus.Running, running.Status);
        Assert.Equal(ScanStatus.Completed, completed.Status);
        Assert.Equal(8, completed.FilesDiscovered);
        Assert.Equal(8, completed.FilesProcessed);
        await Assert.ThrowsAsync<DomainInvariantException>(() => lifecycle.StartAsync(completed.Id));
    }

    [Fact]
    public async Task Theme_settings_default_dark_and_round_trip_through_a_repository()
    {
        var settings = new InMemorySettingRepository();
        var service = new ThemeSettingsService(settings, new FixedClock(DateTimeOffset.UtcNow));

        Assert.Equal(ThemeMode.Dark, await service.GetThemeAsync());
        Assert.Equal(ThemeMode.Light, await service.SetThemeAsync(ThemeMode.Light));
        Assert.Equal(ThemeMode.Light, await service.GetThemeAsync());
    }
}
