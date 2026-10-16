using ForgeDesk.Domain;

namespace ForgeDesk.Application;

/// <summary>Stores the selected theme in the local settings table.</summary>
public sealed class ThemeSettingsService : IThemeSettingsService
{
    private readonly ISettingRepository _settings;
    private readonly IClock _clock;

    public ThemeSettingsService(ISettingRepository settings, IClock? clock = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? new SystemClock();
    }

    public async Task<ThemeMode> GetThemeAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _settings.GetAsync(Setting.ThemeKey, cancellationToken).ConfigureAwait(false);
        return setting is not null && Enum.TryParse<ThemeMode>(setting.Value, true, out var theme) && Enum.IsDefined(theme)
            ? theme
            : ThemeMode.Dark;
    }

    public async Task<ThemeMode> SetThemeAsync(ThemeMode theme, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(theme))
        {
            throw new DomainInvariantException("The requested theme is not supported.");
        }
        await _settings.UpsertAsync(Setting.CreateTheme(theme, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
        return theme;
    }
}
