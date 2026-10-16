namespace ForgeDesk.Presentation.Models;

public sealed class ProjectOpenedEventArgs : EventArgs
{
    public ProjectOpenedEventArgs(ProjectOpenResult project)
    {
        Project = project;
    }

    public ProjectOpenResult Project { get; }
}
