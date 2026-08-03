using Lakona.Hub.Applications;
using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubProjectBrowserTests
{
    [Fact]
    public void Query_FiltersTheObservableProjectView()
    {
        using var browser = new HubProjectBrowser();
        browser.AddOrReplace(Project("Alpha"));
        browser.AddOrReplace(Project("Beta"));

        browser.Query = "beta";

        Assert.Single(browser.VisibleProjects);
        Assert.Equal("Beta", browser.VisibleProjects[0].Name);
        Assert.False(browser.HasNoMatches);
    }

    [Fact]
    public void Remove_ReleasesTheProjectsLocalizationSubscription()
    {
        var localization = new HubLocalization(HubLanguage.SimplifiedChinese);
        using var browser = new HubProjectBrowser();
        var project = Project("Disposable", localization);
        var notifications = 0;
        project.PropertyChanged += (_, _) => notifications++;
        browser.AddOrReplace(project);

        Assert.True(browser.Remove(project));
        localization.SetLanguage(HubLanguage.English);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void UpdateServerEditor_AppliesOneSelectionToEveryProject()
    {
        using var browser = new HubProjectBrowser();
        browser.AddOrReplace(Project("Alpha"));
        browser.AddOrReplace(Project("Beta"));
        var rider = new LocalApplicationInstallation(
            LocalApplicationKind.Rider,
            "Rider",
            Path.Combine(Path.GetTempPath(), "Rider.exe"));

        browser.UpdateServerEditor(rider);

        Assert.All(browser.Projects, project =>
        {
            Assert.True(project.CanOpenServer);
            Assert.Same(rider, project.ClientApplication);
        });
    }

    private static ProjectListItem Project(string name, HubLocalization? localization = null) =>
        ProjectListItem.FromInspection(
            new LakonaProjectInspection(
                Path.Combine(Path.GetTempPath(), name),
                name,
                LakonaProjectStatus.Ready,
                LakonaProjectClient.Console,
                null,
                "1.0.0",
                []),
            Array.Empty<LocalApplicationInstallation>(),
            localization ?? new HubLocalization(HubLanguage.English));
}
