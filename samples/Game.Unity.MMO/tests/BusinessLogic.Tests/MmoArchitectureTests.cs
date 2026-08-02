using System.Text.Json;
using Shared.Interfaces;
using Xunit;

namespace BusinessLogic.Tests;

public sealed class MmoArchitectureTests
{
    [Fact]
    public void Server_UsesOneWebSocketEndpointForTheWorldService()
    {
        var root = FindRepositoryRoot();
        var json = File.ReadAllText(Path.Combine(root, "samples", "Game.Unity.MMO", "Server", "App", "appsettings.json"));
        using var document = JsonDocument.Parse(json);
        var endpoints = document.RootElement.GetProperty("Lakona").GetProperty("Endpoints").EnumerateArray().ToArray();
        var endpoint = Assert.Single(endpoints);
        Assert.Equal("websocket", endpoint.GetProperty("Transport").GetString());
        Assert.Equal(new[] { "world" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(static item => item.GetString()).ToArray());

        var clientPackages = File.ReadAllText(Path.Combine(root, "samples", "Game.Unity.MMO", "Client", "Assets", "packages.config"));
        var clientManifest = File.ReadAllText(Path.Combine(root, "samples", "Game.Unity.MMO", "Client", "Packages", "manifest.json"));
        var serverProject = File.ReadAllText(Path.Combine(root, "samples", "Game.Unity.MMO", "Server", "App", "Server.App.csproj"));
        Assert.DoesNotContain("Kcp", clientPackages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kcp", clientManifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kcp", serverProject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateSyncProtocol_HasBoundedTickAoiAndPopulation()
    {
        Assert.InRange(WorldProtocol.TickIntervalSeconds, 0.05f, 0.25f);
        Assert.InRange(WorldProtocol.InterestRadius, 1f, WorldProtocol.WorldHalfExtent * 2f);
        Assert.InRange(WorldProtocol.MaxCharacters, 1, 1000);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
