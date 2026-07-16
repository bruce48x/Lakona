using System.Text.Json;
using Lakona.Hub.Applications;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ManualApplicationStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoad_RoundTripsMultipleToolsOfTheSameKindAndArbitraryIde()
    {
        var store = new ManualApplicationStore(Path.Combine(root, "application-paths.json"));
        var applications = new[]
        {
            new ManualApplicationRegistration(LocalApplicationKind.Unity, "Unity 2022", Path.Combine(root, "2022", "Unity.exe")),
            new ManualApplicationRegistration(LocalApplicationKind.Unity, "Unity 6", Path.Combine(root, "6", "Unity.exe")),
            new ManualApplicationRegistration(LocalApplicationKind.Other, "Zed", Path.Combine(root, "zed.exe"))
        };

        store.Save(applications);
        var loaded = store.Load();

        Assert.Equal(applications, loaded);
    }

    [Fact]
    public void Load_MigratesLegacyPerKindPathsToManualRegistrations()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "application-paths.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [nameof(LocalApplicationKind.Rider)] = Path.Combine(root, "rider64.exe"),
            [nameof(LocalApplicationKind.Godot)] = Path.Combine(root, "Godot.exe")
        }));

        var loaded = new ManualApplicationStore(path).Load();

        Assert.Equal([LocalApplicationKind.Rider, LocalApplicationKind.Godot], loaded.Select(application => application.Kind));
        Assert.Equal(["Rider", "Godot"], loaded.Select(application => application.DisplayName));
    }

    [Fact]
    public void Load_IgnoresCorruptSettings()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "application-paths.json");
        File.WriteAllText(path, "not json");

        Assert.Empty(new ManualApplicationStore(path).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
