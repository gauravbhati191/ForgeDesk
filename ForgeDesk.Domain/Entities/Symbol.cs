namespace ForgeDesk.Domain;

/// <summary>One Roslyn symbol extracted from a source file in an analysis scan.</summary>
public sealed class Symbol
{
    private Symbol()
    {
        SymbolKey = string.Empty;
        Name = string.Empty;
    }

    private Symbol(Guid id, Guid scanId, Guid sourceFileId, string symbolKey, string name, SymbolKind kind, int? startLine, int? endLine)
    {
        if (id == Guid.Empty || scanId == Guid.Empty || sourceFileId == Guid.Empty)
        {
            throw new DomainInvariantException("A symbol, scan, and source-file identifier are required.");
        }

        Id = id;
        ScanId = scanId;
        SourceFileId = sourceFileId;
        SymbolKey = ValidateText(symbolKey, "A symbol key is required.", 2000);
        Name = ValidateText(name, "A symbol name is required.", 512);
        Kind = Enum.IsDefined(typeof(SymbolKind), kind) ? kind : throw new DomainInvariantException("The symbol kind is not supported.");
        if (startLine is <= 0 || endLine is <= 0)
        {
            throw new DomainInvariantException("Symbol line locations must be positive when provided.");
        }
        if (startLine is not null && endLine is not null && endLine < startLine)
        {
            throw new DomainInvariantException("A symbol end line cannot precede its start line.");
        }

        StartLine = startLine;
        EndLine = endLine;
    }

    public static Symbol Create(Guid scanId, Guid sourceFileId, string symbolKey, string name, SymbolKind kind, int? startLine = null, int? endLine = null)
        => new(Guid.NewGuid(), scanId, sourceFileId, symbolKey, name, kind, startLine, endLine);

    public Guid Id { get; private set; }
    public Guid ScanId { get; private set; }
    public Guid SourceFileId { get; private set; }
    public string SymbolKey { get; private set; }
    public string Name { get; private set; }
    public SymbolKind Kind { get; private set; }
    public int? StartLine { get; private set; }
    public int? EndLine { get; private set; }

    public Scan? Scan { get; private set; }
    public SourceFile? SourceFile { get; private set; }

    private static string ValidateText(string text, string message, int maxLength)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new DomainInvariantException(message);
        }
        if (value.Length > maxLength)
        {
            throw new DomainInvariantException($"{message.TrimEnd('.')} cannot exceed {maxLength} characters.");
        }
        return value;
    }
}
