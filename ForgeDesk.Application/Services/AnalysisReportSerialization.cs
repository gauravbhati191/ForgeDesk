using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeDesk.Application;

/// <summary>Stable finding identity used by report comparison and exposed in exports.</summary>
public static class AnalysisFindingFingerprint
{
    private const char Separator = '\u001f';

    /// <summary>
    /// Creates a SHA-256 fingerprint from rule, normalized relative path, symbol identity,
    /// source location, and normalized message. Severity is deliberately excluded so a
    /// severity-only change is reported as changed rather than removed/new.
    /// </summary>
    public static string Create(AnalysisReportFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var canonical = string.Join(
            Separator,
            NormalizeText(finding.RuleId),
            NormalizeRelativePath(finding.RelativePath),
            NormalizeSymbol(finding.SymbolKey, finding.SymbolName),
            finding.StartLine?.ToString(CultureInfo.InvariantCulture) ?? "<missing-start>",
            finding.EndLine?.ToString(CultureInfo.InvariantCulture) ?? "<missing-end>",
            NormalizeText(finding.Message));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>Returns a canonical path token used by the comparison service.</summary>
    public static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "<missing-path>";
        }

        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? "<missing-path>"
            : normalized.ToUpperInvariant();
    }

    /// <summary>Collapses whitespace so a line-ending-only message change does not create a new identity.</summary>
    public static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "<missing>"
            : string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static string NormalizeSymbol(string? symbolKey, string? symbolName)
        => !string.IsNullOrWhiteSpace(symbolKey)
            ? NormalizeText(symbolKey)
            : !string.IsNullOrWhiteSpace(symbolName)
                ? "<name>" + NormalizeText(symbolName)
                : "<missing-symbol>";
}

/// <summary>Safe generated report file names and destination validation.</summary>
public static class AnalysisReportFileNames
{
    public static string Create(string projectName, Guid scanId, AnalysisReportFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        if (scanId == Guid.Empty)
        {
            throw new ArgumentException("A scan identifier is required.", nameof(scanId));
        }

        return $"{Sanitize(projectName)}-analysis-{scanId:N}{ExtensionFor(format)}";
    }

    public static string Sanitize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars())
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*'
        };
        var builder = new StringBuilder(value.Trim().Length);
        foreach (var character in value.Trim())
        {
            builder.Append(char.IsControl(character) || invalid.Contains(character) ? '-' : character);
        }

        var sanitized = string.Join(
            '-',
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        sanitized = sanitized.Trim(' ', '.');
        if (sanitized.Length == 0)
        {
            sanitized = "project";
        }

        if (sanitized.Length > 80)
        {
            sanitized = sanitized[..80].TrimEnd(' ', '.');
        }

        if (IsReservedWindowsName(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    /// <summary>
    /// Validates a caller-selected destination without creating directories or following
    /// traversal segments. The destination directory must already exist.
    /// </summary>
    public static string ValidateOutputPath(string outputPath, AnalysisReportFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var rawPath = outputPath.Trim();
        if (rawPath.Any(char.IsControl))
        {
            throw new AnalysisReportExportException("The report output path contains control characters.");
        }

        var pathSegments = rawPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Any(segment => segment == ".."))
        {
            throw new AnalysisReportExportException("Report output paths cannot contain traversal segments.");
        }

        var fileName = Path.GetFileName(rawPath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new AnalysisReportExportException("A report file name is required.");
        }

        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars())
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*'
        };
        if (fileName.Any(character => char.IsControl(character) || invalid.Contains(character)))
        {
            throw new AnalysisReportExportException("The report file name contains invalid characters.");
        }

        if (fileName.EndsWith(' ') || fileName.EndsWith('.') || IsReservedWindowsName(Path.GetFileNameWithoutExtension(fileName)))
        {
            throw new AnalysisReportExportException("The report file name is not safe on Windows.");
        }

        var expectedExtension = ExtensionFor(format);
        if (!string.Equals(Path.GetExtension(fileName), expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new AnalysisReportExportException($"A {format} report must use the '{expectedExtension}' extension.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            throw new AnalysisReportExportException("The report output path is invalid.", exception);
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new AnalysisReportExportException("The report output directory does not exist.");
        }

        return fullPath;
    }

    public static string ExtensionFor(AnalysisReportFormat format)
        => format switch
        {
            AnalysisReportFormat.Json => ".json",
            AnalysisReportFormat.Html => ".html",
            AnalysisReportFormat.Csv => ".csv",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The report format is not supported.")
        };

    private static bool IsReservedWindowsName(string value)
    {
        var stem = value.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] is >= '1' and <= '9');
    }
}

