using ForgeDesk.Domain;
using ForgeDesk.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.IntegrationTests;

public sealed class DatabaseBackupIntegrationTests
{
    [Fact]
    public async Task Backup_creation_uses_unique_non_overwriting_files_and_preserves_data()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "forgedesk.db");
        var backupDirectory = Path.Combine(directory.Path, "backups");
        await using (var context = CreateContext(databasePath))
        {
            await context.Database.MigrateAsync();
            await context.Projects.AddAsync(Project.Create(
                "Before backup",
                directory.Path,
                createdAtUtc: new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero)));
            await context.SaveChangesAsync();
        }

        var service = new SqliteDatabaseBackupService(new DatabasePathProvider(databasePath));
        var first = await service.CreateBackupAsync(backupDirectory);
        var second = await service.CreateBackupAsync(backupDirectory);

        Assert.NotEqual(first.BackupPath, second.BackupPath);
        Assert.True(first.SizeBytes > 0);
        Assert.True(second.SizeBytes > 0);
        Assert.True(File.Exists(first.BackupPath));
        Assert.True(File.Exists(second.BackupPath));
        Assert.Equal(2, Directory.GetFiles(backupDirectory, "*.db").Length);

        await using var readBack = CreateContext(first.BackupPath);
        Assert.Equal("Before backup", await readBack.Projects.Select(project => project.Name).SingleAsync());
    }

    [Fact]
    public async Task Restore_reads_back_the_backup_and_removes_only_changes_after_the_backup()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "forgedesk.db");
        var projectRoot = Path.Combine(directory.Path, "source");
        Directory.CreateDirectory(projectRoot);
        var baselineId = Guid.NewGuid();

        await using (var context = CreateContext(databasePath))
        {
            await context.Database.MigrateAsync();
            await context.Projects.AddAsync(new Project(
                baselineId,
                "Baseline",
                projectRoot,
                createdAtUtc: new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero)));
            await context.Settings.AddAsync(Setting.Create(
                "backup.marker",
                "before",
                new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero)));
            await context.SaveChangesAsync();
        }

        var service = new SqliteDatabaseBackupService(new DatabasePathProvider(databasePath));
        var backup = await service.CreateBackupAsync();

        await using (var context = CreateContext(databasePath))
        {
            var baseline = await context.Projects.SingleAsync(project => project.Id == baselineId);
            baseline.Rename("Changed after backup", new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
            await context.Projects.AddAsync(Project.Create(
                "Added after backup",
                Path.Combine(directory.Path, "later"),
                createdAtUtc: new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero)));
            var marker = await context.Settings.SingleAsync(setting => setting.Key == "backup.marker");
            marker.SetValue("after", new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
            await context.SaveChangesAsync();
        }

        await service.RestoreAsync(backup.BackupPath);

        await using var restored = CreateContext(databasePath);
        var restoredProject = await restored.Projects.SingleAsync(project => project.Id == baselineId);
        Assert.Equal("Baseline", restoredProject.Name);
        Assert.Equal("before", await restored.Settings.Select(setting => setting.Value).SingleAsync());
        Assert.DoesNotContain(await restored.Projects.Select(project => project.Name).ToListAsync(), name => name == "Added after backup");
    }

    [Fact]
    public async Task Existing_database_is_backed_up_before_pending_migrations_and_rows_are_preserved()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "forgedesk.db");
        var projectRoot = Path.Combine(directory.Path, "existing");
        Directory.CreateDirectory(projectRoot);
        var existingProjectId = Guid.NewGuid();

        await using (var oldContext = CreateContext(databasePath))
        {
            await oldContext.Database.GetService<IMigrator>().MigrateAsync("20261016053132_InitialCreate");
            await oldContext.Projects.AddAsync(new Project(
                existingProjectId,
                "Existing database row",
                projectRoot,
                createdAtUtc: new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero)));
            await oldContext.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddForgeDeskInfrastructure(options => options.DatabasePath = databasePath);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        }

        var backupDirectory = Path.Combine(directory.Path, "backups");
        var backups = Directory.GetFiles(backupDirectory, "*.db");
        Assert.Single(backups);

        await using var currentContext = CreateContext(databasePath);
        Assert.Equal("Existing database row", await currentContext.Projects
            .Where(project => project.Id == existingProjectId)
            .Select(project => project.Name)
            .SingleAsync());
        Assert.NotEmpty(await currentContext.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Backup_and_restore_report_missing_invalid_and_cancelled_paths_without_overwriting()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "forgedesk.db");
        await using (var context = CreateContext(databasePath))
        {
            await context.Database.MigrateAsync();
            await context.Settings.AddAsync(Setting.Create("failure.marker", "safe"));
            await context.SaveChangesAsync();
        }

        var service = new SqliteDatabaseBackupService(new DatabasePathProvider(databasePath));
        var outputFile = Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(outputFile, "reserved");

        var invalidOutput = await Assert.ThrowsAsync<DatabaseBackupException>(() => service.CreateBackupAsync(outputFile));
        Assert.Contains("backup", invalidOutput.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("reserved", await File.ReadAllTextAsync(outputFile));

        var missingBackup = Path.Combine(directory.Path, "missing-backup.db");
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.RestoreAsync(missingBackup));
        await Assert.ThrowsAsync<IOException>(() => service.RestoreAsync(directory.Path));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RestoreAsync(databasePath));

        var corruptBackup = Path.Combine(directory.Path, "corrupt-backup.db");
        await File.WriteAllTextAsync(corruptBackup, "not a SQLite database");
        var corruptRestore = await Assert.ThrowsAsync<DatabaseRestoreException>(() => service.RestoreAsync(corruptBackup));
        Assert.Contains("restore", corruptRestore.Message, StringComparison.OrdinalIgnoreCase);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.CreateBackupAsync(cancellationToken: cancelled.Token));

        await using var unchanged = CreateContext(databasePath);
        Assert.Equal("safe", await unchanged.Settings.Select(setting => setting.Value).SingleAsync());
    }

    private static ForgeDeskDbContext CreateContext(string databasePath)
        => new(new DbContextOptionsBuilder<ForgeDeskDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options);
}
