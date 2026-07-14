using Lakona.Hub.Applications;
using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ProjectListItemTests
{
    [Fact]
    public void ConsoleClient_DefaultsToRiderAndFollowsServerEditorSelection()
    {
        var rider = Application(LocalApplicationKind.Rider, "Rider");
        var visualStudio = Application(LocalApplicationKind.VisualStudio, "Visual Studio");
        var vsCode = Application(LocalApplicationKind.VisualStudioCode, "VS Code");
        var inspection = new LakonaProjectInspection(
            Path.GetTempPath(),
            "Console game",
            LakonaProjectStatus.Ready,
            LakonaProjectClient.Console,
            null,
            "1.0.0",
            []);

        var item = ProjectListItem.FromInspection(inspection, [vsCode, visualStudio, rider]);

        Assert.Same(rider, item.SelectedServerEditor);
        Assert.Same(rider, item.ClientApplication);
        Assert.Equal("Rider 打开", item.ClientActionText);

        item.SelectedServerEditor = visualStudio;

        Assert.Same(visualStudio, item.ClientApplication);
        Assert.Equal("Visual Studio 打开", item.ClientActionText);
    }

    private static LocalApplicationInstallation Application(LocalApplicationKind kind, string name) =>
        new(kind, name, Path.Combine(Path.GetTempPath(), name + ".exe"));
}
