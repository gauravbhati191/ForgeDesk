using ForgeDesk.Domain;

namespace ForgeDesk.Presentation.Models;

/// <summary>Read-only symbol metadata loaded from one persisted analysis scan.</summary>
public sealed record AnalysisSymbolItem(
    Guid Id,
    Guid ScanId,
    Guid SourceFileId,
    string SymbolKey,
    string Name,
    SymbolKind Kind,
    string? RelativePath,
    int? StartLine,
    int? EndLine)
{
    public string KindLabel => Kind.ToString();

    public string FullyQualifiedName
    {
        get
        {
            var identity = SymbolKey[(SymbolKey.LastIndexOf('/') + 1)..];
            var separator = identity.IndexOf(':');
            if (separator >= 0 && separator < identity.Length - 1)
            {
                identity = identity[(separator + 1)..];
            }

            var metadataSeparator = identity.IndexOf('|');
            if (metadataSeparator >= 0)
            {
                identity = identity[..metadataSeparator];
            }

            return identity.StartsWith("global::", StringComparison.Ordinal)
                ? identity[8..]
                : identity;
        }
    }

    public string? NamespaceName
    {
        get
        {
            if (Kind == SymbolKind.Namespace)
            {
                return FullyQualifiedName;
            }

            var segments = FullyQualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length <= 1)
            {
                return null;
            }

            var member = Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event;
            var length = member ? segments.Length - 2 : segments.Length - 1;
            return length > 0 ? string.Join('.', segments.Take(length)) : null;
        }
    }

    public string? ContainingTypeName
    {
        get
        {
            if (Kind is not (SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event))
            {
                return null;
            }

            var segments = FullyQualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 1
                ? string.Join('.', segments.Take(segments.Length - 1))
                : null;
        }
    }

    public string LocationLabel
    {
        get
        {
            var path = string.IsNullOrWhiteSpace(RelativePath)
                ? "Location unavailable"
                : RelativePath.Replace('\\', '/');

            if (StartLine is null)
            {
                return path;
            }

            return EndLine is not null && EndLine != StartLine
                ? $"{path}:{StartLine}-{EndLine}"
                : $"{path}:{StartLine}";
        }
    }
}
