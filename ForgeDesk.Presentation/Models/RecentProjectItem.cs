using ForgeDesk.Domain;

namespace ForgeDesk.Presentation.Models;

public sealed record RecentProjectItem(
    string Path,
    string DisplayName,
    ProjectType ProjectType,
    DateTimeOffset? LastOpened,
    DateTimeOffset? LastScanned,
    int SourceFileCount,
    int SupportedProjectFileCount,
    ScanStatus? ScanStatus,
    bool IsFavorite,
    bool IsMissing,
    string? Warning = null)
{
    public string ProjectTypeLabel => ProjectType switch
    {
        ProjectType.Solution => "Solution",
        ProjectType.CSharpProject => "C# project",
        _ => "Source folder"
    };

    public string ScanStatusLabel => ScanStatus?.ToString() ?? "Not scanned";

    public string FileSummary => $"{SourceFileCount} C# source files  •  {SupportedProjectFileCount} project files";

    public string LastOpenedLabel => LastOpened is null ? "Not opened yet" : $"Opened {LastOpened.Value:g}";

    public string FavoriteLabel => IsFavorite ? "Favorite" : string.Empty;

    public string AvailabilityLabel => IsMissing ? "Folder unavailable" : "Available";
}
