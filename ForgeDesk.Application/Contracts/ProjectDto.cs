using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Read model used by the desktop shell and recent-project list.</summary>
public sealed record ProjectDto(
    Guid Id,
    string Name,
    string RootPath,
    string NormalizedRootPath,
    ProjectType ProjectType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastScannedAtUtc,
    bool IsFavorite,
    DateTimeOffset? LastOpenedAtUtc,
    ProjectStatus Status,
    string? Description)
{
    public static ProjectDto From(Project project)
        => new(project.Id, project.Name, project.RootPath, project.NormalizedRootPath, project.ProjectType, project.CreatedAtUtc, project.UpdatedAtUtc, project.LastScannedAtUtc, project.IsFavorite, project.LastOpenedAtUtc, project.Status, project.Description);
}
