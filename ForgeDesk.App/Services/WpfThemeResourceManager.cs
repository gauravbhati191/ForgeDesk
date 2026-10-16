using System.Windows;
using ForgeDesk.Presentation.Contracts;

namespace ForgeDesk.App.Services;

/// <summary>
/// Applies the selected presentation dictionary to the supplied WPF application.
/// Application is injected at the composition root; no service locator is used.
/// </summary>
public sealed class WpfThemeResourceManager : IThemeResourceManager
{
    private readonly System.Windows.Application _application;
    private ResourceDictionary? _activeThemeDictionary;

    public WpfThemeResourceManager(System.Windows.Application application)
    {
        _application = application;
    }

    public void Apply(ThemeMode theme)
    {
        var dictionaries = _application.Resources.MergedDictionaries;
        _activeThemeDictionary ??= dictionaries.FirstOrDefault(IsThemeDictionary);

        if (_activeThemeDictionary is not null)
        {
            dictionaries.Remove(_activeThemeDictionary);
        }

        var source = new Uri(
            $"pack://application:,,,/ForgeDesk.Presentation;component/Resources/Themes/{theme}.xaml",
            UriKind.Absolute);
        var nextThemeDictionary = new ResourceDictionary { Source = source };
        dictionaries.Insert(0, nextThemeDictionary);
        _activeThemeDictionary = nextThemeDictionary;
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        return dictionary.Source?.OriginalString.Contains(
            "/Resources/Themes/",
            StringComparison.OrdinalIgnoreCase) == true;
    }
}
