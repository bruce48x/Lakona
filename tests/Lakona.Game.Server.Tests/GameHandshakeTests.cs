using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameHandshakeTests
{
    [Fact]
    public async Task ServerHello_reports_reliable_push_enabled_mode()
    {
        using var provider = CreateProvider(reliablePushEnabled: true);
        var service = provider.GetRequiredService<IGameHandshakeService>();

        var hello = await service.HandshakeAsync(
            new GameClientHello { ProtocolVersionMin = 1, ProtocolVersionMax = 1 },
            "websocket",
            "memorypack",
            CancellationToken.None);

        Assert.True(hello.ReliablePush.Enabled);
        Assert.Equal("reliable", hello.ReliablePush.DeliveryMode);
        Assert.True(hello.ReliablePush.AckRequired);
        Assert.True(hello.ReliablePush.ReplaySupported);
        Assert.Equal(256, hello.ReliablePush.MaxPending);
        Assert.Equal("node-a", hello.ServerNodeId);
    }

    [Fact]
    public async Task ServerHello_reports_immediate_mode_when_reliable_push_disabled()
    {
        using var provider = CreateProvider(reliablePushEnabled: false);
        var service = provider.GetRequiredService<IGameHandshakeService>();

        var hello = await service.HandshakeAsync(
            new GameClientHello { ProtocolVersionMin = 1, ProtocolVersionMax = 1 },
            "websocket",
            "memorypack",
            CancellationToken.None);

        Assert.False(hello.ReliablePush.Enabled);
        Assert.Equal("immediate", hello.ReliablePush.DeliveryMode);
        Assert.False(hello.ReliablePush.AckRequired);
        Assert.False(hello.ReliablePush.ReplaySupported);
    }

    [Fact]
    public void ReliablePush_options_bind_from_configuration()
    {
        using var provider = CreateProvider(reliablePushEnabled: true, maxPending: 64);

        var options = provider.GetRequiredService<ReliablePushOptions>();

        Assert.True(options.Enabled);
        Assert.Equal(64, options.MaxPendingPerOwner);
    }

    [Fact]
    public void ReliablePush_options_ignore_legacy_lakona_game_root()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:ReliablePush:Enabled"] = "false",
                ["Lakona.Game:ReliablePush:MaxPendingPerOwner"] = "13"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLakonaGameServerReliablePush(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<ReliablePushOptions>();
        Assert.True(options.Enabled);
        Assert.Equal(256, options.MaxPendingPerOwner);
    }

    private static ServiceProvider CreateProvider(bool reliablePushEnabled, int? maxPending = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "node-a",
            ["Lakona:ReliablePush:Enabled"] = reliablePushEnabled.ToString()
        };
        if (maxPending.HasValue)
        {
            values["Lakona:ReliablePush:MaxPendingPerOwner"] = maxPending.Value.ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new ServiceCollection()
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();
    }
}
