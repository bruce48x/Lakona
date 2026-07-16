using Xunit;

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

        new HubUserSettingsStore(path).Save(new HubUserSettings(HubLanguage.TraditionalChinese, projectPaths));

        var loaded = new HubUserSettingsStore(path).Load(HubLanguage.English);

        Assert.Equal(HubLanguage.TraditionalChinese, loaded.Language);
        Assert.Equal(projectPaths, loaded.ProjectPaths);
    }

    [Fact]
    public void Load_UsesDetectedLanguageAndNoProjectsBeforeTheFirstSave()
    {
        var loaded = new HubUserSettingsStore(Path.Combine(root, "missing.json"))
            .Load(HubLanguage.SimplifiedChinese);

        Assert.Equal(HubLanguage.SimplifiedChinese, loaded.Language);
        Assert.Empty(loaded.ProjectPaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
