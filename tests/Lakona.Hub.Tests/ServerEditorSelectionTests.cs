using Lakona.Hub.Applications;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ServerEditorSelectionTests
{
    [Fact]
    public void Refresh_DefaultsByServerEditorPriority()
    {
        var selection = new ServerEditorSelection();
        var rider = Application(LocalApplicationKind.Rider, "Rider");
        var visualStudio = Application(LocalApplicationKind.VisualStudio, "Visual Studio");

        selection.Refresh([visualStudio, rider]);

        Assert.Equal([rider, visualStudio], selection.Editors);
        Assert.Same(rider, selection.SelectedEditor);
    }

    [Fact]
    public void Refresh_RestoresThePersistedGlobalEditor()
    {
        var rider = Application(LocalApplicationKind.Rider, "Rider");
        var visualStudio = Application(LocalApplicationKind.VisualStudio, "Visual Studio");
        var selection = new ServerEditorSelection(visualStudio.ExecutablePath);

        selection.Refresh([rider, visualStudio]);

        Assert.Same(visualStudio, selection.SelectedEditor);
    }

    private static LocalApplicationInstallation Application(LocalApplicationKind kind, string name) =>
        new(kind, name, Path.Combine(Path.GetTempPath(), name + ".exe"));
}
