using Lakona.Hub.Applications;
using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubProjectBrowserTests
{
    [Fact]
    public void IdleTimeRefresh_UpdatesLabelsWithoutReplacingRowsOrSavingSettings()
    {
        using var browser = new HubProjectBrowser();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        for (var i = 0; i < 25; i++)
        {
            browser.AddOrReplace(Project($"Project {i:D2}", clock: clock));
        }
        var opened = browser.Projects[0];
        opened.MarkOpened();
        var rows = browser.VisibleProjects.ToArray();
        var timestamp = opened.LastOpenedAtUtc;
        var collectionChanges = 0;
        var saves = 0;
        var labelChanges = 0;
        browser.VisibleProjects.CollectionChanged += (_, _) => collectionChanges++;
        browser.PersistentStateChanged += (_, _) => saves++;
        opened.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectListItem.LastOpened)) labelChanges++;
        };

        for (var minute = 0; minute < 120; minute++)
        {
            clock.Now = clock.Now.AddMinutes(1);
            browser.RefreshLastOpened();
        }

        Assert.Equal("2 hours ago", opened.LastOpened);
        Assert.Equal(120, labelChanges);
        Assert.Equal(timestamp, opened.LastOpenedAtUtc);
        Assert.Equal(rows, browser.VisibleProjects);
        Assert.Equal(0, collectionChanges);
        Assert.Equal(0, saves);
    }

    [Fact]
    public void OpeningProject_StillUpdatesRecentOrderAndPersistsTimestamp()
    {
        using var browser = new HubProjectBrowser();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        var alpha = Project("Alpha", clock: clock);
        var beta = Project("Beta", clock: clock);
        browser.AddOrReplace(alpha);
        browser.AddOrReplace(beta);
        alpha.MarkOpened();
        var saves = 0;
        browser.PersistentStateChanged += (_, _) => saves++;

        clock.Now = clock.Now.AddMinutes(1);
        beta.MarkOpened();

        Assert.Same(beta, browser.VisibleProjects[0]);
        Assert.Equal(clock.Now, beta.LastOpenedAtUtc);
        Assert.Equal("Just now", beta.LastOpened);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void LanguageRefresh_UpdatesLabelsWithoutReplacingRowsOrSavingSettings()
    {
        using var browser = new HubProjectBrowser();
        var localization = new HubLocalization(HubLanguage.English);
        var project = Project("Alpha", localization);
        browser.AddOrReplace(project);
        var collectionChanges = 0;
        var saves = 0;
        browser.VisibleProjects.CollectionChanged += (_, _) => collectionChanges++;
        browser.PersistentStateChanged += (_, _) => saves++;

        localization.SetLanguage(HubLanguage.SimplifiedChinese);

        Assert.Equal("尚未打开", project.LastOpened);
        Assert.Same(project, Assert.Single(browser.VisibleProjects));
        Assert.Equal(0, collectionChanges);
        Assert.Equal(0, saves);
    }

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

    private static ProjectListItem Project(string name, HubLocalization? localization = null, TimeProvider? clock = null) =>
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
            localization ?? new HubLocalization(HubLanguage.English),
            timeProvider: clock);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
