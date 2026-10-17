using ForgeDesk.Application;

namespace ForgeDesk.Presentation.Contracts;

/// <summary>Opens the native save dialog for a selected report representation.</summary>
public interface IReportPathPicker
{
    string? PickSavePath(string suggestedFileName, AnalysisReportFormat format);
}
