using Lakona.Hub.Applications;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubApplicationRegistryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DetectAsync_RebuildsToolsAndReleasesThePreviousItems()
    {
        var localization = new HubLocalization(HubLanguage.SimplifiedChinese);
        using var registry = new HubApplicationRegistry(
            new InstalledApplicationCatalog(new EmptyProbeSource()),
            new ManualApplicationStore(Path.Combine(root, "applications.json")),
            localization,
            []);
        var previous = registry.Tools[0];
        var notifications = 0;
        previous.PropertyChanged += (_, _) => notifications++;

        await registry.DetectAsync(CancellationToken.None);
        localization.SetLanguage(HubLanguage.English);

        Assert.Equal(0, notifications);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class EmptyProbeSource : IApplicationProbeSource
    {
        public IEnumerable<LocalApplicationInstallation> FindApplications() => [];
    }
}
