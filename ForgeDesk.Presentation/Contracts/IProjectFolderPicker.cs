namespace ForgeDesk.Presentation.Contracts;

/// <summary>
/// Native folder selection boundary. The Windows implementation lives in ForgeDesk.App.
/// </summary>
public interface IProjectFolderPicker
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}
