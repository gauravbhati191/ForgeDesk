namespace ForgeDesk.Application;

/// <summary>Input supplied when a user opens a local folder.</summary>
public sealed record ProjectOnboardingRequest(string Name, string RootPath, string? Description = null);
