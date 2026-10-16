using ForgeDesk.Presentation.Contracts;
using Microsoft.Win32;

namespace ForgeDesk.App.Services;

/// <summary>
/// Windows-native folder picker implementation. The presentation layer only knows the
/// IProjectFolderPicker boundary, keeping the shell testable and platform-neutral.
/// </summary>
public sealed class WindowsFolderPicker : IProjectFolderPicker
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a ForgeDesk project folder",
            Multiselect = false
        };

        var result = dialog.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(result == true ? dialog.FolderName : null);
    }
}
