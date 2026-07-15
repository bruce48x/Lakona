using Lakona.Hub.Applications;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ApplicationToolItemTests
{
    [Fact]
    public void Update_ShowsDetectedPathVersionAndLocalizedStatus()
    {
        var localization = new HubLocalization(HubLanguage.English);
        var item = new ApplicationToolItem(LocalApplicationKind.Unity, localization);
        var installation = new LocalApplicationInstallation(
            LocalApplicationKind.Unity,
            "Unity",
            Path.Combine(Path.GetTempPath(), "Unity.exe"),
            "6000.3.3f1");

        item.Update(installation, null);

        Assert.Equal(installation.ExecutablePath, item.PathText);
        Assert.Equal("Detected · 6000.3.3f1", item.StatusText);

        localization.SetLanguage(HubLanguage.TraditionalChinese);
        Assert.Equal("已識別 · 6000.3.3f1", item.StatusText);
        Assert.Equal("瀏覽…", item.BrowseText);
    }

    [Fact]
    public void Update_KeepsUnavailableConfiguredPathVisible()
    {
        var localization = new HubLocalization(HubLanguage.SimplifiedChinese);
        var item = new ApplicationToolItem(LocalApplicationKind.Godot, localization);
        var configuredPath = Path.Combine(Path.GetTempPath(), "missing", "Godot.exe");

        item.Update(null, configuredPath);

        Assert.Equal(configuredPath, item.PathText);
        Assert.Equal("已设置的路径不可用", item.StatusText);
    }
}