/// <summary>Deterministic serializers for all supported report formats.</summary>
public static class AnalysisReportSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default
    };

    static AnalysisReportSerializer()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static string ToJson(AnalysisReportModel report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string ToHtml(AnalysisReportModel report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var html = new StringBuilder(8 * 1024);
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<title>ForgeDesk analysis report</title><style>");
        html.Append("*{box-sizing:border-box}body{margin:0;background:#f5f7fb;color:#1c2430;font-family:Segoe UI,Arial,sans-serif;line-height:1.45}main{max-width:1180px;margin:0 auto;padding:2rem 1rem 4rem}h1,h2{margin:0 0 .75rem}h1{font-size:1.8rem}h2{font-size:1.2rem;margin-top:1.75rem}.muted{color:#5c6878}.warning{border-left:4px solid #b7791f;background:#fff8e6;padding:.8rem 1rem;margin:.5rem 0}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:.75rem;margin:1rem 0}.card{background:#fff;border:1px solid #dbe2ec;border-radius:8px;padding:.9rem}.card strong{display:block;font-size:1.45rem}.meta{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:.3rem 1.5rem;background:#fff;border:1px solid #dbe2ec;border-radius:8px;padding:1rem}.meta dt{font-weight:600;color:#526074}.meta dd{margin:0 0 .35rem;overflow-wrap:anywhere}section{margin-top:1.5rem}table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #dbe2ec;font-size:.9rem}th,td{padding:.55rem .6rem;border-bottom:1px solid #e7ebf1;text-align:left;vertical-align:top}th{background:#edf2f8;font-weight:600}td{overflow-wrap:anywhere}.severity{font-weight:600}.code{font-family:Consolas,monospace;font-size:.86em;word-break:break-word}.pill{display:inline-block;border-radius:999px;background:#e9eef6;padding:.1rem .45rem}.grid-two{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:1rem}.group{background:#fff;border:1px solid #dbe2ec;border-radius:8px;padding:.8rem}.group ul{margin:.3rem 0 0;padding-left:1.2rem}@media print{body{background:#fff}main{max-width:none;padding:.2in}.card,.meta,.group,table{break-inside:avoid;border-color:#aaa}section{break-inside:avoid}}@media(max-width:600px){main{padding:.9rem .6rem}table{font-size:.8rem;display:block;overflow-x:auto;white-space:nowrap}}");
        html.Append("</style></head><body><main>");
        html.Append("<h1>ForgeDesk analysis report</h1><p class=\"muted\">Schema ");
        AppendHtmlText(html, report.SchemaVersion);
        html.Append(" · Fingerprint ");
        AppendHtmlText(html, report.FingerprintVersion);
        html.Append("</p>");

        html.Append("<section><h2>Project and selected analysis</h2><dl class=\"meta\">");
        AppendMeta(html, "Project", report.Project.Name);
        AppendMeta(html, "Project type", report.Project.ProjectType);
        AppendMeta(html, "Project status", report.Project.Status);
        AppendMeta(html, "Root path", report.Project.RootPath ?? (report.Project.RootPathRedacted ? "[redacted]" : "[not available]"));
        AppendMeta(html, "Scan ID", report.Scan.ScanId.ToString());
        AppendMeta(html, "Discovery status", report.Scan.DiscoveryStatus);
        AppendMeta(html, "Analysis status", report.Scan.AnalysisStatus);
        AppendMeta(html, "Tool version", report.Scan.ToolVersion);
        AppendMeta(html, "Analysis started", FormatDate(report.Scan.AnalysisStartedAtUtc));
        AppendMeta(html, "Analysis completed", FormatDate(report.Scan.AnalysisCompletedAtUtc));
        html.Append("</dl></section>");

        html.Append("<section><h2>Summary</h2><div class=\"cards\">");
        AppendCard(html, "Source files", report.Summary.SourceFileCount.ToString(CultureInfo.InvariantCulture));
        AppendCard(html, "Analyzed files", FormatCount(report.Summary.AnalyzedSourceFileCount));
        AppendCard(html, "Symbols", FormatCount(report.Summary.SymbolCount));
        AppendCard(html, "Dependencies", FormatCount(report.Summary.DependencyCount));
        AppendCard(html, "Findings", FormatCount(report.Summary.FindingCount));
        html.Append("</div></section>");

        if (report.Limitations.Count > 0)
        {
            html.Append("<section><h2>Limitations and warnings</h2>");
            foreach (var limitation in report.Limitations)
            {
                html.Append("<div class=\"warning\">");
                AppendHtmlText(html, limitation);
                html.Append("</div>");
            }

            html.Append("</section>");
        }

        html.Append("<section><h2>Findings by group</h2><div class=\"grid-two\">");
        AppendGroup(html, "Severity", report.Summary.FindingsBySeverity);
        AppendGroup(html, "Rule", report.Summary.FindingsByRule);
        AppendGroup(html, "Source file", report.Summary.FindingsByFile);
        html.Append("</div></section>");

        html.Append("<section><h2>Findings</h2>");
        if (report.Findings.Count == 0)
        {
            html.Append("<p class=\"muted\">No persisted findings are present for this selected run.</p>");
        }
        else
        {
            html.Append("<table><thead><tr><th>Severity</th><th>Rule</th><th>Message</th><th>Source</th><th>Lines</th><th>Symbol</th></tr></thead><tbody>");
            foreach (var finding in report.Findings)
            {
                html.Append("<tr><td><span class=\"severity\">");
                AppendHtmlText(html, finding.Severity);
                html.Append("</span></td><td class=\"code\">");
                AppendHtmlText(html, finding.RuleId);
                html.Append("</td><td>");
                AppendHtmlText(html, finding.Message);
                html.Append("</td><td class=\"code\">");
                AppendHtmlText(html, finding.RelativePath ?? "[not available]");
                html.Append("</td><td>");
                AppendHtmlText(html, FormatLineRange(finding.StartLine, finding.EndLine));
                html.Append("</td><td class=\"code\">");
                AppendHtmlText(html, finding.SymbolName ?? finding.SymbolKey ?? "[not available]");
                html.Append("</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        html.Append("</section>");
        if (report.Summary.DependencyStatistics is { } dependencyStatistics)
        {
            html.Append("<section><h2>Dependencies</h2><p>Total: ");
            AppendHtmlText(html, dependencyStatistics.TotalCount.ToString(CultureInfo.InvariantCulture));
            html.Append(" · Resolved: ");
            AppendHtmlText(html, dependencyStatistics.ResolvedCount.ToString(CultureInfo.InvariantCulture));
            html.Append(" · Unresolved: ");
            AppendHtmlText(html, dependencyStatistics.UnresolvedCount.ToString(CultureInfo.InvariantCulture));
            html.Append("</p>");
            AppendGroup(html, "Relationship type", dependencyStatistics.ByRelationshipType);
            html.Append("</section>");
        }

        if (report.GitContext is { } git)
        {
            html.Append("<section><h2>Git context</h2><dl class=\"meta\">");
            AppendMeta(html, "Repository state", git.State.ToString());
            AppendMeta(html, "Repository root", git.RepositoryRootPath ?? (git.RepositoryRootPathRedacted ? "[redacted]" : "[not available]"));
            AppendMeta(html, "Branch", git.BranchName ?? (git.IsDetached ? "[detached HEAD]" : "[not available]"));
            AppendMeta(html, "HEAD", git.HeadCommitId ?? "[not available]");
            AppendMeta(html, "Working tree", git.IsDirty ? "Dirty" : "Clean");
            AppendMeta(html, "Changed files", git.ChangedFileCount.ToString(CultureInfo.InvariantCulture));
            AppendMeta(html, "Association", git.AssociationNote);
            if (!string.IsNullOrWhiteSpace(git.ErrorMessage))
            {
                AppendMeta(html, "Git error", git.ErrorMessage);
            }

            html.Append("</dl></section>");
        }

        html.Append("</main></body></html>");
        return html.ToString();
    }

    public static string ToCsv(AnalysisReportModel report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var csv = new StringBuilder();
        AppendCsvRow(csv, "fingerprint", "severity", "ruleId", "message", "relativePath", "startLine", "endLine", "symbol");
        foreach (var finding in report.Findings)
        {
            AppendCsvRow(
                csv,
                finding.Fingerprint,
                finding.Severity,
                finding.RuleId,
                finding.Message,
                finding.RelativePath,
                finding.StartLine?.ToString(CultureInfo.InvariantCulture),
                finding.EndLine?.ToString(CultureInfo.InvariantCulture),
                finding.SymbolName ?? finding.SymbolKey);
        }

        return csv.ToString();
    }

    private static void AppendMeta(StringBuilder html, string label, string? value)
    {
        html.Append("<div><dt>");
        AppendHtmlText(html, label);
        html.Append("</dt><dd>");
        AppendHtmlText(html, value ?? "[not available]");
        html.Append("</dd></div>");
    }

    private static void AppendCard(StringBuilder html, string label, string value)
    {
        html.Append("<div class=\"card\"><span class=\"muted\">");
        AppendHtmlText(html, label);
        html.Append("</span><strong>");
        AppendHtmlText(html, value);
        html.Append("</strong></div>");
    }

    private static void AppendGroup(StringBuilder html, string title, IReadOnlyDictionary<string, int> values)
    {
        html.Append("<div class=\"group\"><strong>");
        AppendHtmlText(html, title);
        html.Append("</strong>");
        if (values.Count == 0)
        {
            html.Append("<p class=\"muted\">No data</p></div>");
            return;
        }

        html.Append("<ul>");
        foreach (var pair in values)
        {
            html.Append("<li>");
            AppendHtmlText(html, pair.Key);
            html.Append(": ");
            AppendHtmlText(html, pair.Value.ToString(CultureInfo.InvariantCulture));
            html.Append("</li>");
        }

        html.Append("</ul></div>");
    }

    private static void AppendHtmlText(StringBuilder html, string? value)
        => html.Append(WebUtility.HtmlEncode(value ?? string.Empty));

    private static void AppendCsvRow(StringBuilder csv, params string?[] fields)
    {
        for (var index = 0; index < fields.Length; index++)
        {
            if (index > 0)
            {
                csv.Append(',');
            }

            var value = fields[index] ?? string.Empty;
            var escaped = value.Replace("\"", "\"\"");
            if (value.IndexOfAny([',', '"', '\r', '\n']) >= 0)
            {
                csv.Append('"').Append(escaped).Append('"');
            }
            else
            {
                csv.Append(escaped);
            }
        }

        csv.Append("\r\n");
    }

    private static string FormatCount(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "not available";

    private static string FormatDate(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "not available";

    private static string FormatLineRange(int? startLine, int? endLine)
        => startLine is null && endLine is null
            ? "[not available]"
            : startLine == endLine
                ? startLine?.ToString(CultureInfo.InvariantCulture) ?? "[not available]"
                : $"{startLine?.ToString(CultureInfo.InvariantCulture) ?? "?"}-{endLine?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
}
