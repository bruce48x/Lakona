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

        var item = ProjectListItem.FromInspection(
            inspection,
            [vsCode, visualStudio, rider],
            new HubLocalization(HubLanguage.SimplifiedChinese));

        Assert.Same(rider, item.SelectedServerEditor);
        Assert.Same(rider, item.ClientApplication);
        Assert.Equal("Rider 打开", item.ClientActionText);

        item.SelectedServerEditor = visualStudio;

        Assert.Same(visualStudio, item.ClientApplication);
        Assert.Equal("Visual Studio 打开", item.ClientActionText);
    }

    [Fact]
    public void ManualLanguageSwitch_UpdatesProjectActionsAndStatus()
    {
        var localization = new HubLocalization(HubLanguage.SimplifiedChinese);
        var rider = Application(LocalApplicationKind.Rider, "Rider");
        var inspection = new LakonaProjectInspection(
            Path.GetTempPath(),
            "",
            LakonaProjectStatus.Ready,
            LakonaProjectClient.Console,
            null,
            null,
            []);
        var item = ProjectListItem.FromInspection(inspection, [rider], localization);

        Assert.Equal("未命名项目", item.Name);
        Assert.Equal("项目结构完整", item.StatusText);
        Assert.Equal("Rider 打开", item.ClientActionText);

        localization.SetLanguage(HubLanguage.English);

        Assert.Equal("Unnamed project", item.Name);
        Assert.Equal("Project structure is complete", item.StatusText);
        Assert.Equal("Open in Rider", item.ClientActionText);
        Assert.Equal("Open", item.OpenText);
    }

    [Fact]
    public void RestoredProject_PrefersItsPersistedServerEditor()
    {
        var rider = Application(LocalApplicationKind.Rider, "Rider");
        var visualStudio = Application(LocalApplicationKind.VisualStudio, "Visual Studio");
        var inspection = new LakonaProjectInspection(
            Path.GetTempPath(),
            "SavedProject",
            LakonaProjectStatus.Ready,
            LakonaProjectClient.Console,
            null,
            null,
            []);

        var item = ProjectListItem.FromInspection(
            inspection,
            [rider, visualStudio],
            new HubLocalization(HubLanguage.English),
            visualStudio.ExecutablePath);

        Assert.Same(visualStudio, item.SelectedServerEditor);
    }

    [Fact]
    public void TuanjieClient_SelectsTheMatchingTuanjieEditorVersion()
    {
        var older = new LocalApplicationInstallation(
            LocalApplicationKind.Tuanjie,
            "Tuanjie",
            Path.Combine(Path.GetTempPath(), "2022.3.61t9", "Tuanjie.exe"),
            "1.6.8");
        var matching = new LocalApplicationInstallation(
            LocalApplicationKind.Tuanjie,
            "Tuanjie",
            Path.Combine(Path.GetTempPath(), "2022.3.61t8", "Tuanjie.exe"),
            "1.6.7");
        var inspection = new LakonaProjectInspection(
            Path.GetTempPath(),
            "Tuanjie game",
            LakonaProjectStatus.Ready,
            LakonaProjectClient.Tuanjie,
            "1.6.7",
            "1.0.0",
            []);

        var item = ProjectListItem.FromInspection(
            inspection,
            [older, matching],
            new HubLocalization(HubLanguage.SimplifiedChinese));

        Assert.Same(matching, item.ClientApplication);
        Assert.Equal("团结引擎 打开", item.ClientActionText);
    }

    private static LocalApplicationInstallation Application(LocalApplicationKind kind, string name) =>
        new(kind, name, Path.Combine(Path.GetTempPath(), name + ".exe"));
}
