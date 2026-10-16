using ForgeDesk.Application;
using ForgeDesk.Domain;

namespace ForgeDesk.UnitTests;

internal sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
    public DateTimeOffset UtcNow { get; }
}

internal sealed class InMemoryProjectRepository : IProjectRepository
{
    private readonly Dictionary<Guid, Project> _projects = new();

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_projects.GetValueOrDefault(id));

    public Task<Project?> GetByNormalizedPathAsync(string normalizedRootPath, CancellationToken cancellationToken = default)
    {
        var normalized = PathNormalization.Normalize(normalizedRootPath);
        return Task.FromResult(_projects.Values.SingleOrDefault(project => project.NormalizedRootPath == normalized));
    }

    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Project>>(_projects.Values.ToList());

    public Task<IReadOnlyList<Project>> ListRecentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Project>>(_projects.Values.Where(project => project.LastOpenedAtUtc is not null).OrderByDescending(project => project.LastOpenedAtUtc).ToList());

    public Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        _projects.Add(project.Id, project);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        _projects[project.Id] = project;
        return Task.CompletedTask;
    }

    internal void Save(Project project) => _projects[project.Id] = project;

    public Task RemoveFromRecentAsync(string normalizedRootPath, CancellationToken cancellationToken = default)
    {
        var project = _projects.Values.SingleOrDefault(item => item.NormalizedRootPath == PathNormalization.Normalize(normalizedRootPath));
        project?.ClearLastOpened();
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryScanRepository : IScanRepository
{
    private readonly Dictionary<Guid, Scan> _scans = new();

    public Task<Scan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_scans.GetValueOrDefault(id));

    public Task AddAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        _scans.Add(scan.Id, scan);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        _scans[scan.Id] = scan;
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySettingRepository : ISettingRepository
{
    private readonly Dictionary<string, Setting> _settings = new(StringComparer.Ordinal);

    public Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_settings.GetValueOrDefault(key));

    public Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Setting>>(_settings.Values.ToList());

    public Task UpsertAsync(Setting setting, CancellationToken cancellationToken = default)
    {
        _settings[setting.Key] = setting;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _settings.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryProjectOnboardingStore : IProjectOnboardingStore
{
    private readonly Dictionary<Guid, Project> _projects = new();
    private readonly Dictionary<Guid, Scan> _scans = new();
    private readonly Dictionary<Guid, List<SourceFile>> _files = new();
    private readonly InMemoryProjectRepository? _projectRepository;

    public InMemoryProjectOnboardingStore(InMemoryProjectRepository? projectRepository = null) => _projectRepository = projectRepository;

    public Task BeginAsync(Project project, Scan scan, CancellationToken cancellationToken = default)
    {
        _projects[project.Id] = project;
        _projectRepository?.Save(project);
        _scans[scan.Id] = scan;
        _files[scan.Id] = [];
        return Task.CompletedTask;
    }

    public Task SaveRunningAsync(Scan scan, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task CompleteAsync(Project project, Scan scan, IReadOnlyCollection<SourceFile> sourceFiles, CancellationToken cancellationToken = default)
    {
        _projects[project.Id] = project;
        _scans[scan.Id] = scan;
        _files[scan.Id] = sourceFiles.ToList();
        return Task.CompletedTask;
    }

    public Task SaveFailedAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        _scans[scan.Id] = scan;
        return Task.CompletedTask;
    }

    public Task SaveCancelledAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        _scans[scan.Id] = scan;
        return Task.CompletedTask;
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("forgedesk-tests-").FullName;
    public string Path { get; }
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
