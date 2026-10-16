using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeDesk.Presentation.Contracts;
using Microsoft.Extensions.Logging;

namespace ForgeDesk.Presentation.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IThemePreferenceStore _themePreferenceStore;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        IThemePreferenceStore themePreferenceStore,
        ILogger<SettingsViewModel> logger)
    {
        _themePreferenceStore = themePreferenceStore;
        _logger = logger;
        _themePreferenceStore.PreferenceChanged += OnPreferenceChanged;
        _theme = _themePreferenceStore.Current;
    }

    [ObservableProperty]
    private ThemeMode _theme;

    public string CurrentThemeLabel => Theme == ThemeMode.Dark ? "Dark" : "Light";

    public string ThemeDescription => Theme == ThemeMode.Dark
        ? "Dark surfaces reduce glare in low-light workspaces."
        : "Light surfaces keep the workspace bright in daylight.";

    public string ThemeButtonLabel => Theme == ThemeMode.Dark
        ? "Switch to light theme"
        : "Switch to dark theme";

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        var nextTheme = Theme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;

        try
        {
            await _themePreferenceStore.SetAsync(nextTheme);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to save the selected theme {Theme}.", nextTheme);
            StatusMessage = "The theme preference could not be saved.";
        }
    }

    [ObservableProperty]
    private string _statusMessage = "Theme preferences are ready.";

    partial void OnThemeChanged(ThemeMode value)
    {
        OnPropertyChanged(nameof(CurrentThemeLabel));
        OnPropertyChanged(nameof(ThemeDescription));
        OnPropertyChanged(nameof(ThemeButtonLabel));
        StatusMessage = $"{CurrentThemeLabel} theme selected.";
    }

    private void OnPreferenceChanged(object? sender, ThemePreferenceChangedEventArgs eventArgs)
    {
        Theme = eventArgs.Theme;
    }
}
