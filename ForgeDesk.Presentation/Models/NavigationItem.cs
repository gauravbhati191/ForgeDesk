namespace ForgeDesk.Presentation.Models;

public sealed record NavigationItem(
    NavigationPage Page,
    string Label,
    string IconKey,
    bool IsAvailable = true);
