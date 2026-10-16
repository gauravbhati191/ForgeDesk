namespace ForgeDesk.Domain;

/// <summary>Supported semantic relationship categories for analysis dependencies.</summary>
public enum DependencyKind
{
    ProjectReference = 0,
    PackageReference = 1,
    TypeReference = 2,
    Calls = 3,
    Inherits = 4,
    Implements = 5,
    Unknown = 6
}
