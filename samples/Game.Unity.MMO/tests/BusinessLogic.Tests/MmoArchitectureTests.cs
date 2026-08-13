using System.Runtime.Loader;
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
        Assert.True(WorldProtocol.WorldHalfExtent >= 100f);
        Assert.InRange(WorldProtocol.AttackCooldownTicks, 1, 20);
    }

    [Fact]
    public void EmbeddedUnityRpcCore_UsesParameterlessNotificationContractMarker()
    {
        var root = FindRepositoryRoot();
        var packageDirectory = Directory.GetDirectories(
            Path.Combine(root, "samples", "Game.Unity.MMO", "Client", "Assets", "Packages"),
            "Lakona.Rpc.Core.*");
        var package = Assert.Single(packageDirectory);
        var assemblyPath = Path.Combine(package, "lib", "netstandard2.1", "Lakona.Rpc.Core.dll");
        var loadContext = new AssemblyLoadContext("mmo-embedded-rpc-core", isCollectible: true);

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var attribute = assembly.GetType("Lakona.Rpc.Core.RpcNotificationContractAttribute", throwOnError: true)!;
            var constructor = Assert.Single(attribute.GetConstructors());
            Assert.Empty(constructor.GetParameters());
        }
        finally
        {
            loadContext.Unload();
        }
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
