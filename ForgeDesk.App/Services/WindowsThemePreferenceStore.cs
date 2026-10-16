using System.IO;
using ForgeDesk.Presentation.Contracts;

namespace ForgeDesk.App.Services;

/// <summary>
/// Small desktop persistence adapter for the theme choice. It can be replaced by a core
/// preference service through IThemePreferenceStore without changing the viewmodels.
/// </summary>
public sealed class WindowsThemePreferenceStore : IThemePreferenceStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _preferencePath;
    private ThemeMode _current = ThemeMode.Dark;

    public WindowsThemePreferenceStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _preferencePath = Path.Combine(localAppData, "ForgeDesk", "theme-preference.txt");
    }

    public ThemeMode Current => _current;

    public event EventHandler<ThemePreferenceChangedEventArgs>? PreferenceChanged;

    public async Task<ThemeMode> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_preferencePath))
            {
                var savedValue = await File.ReadAllTextAsync(_preferencePath, cancellationToken);
                if (Enum.TryParse<ThemeMode>(savedValue.Trim(), ignoreCase: true, out var savedTheme))
                {
                    _current = savedTheme;
                }
            }

            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(ThemeMode theme, CancellationToken cancellationToken = default)
    {
        var changed = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_current == theme)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_preferencePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_preferencePath, theme.ToString(), cancellationToken);
            _current = theme;
            changed = true;
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            PreferenceChanged?.Invoke(this, new ThemePreferenceChangedEventArgs(theme));
        }
    }
}
