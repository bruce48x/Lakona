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
        Assert.Equal("瀏覽…", item.ActionText);
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

    [Fact]
    public void Dispose_StopsObservingLocalizationChanges()
    {
        var localization = new HubLocalization(HubLanguage.SimplifiedChinese);
        var item = new ApplicationToolItem(LocalApplicationKind.Godot, localization);
        var notifications = 0;
        item.PropertyChanged += (_, _) => notifications++;

        item.Dispose();
        localization.SetLanguage(HubLanguage.English);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void ManualTool_ShowsItsOwnNameAndCanBeRemoved()
    {
        var localization = new HubLocalization(HubLanguage.SimplifiedChinese);
        var installation = new LocalApplicationInstallation(
            LocalApplicationKind.Unity,
            "Unity 2022",
            Path.Combine(Path.GetTempPath(), "Unity.exe"),
            "2022.3.62f1");
        var item = new ApplicationToolItem(installation, localization, isManual: true);

        Assert.Equal("Unity 2022", item.DisplayName);
        Assert.Equal("手动添加 · 2022.3.62f1", item.StatusText);
        Assert.Equal("移除", item.ActionText);
        Assert.Equal(installation.ExecutablePath, item.ManualPath);
    }

    [Fact]
    public void ToolList_KeepsMultipleEditorVersionsAndUnavailableManualTools()
    {
        var localization = new HubLocalization(HubLanguage.English);
        var unity2022 = Path.Combine(Path.GetTempPath(), "2022", "Unity.exe");
        var unity6 = Path.Combine(Path.GetTempPath(), "6", "Unity.exe");
        var customIde = Path.Combine(Path.GetTempPath(), "missing", "CustomIde.exe");
        var installed = new[]
        {
            new LocalApplicationInstallation(LocalApplicationKind.UnityHub, "Unity Hub", "Unity Hub.exe", "3.16"),
            new LocalApplicationInstallation(LocalApplicationKind.Unity, "Unity", unity2022, "2022.3"),
            new LocalApplicationInstallation(LocalApplicationKind.Unity, "Unity 6", unity6, "6000.3")
        };
        var manual = new[]
        {
            new ManualApplicationRegistration(LocalApplicationKind.Unity, "Unity 6", unity6),
            new ManualApplicationRegistration(LocalApplicationKind.Other, "Custom IDE", customIde)
        };

        var tools = ApplicationToolList.Build(installed, manual, localization);

        Assert.Equal(2, tools.Count(tool => tool.Kind == LocalApplicationKind.Unity));
        Assert.Contains(tools, tool => tool.Kind == LocalApplicationKind.UnityHub);
        Assert.Contains(tools, tool => tool.DisplayName == "Unity 6" && tool.IsManual);
        Assert.Contains(tools, tool =>
            tool.DisplayName == "Custom IDE" &&
            tool.StatusText == "Configured path unavailable");
    }

    [Fact]
    public void ToolList_HidesUndetectedTuanjieToolsAndShowsEveryDetectedInstallation()
    {
        var localization = new HubLocalization(HubLanguage.English);

        var empty = ApplicationToolList.Build([], [], localization);

        Assert.DoesNotContain(empty, tool => tool.Kind is LocalApplicationKind.TuanjieHub or LocalApplicationKind.Tuanjie);

        var detected = ApplicationToolList.Build(
        [
            new LocalApplicationInstallation(LocalApplicationKind.TuanjieHub, "Tuanjie Hub", @"C:\Program Files\Tuanjie Hub\Tuanjie Hub.exe"),
            new LocalApplicationInstallation(LocalApplicationKind.Tuanjie, "Tuanjie", @"C:\Program Files\Tuanjie\Hub\Editor\2022.3.61t11\Editor\Tuanjie.exe", "1.6.10"),
            new LocalApplicationInstallation(LocalApplicationKind.Tuanjie, "Tuanjie", @"C:\Program Files\Tuanjie\Hub\Editor\2022.3.61t8\Editor\Tuanjie.exe", "1.6.7")
        ],
        [],
        localization);

        Assert.Single(detected, tool => tool.Kind == LocalApplicationKind.TuanjieHub);
        Assert.Equal(2, detected.Count(tool => tool.Kind == LocalApplicationKind.Tuanjie));
    }
}
