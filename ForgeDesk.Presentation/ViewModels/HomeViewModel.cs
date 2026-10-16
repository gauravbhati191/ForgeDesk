namespace ForgeDesk.Presentation.ViewModels;

public sealed class HomeViewModel
{
    public string Title => "Welcome to ForgeDesk";

    public string Description =>
        "A focused workspace for understanding and improving software projects.";

    public string PrimaryActionTitle => "Open a project folder to get started.";

    public string WorkspaceCardTitle => "Workspace";

    public string WorkspaceCardDescription =>
        "Choose a local project folder and keep it close at hand for the next session.";

    public string AnalysisCardTitle => "Analysis";

    public string AnalysisCardDescription =>
        "Code Explorer, Dependencies, and Code Health will become available as the core services land.";

    public string DeliveryCardTitle => "Delivery";

    public string DeliveryCardDescription =>
        "Git Changes and Reports are reserved for the next integration phase.";
}
