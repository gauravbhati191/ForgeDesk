using ForgeDesk.Presentation.Models;

namespace ForgeDesk.Presentation.Contracts;

/// <summary>Resolves persisted relative source paths and loads a read-only preview.</summary>
public interface ISourceNavigationService
{
    Task<SourcePreviewResult> LoadPreviewAsync(
        string projectRootPath,
        string relativePath,
        int? selectedLine = null,
        CancellationToken cancellationToken = default);
}
