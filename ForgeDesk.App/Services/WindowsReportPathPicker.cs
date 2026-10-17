using ForgeDesk.Application;
using ForgeDesk.Presentation.Contracts;
using Microsoft.Win32;

namespace ForgeDesk.App.Services;

/// <summary>Native Windows save dialog used for explicit report exports.</summary>
public sealed class WindowsReportPathPicker : IReportPathPicker
{
    public string? PickSavePath(string suggestedFileName, AnalysisReportFormat format)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            CheckPathExists = true,
            FileName = suggestedFileName,
            Filter = format switch
            {
                AnalysisReportFormat.Json => "JSON report (*.json)|*.json|All files (*.*)|*.*",
                AnalysisReportFormat.Html => "HTML report (*.html)|*.html|All files (*.*)|*.*",
                AnalysisReportFormat.Csv => "CSV findings (*.csv)|*.csv|All files (*.*)|*.*",
                _ => "Report files|*.*"
            },
            OverwritePrompt = true,
            Title = "Export ForgeDesk analysis report"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
