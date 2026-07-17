using Xunit;
using System.Text.Json;

namespace Lakona.Hub.Tests;

public sealed class HubUserSettingsStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoad_RestoresLanguageAndImportedProjectsInANewStoreInstance()
    {
        var path = Path.Combine(root, "user-settings.json");
        var projectPaths = new[]
        {
            Path.Combine(root, "FirstProject"),
            Path.Combine(root, "SecondProject")
        };

        var checkedAt = new DateTimeOffset(2026, 7, 16, 1, 2, 3, TimeSpan.Zero);
        var settings = new HubUserSettings(
            HubLanguage.TraditionalChinese,
            [new HubProjectSettings(projectPaths[0], Path.Combine(root, "Rider.exe"), checkedAt),
             new HubProjectSettings(projectPaths[1], null, null)],
            [new HubDetectedApplicationSettings("Unity", "Unity 6", Path.Combine(root, "Unity.exe"), "6000.3.3f1")],
            new HubCreationDraft("SavedGame", root, "godot", "4.6", "tcp", "json", "postgres", "embedded"),
            "Settings",
            new HubWindowSettings(120, 80, 1280, 900, "Maximized"),
            new HubUpdateCheckSettings(checkedAt, "0.2.18", "win-x64", "hub-v0.2.18", "Lakona.msi", "abc", 123));

        new HubUserSettingsStore(path).Save(settings);

        var loaded = new HubUserSettingsStore(path).Load(HubLanguage.English);

        Assert.Equal(HubLanguage.TraditionalChinese, loaded.Language);
        Assert.Equal(projectPaths, loaded.Projects.Select(project => project.Path));
        Assert.Equal(settings.Projects[0].SelectedServerEditorPath, loaded.Projects[0].SelectedServerEditorPath);
        Assert.Equal(settings.Projects[0].LastOpenedAtUtc, loaded.Projects[0].LastOpenedAtUtc);
        Assert.Equal(settings.DetectedApplications[0], Assert.Single(loaded.DetectedApplications));
        Assert.Equal(settings.CreationDraft, loaded.CreationDraft);
        Assert.Equal(settings.CurrentPage, loaded.CurrentPage);
        Assert.Equal(settings.Window, loaded.Window);
        Assert.Equal(settings.UpdateCheck, loaded.UpdateCheck);
    }

    [Fact]
    public void Load_UsesDetectedLanguageAndNoProjectsBeforeTheFirstSave()
    {
        var loaded = new HubUserSettingsStore(Path.Combine(root, "missing.json"))
            .Load(HubLanguage.SimplifiedChinese);

        Assert.Equal(HubLanguage.SimplifiedChinese, loaded.Language);
        Assert.Empty(loaded.Projects);
        Assert.Empty(loaded.DetectedApplications);
        Assert.Null(loaded.CreationDraft);
        Assert.Null(loaded.Window);
        Assert.Null(loaded.UpdateCheck);
    }

    [Fact]
    public void Load_MigratesVersionOneLanguageAndProjectPaths()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "user-settings.json");
        var projectPath = Path.Combine(root, "LegacyProject");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            language = "English",
            projectPaths = new[] { projectPath }
        }));

        var loaded = new HubUserSettingsStore(path).Load(HubLanguage.SimplifiedChinese);

        Assert.Equal(HubLanguage.English, loaded.Language);
        Assert.Equal(projectPath, Assert.Single(loaded.Projects).Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
