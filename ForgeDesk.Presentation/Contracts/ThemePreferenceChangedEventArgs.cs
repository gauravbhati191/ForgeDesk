namespace ForgeDesk.Presentation.Contracts;

public sealed class ThemePreferenceChangedEventArgs : EventArgs
{
    public ThemePreferenceChangedEventArgs(ThemeMode theme)
    {
        Theme = theme;
    }

    public ThemeMode Theme { get; }
}
