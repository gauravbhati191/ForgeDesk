namespace ForgeDesk.Presentation.Models;

/// <summary>Progress reported while analysis reads, parses, binds, and persists source data.</summary>
public sealed record AnalysisProgress(
    string Message,
    int Completed,
    int Total,
    int SymbolsFound = 0,
    int DependenciesFound = 0,
    int FindingsFound = 0,
    string? CurrentPath = null)
{
    public int Percent => Total <= 0
        ? 0
        : Math.Clamp((int)Math.Round(Completed * 100d / Total), 0, 100);

    public string Detail => string.IsNullOrWhiteSpace(CurrentPath)
        ? Message
        : $"{Message}: {CurrentPath}";
}
