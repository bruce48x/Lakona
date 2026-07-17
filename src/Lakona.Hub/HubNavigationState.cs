namespace Lakona.Hub;

internal enum HubPage
{
    Projects,
    Settings
}

internal enum HubExperience
{
    EmptyProjects,
    Projects,
    CreateProject,
    Settings
}

internal sealed class HubNavigationState(HubPage initialPage)
{
    public HubPage CurrentPage { get; private set; } = initialPage;

    public bool IsCreatingProject { get; private set; }

    public void Navigate(HubPage page)
    {
        CurrentPage = page;
        IsCreatingProject = false;
    }

    public void StartCreating()
    {
        CurrentPage = HubPage.Projects;
        IsCreatingProject = true;
    }

    public void CancelCreating() => IsCreatingProject = false;

    public HubExperience Experience(bool hasProjects) => CurrentPage switch
    {
        HubPage.Settings => HubExperience.Settings,
        _ when IsCreatingProject => HubExperience.CreateProject,
        _ when hasProjects => HubExperience.Projects,
        _ => HubExperience.EmptyProjects
    };
}
