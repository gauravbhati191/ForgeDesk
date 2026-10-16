namespace ForgeDesk.Domain;

/// <summary>A local source tree registered in ForgeDesk.</summary>
public sealed class Project
{
    private Project()
    {
        Name = string.Empty;
        RootPath = string.Empty;
        NormalizedRootPath = string.Empty;
    }

    public Project(
        Guid id,
        string name,
        string rootPath,
        ProjectType projectType = ProjectType.SourceFolder,
        DateTimeOffset? createdAtUtc = null,
        string? description = null)
    {
        if (id == Guid.Empty)
        {
            throw new DomainInvariantException("A project identifier is required.");
        }

        Id = id;
        Name = ValidateName(name);
        RootPath = ValidateRootPath(rootPath);
        NormalizedRootPath = PathNormalization.Normalize(RootPath);
        ProjectType = ValidateProjectType(projectType);
        Description = NormalizeDescription(description);
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        Status = ProjectStatus.Active;
    }

    public static Project Create(
        string name,
        string rootPath,
        ProjectType projectType = ProjectType.SourceFolder,
        DateTimeOffset? createdAtUtc = null,
        string? description = null)
        => new(Guid.NewGuid(), name, rootPath, projectType, createdAtUtc, description);

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string RootPath { get; private set; }
    public string NormalizedRootPath { get; private set; }
    public ProjectType ProjectType { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? LastScannedAtUtc { get; private set; }
    public bool IsFavorite { get; private set; }
    public DateTimeOffset? LastOpenedAtUtc { get; private set; }
    public string? Description { get; private set; }
    public ProjectStatus Status { get; private set; }

    // Compatibility aliases keep application vocabulary explicit while allowing older callers to migrate.
    public DateTimeOffset CreatedAt => CreatedAtUtc;
    public DateTimeOffset UpdatedAt => UpdatedAtUtc;

    public ICollection<Scan> Scans { get; private set; } = new List<Scan>();

    public void MarkOpened(DateTimeOffset openedAtUtc)
    {
        LastOpenedAtUtc = openedAtUtc;
        Touch(openedAtUtc);
    }

    public void ClearLastOpened()
    {
        LastOpenedAtUtc = null;
    }

    public void MarkScanned(DateTimeOffset scannedAtUtc)
    {
        LastScannedAtUtc = scannedAtUtc;
        Touch(scannedAtUtc);
    }

    public void SetFavorite(bool isFavorite, DateTimeOffset? changedAtUtc = null)
    {
        IsFavorite = isFavorite;
        Touch(changedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public void Rename(string name, DateTimeOffset? changedAtUtc = null)
    {
        Name = ValidateName(name);
        Touch(changedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public void ChangeRootPath(string rootPath, DateTimeOffset? changedAtUtc = null)
    {
        RootPath = ValidateRootPath(rootPath);
        NormalizedRootPath = PathNormalization.Normalize(RootPath);
        Touch(changedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public void Archive(DateTimeOffset? changedAtUtc = null)
    {
        Status = ProjectStatus.Archived;
        Touch(changedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public void Restore(DateTimeOffset? changedAtUtc = null)
    {
        Status = ProjectStatus.Active;
        Touch(changedAtUtc ?? DateTimeOffset.UtcNow);
    }

    private void Touch(DateTimeOffset changedAtUtc) => UpdatedAtUtc = changedAtUtc;

    private static ProjectType ValidateProjectType(ProjectType projectType)
    {
        if (!Enum.IsDefined(typeof(ProjectType), projectType))
        {
            throw new DomainInvariantException("The project type is not supported.");
        }

        return projectType;
    }

    private static string ValidateName(string name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 200)
        {
            throw new DomainInvariantException("A project name must contain between 1 and 200 characters.");
        }

        return value;
    }

    private static string ValidateRootPath(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new DomainInvariantException("A project root path is required.");
        }

        var value = rootPath.Trim();
        if (value.Length > 4096)
        {
            throw new DomainInvariantException("A project root path cannot exceed 4096 characters.");
        }

        return value;
    }

    private static string? NormalizeDescription(string? description)
    {
        var value = description?.Trim();
        if (value is not null && value.Length > 2000)
        {
            throw new DomainInvariantException("A project description cannot exceed 2000 characters.");
        }

        return string.IsNullOrEmpty(value) ? null : value;
    }
}
