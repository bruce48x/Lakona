using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ProjectListViewTests
{
    [Fact]
    public void Apply_FiltersAcrossProjectMetadata_AndSortsByName()
    {
        var alpha = Item("Alpha", "Unity", "1.0.0");
        var beta = Item("Beta", "Godot", "2.0.0");

        var result = ProjectListView.Apply([beta, alpha], "unity", ProjectSortField.Name, false).ToArray();

        Assert.Equal([alpha], result);
    }

    [Fact]
    public void Apply_TimeSortUsesAddedTimeForProjectsThatHaveNeverBeenOpened()
    {
        var now = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var previouslyOpened = Item("PreviouslyOpened", "Console", "1.0.0", now.AddDays(-1), now.AddDays(-2));
        var newlyImported = Item("NewlyImported", "Console", "1.0.0", null, now);

        var result = ProjectListView.Apply(
            [previouslyOpened, newlyImported],
            query: null,
            ProjectSortField.LastOpened,
            descending: true).ToArray();

        Assert.Equal([newlyImported, previouslyOpened], result);
        Assert.Equal("Never opened", newlyImported.LastOpened);
    }

    private static ProjectListItem Item(
        string name,
        string client,
        string version,
        DateTimeOffset? lastOpenedAtUtc = null,
        DateTimeOffset? addedAtUtc = null) =>
        ProjectListItem.FromInspection(new LakonaProjectInspection(
            Path.Combine(Path.GetTempPath(), name), name, LakonaProjectStatus.Ready,
            Enum.Parse<LakonaProjectClient>(client), null, version, []),
            [],
            new HubLocalization(HubLanguage.English),
            lastOpenedAtUtc: lastOpenedAtUtc,
            addedAtUtc: addedAtUtc);
}
