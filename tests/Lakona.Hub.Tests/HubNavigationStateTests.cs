using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubNavigationStateTests
{
    [Fact]
    public void Experience_FollowsProjectsCreationAndSettingsState()
    {
        var navigation = new HubNavigationState(HubPage.Projects);

        Assert.Equal(HubExperience.EmptyProjects, navigation.Experience(hasProjects: false));
        Assert.Equal(HubExperience.Projects, navigation.Experience(hasProjects: true));

        navigation.StartCreating();
        Assert.Equal(HubExperience.CreateProject, navigation.Experience(hasProjects: true));

        navigation.Navigate(HubPage.Settings);
        Assert.Equal(HubExperience.Settings, navigation.Experience(hasProjects: true));
        Assert.False(navigation.IsCreatingProject);
    }
}
