namespace ForgeDesk.Presentation.Contracts;

/// <summary>
/// Persistence boundary for the user's theme preference. A core or cloud-backed
/// implementation can replace the desktop implementation without changing viewmodels.
/// </summary>
public interface IThemePreferenceStore
{
    ThemeMode Current { get; }

    event EventHandler<ThemePreferenceChangedEventArgs>? PreferenceChanged;

    Task<ThemeMode> LoadAsync(CancellationToken cancellationToken = default);

    Task SetAsync(ThemeMode theme, CancellationToken cancellationToken = default);
}
