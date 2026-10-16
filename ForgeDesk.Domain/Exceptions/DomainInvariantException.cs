namespace ForgeDesk.Domain;

/// <summary>Raised when a domain entity cannot represent an invalid state.</summary>
public sealed class DomainInvariantException : InvalidOperationException
{
    public DomainInvariantException(string message) : base(message)
    {
    }
}
