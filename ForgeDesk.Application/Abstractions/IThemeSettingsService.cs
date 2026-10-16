using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Stores and retrieves the user's selected theme.</summary>
public interface IThemeSettingsService
{
    Task<ThemeMode> GetThemeAsync(CancellationToken cancellationToken = default);
    Task<ThemeMode> SetThemeAsync(ThemeMode theme, CancellationToken cancellationToken = default);
}
