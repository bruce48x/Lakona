using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameHandshakeTests
{
    [Fact]
    public async Task ServerHello_reports_heartbeat_policy()
    {
        using var provider = CreateProvider(
            reliablePushEnabled: true,
            heartbeatInterval: TimeSpan.FromSeconds(6),
            heartbeatTimeout: TimeSpan.FromSeconds(18));
        var service = provider.GetRequiredService<IGameHandshakeService>();

        var hello = await service.HandshakeAsync(
            new GameClientHello { ProtocolVersion = 1 },
            "websocket",
            "memorypack",
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(6), hello.Heartbeat.Interval);
        Assert.Equal(TimeSpan.FromSeconds(18), hello.Heartbeat.Timeout);
    }

    [Fact]
    public async Task ServerHello_reports_reliable_push_enabled_policy()
    {
        using var provider = CreateProvider(reliablePushEnabled: true);
        var service = provider.GetRequiredService<IGameHandshakeService>();

        var hello = await service.HandshakeAsync(
            new GameClientHello { ProtocolVersion = 1 },
            "websocket",
            "memorypack",
            CancellationToken.None);

        Assert.True(hello.ReliablePush.Enabled);
        Assert.True(hello.ReliablePush.AckRequired);
    }

    [Fact]
    public async Task ServerHello_reports_reliable_push_disabled_policy()
    {
        using var provider = CreateProvider(reliablePushEnabled: false);
        var service = provider.GetRequiredService<IGameHandshakeService>();

        var hello = await service.HandshakeAsync(
            new GameClientHello { ProtocolVersion = 1 },
            "websocket",
            "memorypack",
            CancellationToken.None);

        Assert.False(hello.ReliablePush.Enabled);
        Assert.False(hello.ReliablePush.AckRequired);
    }

    [Fact]
    public async Task Handshake_rejects_unsupported_protocol_version()
    {
        var service = new GameHandshakeService(
            new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions { Id = "node-a" },
                Heartbeat = LakonaGameHeartbeatOptions.Defaults()
            },
            new ReliablePushOptions());

        var exception = await Assert.ThrowsAsync<GameHandshakeRejectedException>(async () =>
            await service.HandshakeAsync(
                new GameClientHello { ProtocolVersion = 2 },
                "kcp",
                "memorypack",
                TestContext.Current.CancellationToken));

        Assert.Contains("protocol version 1", exception.Message, StringComparison.Ordinal);
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

    private static ServiceProvider CreateProvider(
        bool reliablePushEnabled,
        int? maxPending = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? heartbeatTimeout = null)
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

        if (heartbeatInterval.HasValue)
        {
            values["Lakona:Heartbeat:Interval"] = heartbeatInterval.Value.ToString();
        }

        if (heartbeatTimeout.HasValue)
        {
            values["Lakona:Heartbeat:Timeout"] = heartbeatTimeout.Value.ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new ServiceCollection()
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();
    }
}
