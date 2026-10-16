namespace ForgeDesk.Application;

public sealed class ProjectValidationException : InvalidOperationException
{
    public ProjectValidationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors)) => Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

public sealed class EntityNotFoundException : InvalidOperationException
{
    public EntityNotFoundException(string entityName, Guid id)
        : base($"{entityName} '{id}' was not found.")
    {
    }
}

public sealed class ProjectAlreadyExistsException : InvalidOperationException
{
    public ProjectAlreadyExistsException(string normalizedPath)
        : base($"A project with the normalized path '{normalizedPath}' is already registered.")
    {
    }
}
