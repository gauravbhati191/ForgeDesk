namespace ForgeDesk.Domain;

/// <summary>Reserved future code-quality finding.</summary>
public sealed class Finding
{
    private Finding()
    {
        RuleId = string.Empty;
        Message = string.Empty;
    }

    private Finding(Guid id, Guid scanId, string ruleId, Guid? sourceFileId, Guid? symbolId, FindingSeverity severity, string message, int? startLine, int? endLine, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || scanId == Guid.Empty)
        {
            throw new DomainInvariantException("A finding and scan identifier are required.");
        }
        if (startLine is <= 0 || endLine is <= 0)
        {
            throw new DomainInvariantException("Finding line locations must be positive when provided.");
        }
        if (startLine is not null && endLine is not null && endLine < startLine)
        {
            throw new DomainInvariantException("A finding end line cannot precede its start line.");
        }

        Id = id;
        ScanId = scanId;
        RuleId = ValidateText(ruleId, "A finding rule identifier is required.", 256);
        SourceFileId = sourceFileId;
        SymbolId = symbolId;
        Severity = Enum.IsDefined(typeof(FindingSeverity), severity) ? severity : throw new DomainInvariantException("The finding severity is not supported.");
        Message = ValidateText(message, "A finding message is required.", 4000);
        StartLine = startLine;
        EndLine = endLine;
        CreatedAtUtc = createdAtUtc;
    }

    public static Finding Create(Guid scanId, string ruleId, FindingSeverity severity, string message, Guid? sourceFileId = null, Guid? symbolId = null, int? startLine = null, int? endLine = null, DateTimeOffset? createdAtUtc = null)
        => new(Guid.NewGuid(), scanId, ruleId, sourceFileId, symbolId, severity, message, startLine, endLine, createdAtUtc ?? DateTimeOffset.UtcNow);

    public Guid Id { get; private set; }
    public Guid ScanId { get; private set; }
    public string RuleId { get; private set; }
    public Guid? SourceFileId { get; private set; }
    public Guid? SymbolId { get; private set; }
    public FindingSeverity Severity { get; private set; }
    public string Message { get; private set; }
    public int? StartLine { get; private set; }
    public int? EndLine { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Scan? Scan { get; private set; }
    public SourceFile? SourceFile { get; private set; }
    public Symbol? Symbol { get; private set; }
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
