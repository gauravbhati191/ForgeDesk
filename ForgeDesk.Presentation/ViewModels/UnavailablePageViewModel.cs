namespace ForgeDesk.Presentation.ViewModels;

public sealed class UnavailablePageViewModel
{
    public UnavailablePageViewModel(
        string title,
        string capability,
        string reason)
    {
        Title = title;
        Capability = capability;
        Reason = reason;
    }

    public string Title { get; }

    public string Capability { get; }

    public string Reason { get; }

    public string Detail =>
        "This navigation surface is intentionally reserved for a later ForgeDesk integration phase.";
}

public sealed class UnavailablePageViewModelFactory
{
    public UnavailablePageViewModel Create(string title, string capability)
    {
        return new UnavailablePageViewModel(
            title,
            capability,
            "The required application service is not available in Phase 1.");
    }
}
