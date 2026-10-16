namespace ForgeDesk.Domain;

/// <summary>Symbol categories persisted from C# semantic declarations.</summary>
public enum SymbolKind
{
    Unknown = 0,
    Namespace = 1,
    Class = 2,
    Struct = 3,
    Interface = 4,
    Enum = 5,
    Record = 6,
    Method = 7,
    Property = 8,
    Field = 9,
    Event = 10
}
