namespace ForgeDesk.Domain;

/// <summary>Reserved future dependency result. Phase 1 does not populate dependencies.</summary>
public sealed class Dependency
{
    private Dependency()
    {
        RelationshipType = string.Empty;
    }

    private Dependency(Guid id, Guid scanId, Guid? sourceSymbolId, Guid? targetSymbolId, string relationshipType, bool isResolved)
    {
        if (id == Guid.Empty || scanId == Guid.Empty)
        {
            throw new DomainInvariantException("A dependency and scan identifier are required.");
        }
        if (sourceSymbolId is null && targetSymbolId is null)
        {
            throw new DomainInvariantException("A dependency must identify at least one symbol.");
        }

        Id = id;
        ScanId = scanId;
        SourceSymbolId = sourceSymbolId;
        TargetSymbolId = targetSymbolId;
        RelationshipType = ValidateText(relationshipType, "A dependency relationship type is required.", 128);
        IsResolved = isResolved;
    }

    public static Dependency Create(Guid scanId, Guid? sourceSymbolId, Guid? targetSymbolId, string relationshipType, bool isResolved = false)
        => new(Guid.NewGuid(), scanId, sourceSymbolId, targetSymbolId, relationshipType, isResolved);

    public Guid Id { get; private set; }
    public Guid ScanId { get; private set; }
    public Guid? SourceSymbolId { get; private set; }
    public Guid? TargetSymbolId { get; private set; }
    public string RelationshipType { get; private set; }
    public bool IsResolved { get; private set; }

    public Scan? Scan { get; private set; }
    public Symbol? SourceSymbol { get; private set; }
    public Symbol? TargetSymbol { get; private set; }

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
