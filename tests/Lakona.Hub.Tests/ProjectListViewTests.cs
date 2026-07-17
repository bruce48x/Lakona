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

    private static ProjectListItem Item(string name, string client, string version) =>
        ProjectListItem.FromInspection(new LakonaProjectInspection(
            Path.Combine(Path.GetTempPath(), name), name, LakonaProjectStatus.Ready,
            Enum.Parse<LakonaProjectClient>(client), null, version, []), []);
}
