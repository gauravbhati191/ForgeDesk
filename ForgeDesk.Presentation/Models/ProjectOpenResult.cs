using ForgeDesk.Application;

namespace ForgeDesk.Presentation.Models;

public sealed record ProjectOpenResult(
    bool Succeeded,
    string FolderPath,
    string DisplayName,
    ProjectDto? Project = null,
    ScanDto? Scan = null,
    int SourceFileCount = 0,
    int SupportedProjectFileCount = 0,
    IReadOnlyList<string>? Warnings = null,
    string? Message = null)
{
    public IReadOnlyList<string> EffectiveWarnings => Warnings ?? Array.Empty<string>();
}
