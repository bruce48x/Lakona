using Lakona.Hub.Applications;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ApplicationPathStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoad_RoundTripsManualApplicationPaths()
    {
        var store = new ApplicationPathStore(Path.Combine(root, "application-paths.json"));
        var paths = new Dictionary<LocalApplicationKind, string>
        {
            [LocalApplicationKind.Rider] = Path.Combine(root, "rider64.exe"),
            [LocalApplicationKind.Godot] = Path.Combine(root, "Godot.exe")
        };

        store.Save(paths);
        var loaded = store.Load();

        Assert.Equal(paths, loaded);
    }

    [Fact]
    public void Load_IgnoresCorruptSettings()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "application-paths.json");
        File.WriteAllText(path, "not json");

        Assert.Empty(new ApplicationPathStore(path).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
