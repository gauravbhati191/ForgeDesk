using CoreThemeMode = ForgeDesk.Domain.ThemeMode;
using ForgeDesk.Application;
using ForgeDesk.Presentation.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDesk.App.Services;

/// <summary>Adapts the application settings service to a WPF-friendly theme preference boundary.</summary>
public sealed class ApplicationThemePreferenceStore : IThemePreferenceStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ThemeMode _current = ThemeMode.Dark;

    public ApplicationThemePreferenceStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public ThemeMode Current => _current;

    public event EventHandler<ThemePreferenceChangedEventArgs>? PreferenceChanged;

    public async Task<ThemeMode> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var stored = await scope.ServiceProvider.GetRequiredService<IThemeSettingsService>().GetThemeAsync(cancellationToken).ConfigureAwait(false);
            _current = FromCoreTheme(stored);
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(ThemeMode theme, CancellationToken cancellationToken = default)
    {
        var selected = theme == ThemeMode.Light ? ThemeMode.Light : ThemeMode.Dark;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current == selected)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var stored = await scope.ServiceProvider.GetRequiredService<IThemeSettingsService>().SetThemeAsync(ToCoreTheme(selected), cancellationToken).ConfigureAwait(false);
            _current = FromCoreTheme(stored);
        }
        finally
        {
            _gate.Release();
        }

        PreferenceChanged?.Invoke(this, new ThemePreferenceChangedEventArgs(_current));
    }

    private static ThemeMode FromCoreTheme(CoreThemeMode theme) => theme == CoreThemeMode.Light ? ThemeMode.Light : ThemeMode.Dark;

    private static CoreThemeMode ToCoreTheme(ThemeMode theme) => theme == ThemeMode.Light ? CoreThemeMode.Light : CoreThemeMode.Dark;
}
