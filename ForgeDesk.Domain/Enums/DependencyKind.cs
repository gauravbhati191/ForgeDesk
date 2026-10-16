namespace ForgeDesk.Domain;

/// <summary>Reserved relationship categories for Phase 2 dependency extraction.</summary>
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
