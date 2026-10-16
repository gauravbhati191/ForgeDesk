namespace ForgeDesk.Presentation.Contracts;

/// <summary>
/// Application-resource boundary for switching WPF theme dictionaries.
/// </summary>
public interface IThemeResourceManager
{
    void Apply(ThemeMode theme);
}
