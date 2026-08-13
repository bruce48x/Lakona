using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Server.App;
using Server.App.Routing;
using Server.App.Matchmaking;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Server.App.Leaderboard;
using Server.Hotfix.Matchmaking;
using Server.Hotfix.Players;
using Server.Hotfix.Rooms;
using Server.Hotfix.Sessions;
using Server.Hotfix.Users;
using Shared.Interfaces;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class DistributedTopologyConfigurationTests
{
    private sealed class CapturingBattleCallback : IBattleCallback, IRpcNotificationDispatchTarget
    {
        public TaskCompletionSource<FrameSyncFrame> Frame { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnFrameSyncStarted(FrameSyncStart start)
        {
        }

        public void OnFrame(FrameSyncFrame frame)
        {
            Frame.TrySetResult(frame);
        }

        public void OnFrames(FrameSyncPush push)
        {
            foreach (var frame in push.Frames)
            {
                OnFrame(frame);
            }
        }

        public ValueTask DispatchNotificationAsync<TPayload>(
            int serviceId,
            int methodId,
            TPayload payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            switch (payload)
            {
                case FrameSyncStart start:
                    OnFrameSyncStarted(start);
                    break;
                case FrameSyncFrame frame:
                    OnFrame(frame);
                    break;
                case FrameSyncPush push:
                    OnFrames(push);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Unexpected battle callback payload '{typeof(TPayload).FullName}'.");
            }

            return default;
        }

        public ValueTask DispatchNotificationAsync(
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            switch (methodId)
            {
                case 1:
                    OnFrameSyncStarted(JsonSerializer.Deserialize<FrameSyncStart>(payload.Span)!);
                    break;
                case 2:
                    OnFrame(JsonSerializer.Deserialize<FrameSyncFrame>(payload.Span)!);
                    break;
                case 3:
                    OnFrames(JsonSerializer.Deserialize<FrameSyncPush>(payload.Span)!);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Unexpected battle callback method id '{methodId}'.");
            }

            return default;
        }
    }

    [Fact]
    public void DataNodeOwnsStateAndClusterEndpointWithoutClientEndpoints()
    {
        var configuration = BuildNodeEnvironmentConfiguration("data-1");
        var options = Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("data-1", options.Node.Id);
        Assert.Equal(new[] { "user", "matchmaking", "leaderboard" }, options.ActorHosts);
        Assert.Empty(options.Endpoints);
        Assert.Equal("tcp://10.0.0.1:21001", options.Cluster!.Endpoint);
        var peer = Assert.Single(options.Cluster.Peers);
        Assert.Equal("gateway-1", peer.Id);
        Assert.Equal("tcp://10.0.0.2:21002", peer.Endpoint);
        Assert.Equal(
            "AgarGamePostgres",
            configuration["Agar:Persistence:Postgres:ConnectionStringName"]);
        Assert.Equal(
            "AgarGameRedis",
            configuration["Agar:Persistence:Redis:ConnectionStringName"]);
    }

    [Fact]
    public void GatewayNodeOwnsOnlyWebSocketClientEndpoint()
    {
        var configuration = BuildNodeEnvironmentConfiguration("gateway-1");
        var options = Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("gateway-1", options.Node.Id);
        Assert.Empty(options.ActorHosts);

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal("websocket", endpoint.Transport);
        Assert.Equal("memorypack", endpoint.Serializer);
        Assert.Equal("0.0.0.0", endpoint.Host);
        Assert.Equal("gateway-1", endpoint.AdvertisedHost);
        Assert.Equal(20000, endpoint.Port);
        Assert.Equal("/ws", endpoint.Path);
        Assert.Equal(new[] { "login", "player" }, endpoint.RpcServices);
    }

    [Fact]
    public void BattleNodeOwnsRuntimeAndKcpEndpoint()
    {
        var configuration = BuildNodeEnvironmentConfiguration("battle-1");
        var options = Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("battle-1", options.Node.Id);
        Assert.Equal(new[] { "room" }, options.ActorHosts);

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal("kcp", endpoint.Transport);
        Assert.Equal("memorypack", endpoint.Serializer);
        Assert.Equal("0.0.0.0", endpoint.Host);
        Assert.Equal("battle-1", endpoint.AdvertisedHost);
        Assert.Equal(20001, endpoint.Port);
        Assert.Equal("", endpoint.Path);
        Assert.Equal(new[] { "battle" }, endpoint.RpcServices);
    }

    [Fact]
    public void DefaultAppsettingsExposeControlAndBattleRpcServicesSeparately()
    {
        using var document = Open("appsettings.json");
        var lakona = document.RootElement.GetProperty("Lakona");
        var actorHosts = lakona.GetProperty("ActorHosts").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.False(lakona.TryGetProperty("StartupActors", out _));
        var cluster = lakona.GetProperty("Cluster");
        var endpoints = lakona.GetProperty("Endpoints").EnumerateArray().ToArray();

        var control = endpoints.Single(endpoint =>
            string.Equals(endpoint.GetProperty("Transport").GetString(), "websocket", StringComparison.Ordinal));
        var battle = endpoints.Single(endpoint =>
            string.Equals(endpoint.GetProperty("Transport").GetString(), "kcp", StringComparison.Ordinal));

        Assert.Equal(new[] { "user", "matchmaking", "leaderboard", "room" }, actorHosts);
        Assert.Equal("tcp://127.0.0.1:21001", cluster.GetProperty("Endpoint").GetString());
        Assert.False(cluster.TryGetProperty("Serializer", out _));
        Assert.Empty(cluster.GetProperty("Peers").EnumerateArray());
        Assert.Equal(new[] { "login", "player" }, control.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "battle" }, battle.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void ProgramDoesNotScanRpcServicesOrSelectRealtimeOptions()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "App",
            "Program.cs");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("HasRpcService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectRealtimeOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RpcServices.Any", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new LakonaGameEndpointOptions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameworkHostUsesDefaultConfigurationPrecedenceWithAppBasePath()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Lakona.Game.Server",
            "Hosting",
            "LakonaGameServerBootstrapper.cs"));

        Assert.Contains(
            "WebApplication.CreateBuilder(new WebApplicationOptions",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Args = args", source, StringComparison.Ordinal);
        Assert.Contains("ContentRootPath = AppContext.BaseDirectory", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".SetBasePath(AppContext.BaseDirectory)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".AddJsonFile(\"appsettings.json\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".AddEnvironmentVariables()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AgarSampleUsesFrameworkOwnedSessionLifecycleHotfix()
    {
        var root = FindRepositoryRoot();
        var appText = ReadAllTextFiles(Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "App"));
        var hotfixText = ReadAllTextFiles(Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Hotfix"));

        Assert.DoesNotContain("AddLakonaGameSessionHotfixLifecycle", appText, StringComparison.Ordinal);
        Assert.Contains("AgarSessionLifecycle", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixLifecycle(typeof(IGameSessionLifecycle))]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("HotfixLifecycleCall<GameSessionDisconnectedRequest>", hotfixText, StringComparison.Ordinal);
        Assert.Contains("HotfixLifecycleCall<GameSessionExpiredRequest>", hotfixText, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerSessionLifecycleObserver", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("DisconnectedSessionCleanupHostedService", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkControlDisconnected", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("CleanupExpiredSession", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("AgarPlayerDisconnectRequest", appText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GatewayNodeRegistersControlServicesWithoutKcpEndpoint()
    {
        var services = BuildProgramServices("gateway-1");

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IActorRuntime>();
        provider.GetRequiredService<IGameSessionRegistry>();
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService(RequiredServerAppType("Server.App.Hotfix.Agar" + "Hotfix" + "Runtime" + "Events")));

        var endpoint = Assert.Single(provider.GetRequiredService<Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions>().Endpoints);
        Assert.Equal("websocket", endpoint.Transport);
        Assert.Equal("gateway-1", endpoint.AdvertisedHost);
    }

    [Fact]
    public async Task BattleNodeRegistersRuntimeServicesWithoutControlCoordinator()
    {
        var services = BuildProgramServices("battle-1");

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IActorRuntime>();
        provider.GetRequiredService<IGameSessionRegistry>();
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService(RequiredServerAppType("Server.App.Realtime.Room" + "Runtime" + "Host")));

        var endpoint = Assert.Single(provider.GetRequiredService<Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions>().Endpoints);
        Assert.Equal("kcp", endpoint.Transport);
        Assert.Equal("battle-1", endpoint.AdvertisedHost);

        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService(RequiredServerAppType("Server.App.Hotfix.Agar" + "Hotfix" + "Runtime" + "Events")));
    }

    [Theory]
    [InlineData("gateway-1")]
    [InlineData("battle-1")]
    public async Task HotfixReloadSucceedsForSplitRuntimeNodes(string nodeName)
    {
        var services = BuildProgramServices(nodeName);
        var hotfixAssemblyPath = TestHotfix.FindHotfixAssemblyPath();
        services.AddLakonaGameHotfix(
            new Lakona.Game.Server.Hotfix.Loading.CurrentDirectoryHotfixAssemblySource(
                Path.GetDirectoryName(hotfixAssemblyPath)!,
                Path.GetFileName(hotfixAssemblyPath)),
            TestHotfix.HostAssemblyNames());

        await using var provider = services.BuildServiceProvider();

        var reload = await provider
            .GetRequiredService<IHotfixManager>()
            .ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(reload.Succeeded, TestHotfix.BuildReloadDiagnostics(reload));
    }

    [Fact]
    public async Task HotfixReloadPublishesRoomActorHostWithoutPlacementOverride()
    {
        var services = BuildProgramServices("battle-1");
        var hotfixAssemblyPath = TestHotfix.FindHotfixAssemblyPath();
        services.AddLakonaGameHotfix(
            new Lakona.Game.Server.Hotfix.Loading.CurrentDirectoryHotfixAssemblySource(
                Path.GetDirectoryName(hotfixAssemblyPath)!,
                Path.GetFileName(hotfixAssemblyPath)),
            TestHotfix.HostAssemblyNames());

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IHotfixManager>();

        var reload = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(reload.Succeeded, TestHotfix.BuildReloadDiagnostics(reload));
        Assert.Contains(manager.Current.ActorHosts, host => host.Actor == "room");
    }

    [Fact]
    public async Task RoomCreationReturnsTheExecutingNodesAdvertisedBattleEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
        services.AddSingleton(new Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions
        {
            Node = new Lakona.Game.Server.Configuration.LakonaGameNodeOptions { Id = "battle-owner" },
            ActorHosts = [],
            Endpoints =
            [
                new Lakona.Game.Server.Configuration.LakonaGameEndpointOptions
                {
                    Transport = "quic",
                    Host = "0.0.0.0",
                    AdvertisedHost = "public-battle.example",
                    Port = 24001,
                    Path = "/realtime",
                    RpcServices = ["battle"]
                }
            ]
        });

        await using var provider = services.BuildServiceProvider();
        await TestHotfix.LoadCurrentRuntimeAsync(provider, cancellationToken);
        await provider.StartClusterMembershipAsync(cancellationToken);
        var roomId = new RoomId("owner-endpoint-room");
        await provider.GetRequiredService<ActorHosting>()
            .EnsureAsync<RoomActor>(ActorIdentity.Create<RoomActor, RoomId>(roomId), cancellationToken);

        var result = await provider.GetRequiredService<ActorAccess>()
            .Local<RoomActor>(roomId)
            .CallAsync(
                static behavior => behavior.CreateAsync,
                new RoomCreateRequest
                {
                    RoomId = roomId.Value,
                    MatchId = "owner-endpoint-match",
                    CreatedByUserId = "player-1",
                    CreatedAtUtc = DateTime.UtcNow,
                    Players =
                    [
                        new PlayerRoomAssignment
                        {
                            UserId = "player-1",
                            RoomId = roomId.Value,
                            MatchId = "owner-endpoint-match",
                            SeatIndex = 0
                        }
                    ]
                },
                cancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("battle-owner", result.Snapshot.RuntimeGateway.InstanceId);
        Assert.Equal("quic", result.Snapshot.RuntimeGateway.Transport);
        Assert.Equal("public-battle.example", result.Snapshot.RuntimeGateway.Host);
        Assert.Equal(24001, result.Snapshot.RuntimeGateway.Port);
        Assert.Equal("/realtime", result.Snapshot.RuntimeGateway.Path);
    }

    [Fact]
    public async Task MatchmakingKeepsTicketsQueuedWhenBattleRuntimeEndpointIsUnavailable()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();

        await using var provider = services.BuildReadyServiceProvider(
            TestContext.Current.CancellationToken);
        var actors = provider.GetRequiredService<IActorRuntime>();

        MatchmakingEnqueueResult? result = null;
        var playerIds = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var playerId = $"no-runtime-player-{i}";
            playerIds.Add(playerId);
            var login = await LoginAndAttachAsync(
                provider,
                playerId,
                $"control-{i}");

            result = await EnqueueAsync(provider, new MatchmakingEnqueueRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                EnqueuedAtUtc = DateTime.UtcNow
            });
        }

        Assert.NotNull(result);
        Assert.True(result.Queued);

        var status = await GetMatchmakingStatusAsync(provider);
        Assert.Equal(10, status.QueuedCount);
        foreach (var playerId in playerIds)
        {
            var snapshot = await GetSessionSnapshotAsync(provider, playerId);
            Assert.True(string.IsNullOrWhiteSpace(snapshot.CurrentRoomId));
            Assert.True(string.IsNullOrWhiteSpace(snapshot.CurrentMatchId));
            Assert.True(string.IsNullOrWhiteSpace(snapshot.RuntimeGateway.Host));
            Assert.Equal(0, snapshot.RuntimeGateway.Port);
        }

        Assert.Empty(actors.GetActiveActorIds(typeof(RoomActor)));
    }

    [Fact]
    public async Task MatchmakingAllocatesExpiredPartialBatchForDefaultSingleNodeCluster()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = BuildProgramServices("appsettings.json");
        var hotfixAssemblyPath = TestHotfix.FindHotfixAssemblyPath();
        services.AddLakonaGameHotfix(
            new Lakona.Game.Server.Hotfix.Loading.CurrentDirectoryHotfixAssemblySource(
                Path.GetDirectoryName(hotfixAssemblyPath)!,
                Path.GetFileName(hotfixAssemblyPath)),
            TestHotfix.HostAssemblyNames());

        await using var provider = services.BuildServiceProvider();
        var reload = await provider.GetRequiredService<IHotfixManager>().ReloadAsync(cancellationToken);
        if (!reload.Succeeded)
        {
            throw new InvalidOperationException(TestHotfix.BuildReloadDiagnostics(reload));
        }

        var hostedServices = provider
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .ToArray();
        var membership = hostedServices.Single(service =>
            service.GetType().Name == "ReplicatedClusterMembershipHostedService");
        await membership.StartAsync(cancellationToken);

        var actors = provider.GetRequiredService<IActorRuntime>();
        var localMember = Assert.Single(provider.GetRequiredService<IClusterMembership>().Current.Members);
        Assert.Equal(new NodeId("gateway-1"), localMember.Reference.Node);
        Assert.Equal("tcp://127.0.0.1:21001", localMember.ClusterEndpoint.Address);
        Assert.Contains(localMember.ActorHosts, host => host.Actor == "room");

        try
        {
            var login = await LoginAndAttachAsync(
                provider,
                "local-runtime-player",
                "control-local-runtime");

            var result = await EnqueueAsync(provider, new MatchmakingEnqueueRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                EnqueuedAtUtc = DateTime.UtcNow.AddSeconds(-6)
            });

            Assert.NotNull(result);
            Assert.True(result.Queued);

            await actors.AskAsync<MatchmakingActor, bool>(
                ActorId.From("default"),
                async (actor, _) =>
                {
                    await actor.RunTickAsync(new MatchmakingTickRequest
                    {
                        ObservedAtUtc = DateTime.UtcNow
                    });
                    return true;
                },
                cancellationToken);

            var status = await GetMatchmakingStatusAsync(provider);
            Assert.Equal(0, status.QueuedCount);

            var session = await GetSessionSnapshotAsync(provider, login.UserId);
            Assert.False(string.IsNullOrWhiteSpace(session.CurrentRoomId));
            Assert.False(string.IsNullOrWhiteSpace(session.CurrentMatchId));
            Assert.Equal("gateway-1", session.RuntimeGateway.InstanceId);
            Assert.Equal("kcp", session.RuntimeGateway.Transport);
            Assert.Equal("127.0.0.1", session.RuntimeGateway.Host);
            Assert.Equal(20001, session.RuntimeGateway.Port);
        }
        finally
        {
            await membership.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MatchmakingStartupTimerAllocatesExpiredPartialBatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = BuildProgramServices("appsettings.json");
        var hotfixAssemblyPath = TestHotfix.FindHotfixAssemblyPath();
        services.AddLakonaGameHotfix(
            new Lakona.Game.Server.Hotfix.Loading.CurrentDirectoryHotfixAssemblySource(
                Path.GetDirectoryName(hotfixAssemblyPath)!,
                Path.GetFileName(hotfixAssemblyPath)),
            TestHotfix.HostAssemblyNames());

        await using var provider = services.BuildServiceProvider();
        var reload = await provider.GetRequiredService<IHotfixManager>().ReloadAsync(cancellationToken);
        Assert.True(reload.Succeeded, TestHotfix.BuildReloadDiagnostics(reload));

        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToArray();
        var timerScheduler = hostedServices.Single(service => service.GetType().Name == "LakonaTimerScheduler");
        var actorStartup = hostedServices.Single(service => service.GetType().Name == "StartupActorHostedService");
        var membership = hostedServices.Single(service => service.GetType().Name == "ReplicatedClusterMembershipHostedService");

        await membership.StartAsync(cancellationToken);
        await timerScheduler.StartAsync(cancellationToken);
        await actorStartup.StartAsync(cancellationToken);
        try
        {
            var login = await LoginAndAttachAsync(
                provider,
                "startup-timer-player",
                "control-startup-timer");

            var result = await EnqueueAsync(provider, new MatchmakingEnqueueRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                EnqueuedAtUtc = DateTime.UtcNow.AddSeconds(-6)
            });
            Assert.True(result.Queued);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            while (!timeout.IsCancellationRequested)
            {
                var status = await GetMatchmakingStatusAsync(provider);
                if (status.QueuedCount == 0)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }

            Assert.Fail("The startup actor's matchmaking timer did not process the expired ticket.");
        }
        finally
        {
            await actorStartup.StopAsync(CancellationToken.None);
            await timerScheduler.StopAsync(CancellationToken.None);
            await membership.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BattleRuntimeTimerPublishesInputFrames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = BuildProgramServices("appsettings.json");
        services.Replace(ServiceDescriptor.Singleton<
            IGameSessionEstablishedNotifier,
            NoopGameSessionEstablishedNotifier>());
        var hotfixAssemblyPath = TestHotfix.FindHotfixAssemblyPath();
        services.AddLakonaGameHotfix(
            new Lakona.Game.Server.Hotfix.Loading.CurrentDirectoryHotfixAssemblySource(
                Path.GetDirectoryName(hotfixAssemblyPath)!,
                Path.GetFileName(hotfixAssemblyPath)),
            TestHotfix.HostAssemblyNames());

        await using var provider = services.BuildServiceProvider();
        var reload = await provider.GetRequiredService<IHotfixManager>().ReloadAsync(cancellationToken);
        Assert.True(reload.Succeeded, TestHotfix.BuildReloadDiagnostics(reload));

        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToArray();
        var timerScheduler = hostedServices.Single(service => service.GetType().Name == "LakonaTimerScheduler");
        var membership = hostedServices.Single(service => service.GetType().Name == "ReplicatedClusterMembershipHostedService");

        await membership.StartAsync(cancellationToken);
        await timerScheduler.StartAsync(cancellationToken);
        try
        {
            const string playerId = "battle-timer-player";
            const string roomId = "battle-timer-room";
            const string matchId = "battle-timer-match";
            var callback = new CapturingBattleCallback();
            var gameServer = provider.GetRequiredService<ILakonaGameServer>();
            var session = await gameServer.StartSessionAsync(
                playerId,
                "battle-timer-connection",
                cancellationToken);
            await using var connection = new TestCallbackConnection(
                provider,
                "battle-timer-connection",
                callback);
            await provider.GetRequiredService<ActorHosting>()
                .EnsureAsync<RoomActor>(ActorIdentity.Create<RoomActor, RoomId>(new RoomId(roomId)), cancellationToken);
            var actors = provider.GetRequiredService<ActorAccess>();

            await actors.Route<RoomActor>(new RoomId(roomId)).CallAsync(
                static behavior => behavior.CreateAsync,
                new RoomCreateRequest
                {
                    RoomId = roomId,
                    MatchId = matchId,
                    CreatedByUserId = playerId,
                    CreatedAtUtc = DateTime.UtcNow,
                    Players =
                    [
                        new PlayerRoomAssignment
                        {
                            UserId = playerId,
                            RoomId = roomId,
                            MatchId = matchId,
                            SessionToken = "battle-timer-token",
                            ConnectionId = "battle-timer-control",
                            ControlSessionId = "battle-timer-control-session",
                            AssignedAtUtc = DateTime.UtcNow
                        }
                    ]
                },
                cancellationToken);
            await actors.Route<RoomActor>(new RoomId(roomId)).CallAsync(
                static behavior => behavior.SetReadyAsync,
                new RoomPlayerReadyRequest
                {
                    UserId = playerId,
                    RoomId = roomId,
                    IsReady = true,
                    RealtimeSessionId = session.SessionId,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            var snapshot = await actors.Route<RoomActor>(new RoomId(roomId)).CallAsync(
                static behavior => behavior.GetSnapshotAsync,
                new RoomSnapshotRequest(),
                cancellationToken);
            Assert.True(snapshot.Revision > 3, $"Battle timer did not advance the room; revision={snapshot.Revision}.");

            var frame = await callback.Frame.Task.WaitAsync(
                TimeSpan.FromSeconds(3),
                cancellationToken);
            Assert.True(frame.Frame > 0);
            Assert.Contains(frame.Inputs, input => input.PlayerId == playerId);
        }
        finally
        {
            await timerScheduler.StopAsync(CancellationToken.None);
            await membership.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReleasePlayerCleansUserSessionWhenRemoteRoomLeaveFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestHotfix.LoadCurrentAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();

        await using var provider = services.BuildReadyServiceProvider(cancellationToken);
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorIdentity.Create<UserActor, UserId>(new UserId("player-stale")), cancellationToken);
        var login = await LoginAndAttachAsync(
            provider,
            "player-stale",
            "control-stale",
            "control-session-stale");
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorIdentity.Create<UserActor, UserId>(new UserId("player-stale")),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = "player-stale",
                SessionToken = login.SessionToken,
                ConnectionId = "control-stale",
                RoomId = "stale-room",
                MatchId = "stale-match",
                SeatIndex = 0,
                AssignedAtUtc = DateTime.UtcNow,
                RuntimeGateway = new Server.App.Routing.GatewayEndpointDescriptor
                {
                    InstanceId = "battle-remote",
                    Transport = "kcp",
                    Host = "battle-remote",
                    Port = 20001
                }
            }),
            cancellationToken);

        await ReleasePlayerThroughInternalBoundaryAsync(provider, "player-stale", "test stale room");

        var snapshot = await GetSessionSnapshotAsync(provider, "player-stale");
        Assert.Equal("", snapshot.ConnectionId);
        Assert.Equal("", snapshot.CurrentRoomId);
        Assert.Equal("", snapshot.CurrentMatchId);
    }

    [Fact]
    public async Task AgarStartupOrderHonorsActorConfigurationAfterSampleStateRegistration()
    {
        var configuration = BuildAppConfiguration("appsettings.json");
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLakonaGameRuntimeValidation();
        services.AddLakonaGameServer(configuration);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ActorRuntimeOptions>();

        Assert.Equal(TimeSpan.FromSeconds(5), options.CallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), options.SlowMessageThreshold);
    }

    [Fact]
    public async Task DataNodeRegistersReplicatedInMemoryClusterServices()
    {
        var services = BuildNodeServices("data-1");

        await using var provider = services.BuildServiceProvider();
        var runtimeOptions = provider.GetRequiredService<Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions>();

        Assert.Equal(["user", "matchmaking", "leaderboard"], runtimeOptions.ActorHosts);
        Assert.Equal("ActorLocationDirectory", provider.GetRequiredService<IActorDirectory>().GetType().Name);
        Assert.NotNull(provider.GetRequiredService<IClusterMembership>());
    }

    [Fact]
    public void AgarPostgresInitContainsOnlyBusinessStateSchema()
    {
        var root = FindRepositoryRoot();
        var initDirectory = Path.Combine(
            root,
            "samples",
            "Game.Unity.Agar",
            "infra",
            "postgres",
            "init");
        var scripts = Directory.GetFiles(initDirectory, "*.sql");
        var combined = string.Join('\n', scripts.Select(File.ReadAllText));

        Assert.DoesNotContain("lakona_cluster_nodes", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agar_users", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DockerComposeDynamicAddressesDoNotOverlapStaticGameNodes()
    {
        var compose = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "docker-compose.yml"));

        Assert.Contains("ipv4_address: 10.0.0.1", compose, StringComparison.Ordinal);
        Assert.Contains("ipv4_address: 10.0.0.2", compose, StringComparison.Ordinal);
        Assert.Contains("ipv4_address: 10.0.0.3", compose, StringComparison.Ordinal);
        Assert.Contains("ip_range: 10.0.0.128/25", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerComposeDefinesRuntimeConfigurationThroughEnvironmentVariables()
    {
        var compose = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "docker-compose.yml"));

        var data = ExtractComposeService(compose, "data-1");
        Assert.Contains("Lakona__Node__Id: data-1", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__ActorHosts: '[\"user\",\"matchmaking\",\"leaderboard\"]'", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__StartupActors", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.0.0.1:21001", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Cluster__Serializer", data, StringComparison.Ordinal);
        Assert.Contains(
            "Lakona__Cluster__Peers: '[{\"Id\":\"gateway-1\",\"Endpoint\":\"tcp://10.0.0.2:21002\"}]'",
            data,
            StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaClusterPostgres", data, StringComparison.Ordinal);
        Assert.Contains(
            "Agar__Persistence__Postgres__ConnectionStringName: AgarGamePostgres",
            data,
            StringComparison.Ordinal);
        Assert.Contains(
            "Agar__Persistence__Redis__ConnectionStringName: AgarGameRedis",
            data,
            StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Lakona__", "Fea", "ture"), data, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Endpoints__", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Cluster__Peers__", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Http__Listeners: >-", data, StringComparison.Ordinal);
        Assert.Contains("\"Id\": \"operations\"", data, StringComparison.Ordinal);
        Assert.Contains("\"Port\": 21000", data, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Exposure\"", data, StringComparison.Ordinal);
        Assert.Contains("\"Services\": [ \"agar-operations\" ]", data, StringComparison.Ordinal);
        Assert.Contains(
            "\"${AGAR_OPERATIONS_BIND_HOST:-127.0.0.1}:${AGAR_OPERATIONS_PORT:-21000}:21000\"",
            data,
            StringComparison.Ordinal);

        var gateway = ExtractComposeService(compose, "gateway-1");
        Assert.Contains("Lakona__Node__Id: gateway-1", gateway, StringComparison.Ordinal);
        Assert.Contains("Lakona__ActorHosts: '[]'", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__StartupActors", gateway, StringComparison.Ordinal);
        Assert.Contains("Lakona__Endpoints: >-", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Transport\": \"websocket\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Serializer\": \"memorypack\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Host\": \"0.0.0.0\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"AdvertisedHost\": \"gateway-1\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Port\": 20000", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Path\": \"/ws\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"RpcServices\": [ \"login\", \"player\" ]", gateway, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.0.0.2:21002", gateway, StringComparison.Ordinal);
        Assert.Contains(
            "Lakona__Cluster__Peers: '[{\"Id\":\"data-1\",\"Endpoint\":\"tcp://10.0.0.1:21001\"},{\"Id\":\"battle-1\",\"Endpoint\":\"tcp://10.0.0.3:21003\"}]'",
            gateway,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Agar__Persistence__", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionStrings__AgarGame", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Endpoints__0__", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Cluster__Peers__", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Http", gateway, StringComparison.Ordinal);

        var battle = ExtractComposeService(compose, "battle-1");
        Assert.Contains("Lakona__Node__Id: battle-1", battle, StringComparison.Ordinal);
        Assert.Contains("Lakona__ActorHosts: '[\"room\"]'", battle, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__StartupActors", battle, StringComparison.Ordinal);
        Assert.Contains("Lakona__Endpoints: >-", battle, StringComparison.Ordinal);
        Assert.Contains("\"Transport\": \"kcp\"", battle, StringComparison.Ordinal);
        Assert.Contains("\"Serializer\": \"memorypack\"", battle, StringComparison.Ordinal);
        Assert.Contains("\"Host\": \"0.0.0.0\"", battle, StringComparison.Ordinal);
        Assert.Contains("\"AdvertisedHost\": \"${AGAR_BATTLE_ADVERTISED_HOST:-127.0.0.1}\"", battle, StringComparison.Ordinal);
        Assert.Contains("\"Port\": 20001", battle, StringComparison.Ordinal);
        Assert.Contains("\"RpcServices\": [ \"battle\" ]", battle, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.0.0.3:21003", battle, StringComparison.Ordinal);
        Assert.Contains(
            "Lakona__Cluster__Peers: '[{\"Id\":\"gateway-1\",\"Endpoint\":\"tcp://10.0.0.2:21002\"}]'",
            battle,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Agar__Persistence__", battle, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionStrings__AgarGame", battle, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Lakona__", "Fea", "ture"), battle, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Endpoints__0__", battle, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Cluster__Peers__", battle, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Http", battle, StringComparison.Ordinal);
    }

    [Fact]
    public void AgarServerDockerImageRemovesPublishedAppsettingsFiles()
    {
        var dockerfile = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "Dockerfile"));

        Assert.Contains("RUN rm -f appsettings*.json", dockerfile, StringComparison.Ordinal);
        Assert.True(
            dockerfile.IndexOf("RUN rm -f appsettings*.json", StringComparison.Ordinal)
            < dockerfile.IndexOf("USER $APP_UID", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GatewayNodeDoesNotRegisterDatabaseServicesOrActorHosts()
    {
        var services = BuildNodeServices("gateway-1");

        await using var provider = services.BuildServiceProvider();
        var runtimeOptions = provider.GetRequiredService<Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions>();

        Assert.Empty(runtimeOptions.ActorHosts);
        Assert.Equal("ActorLocationDirectory", provider.GetRequiredService<IActorDirectory>().GetType().Name);
        Assert.NotNull(provider.GetRequiredService<IClusterMembership>());
    }

    [Fact]
    public async Task BattleNodeDoesNotRegisterDatabaseServices()
    {
        var services = BuildNodeServices("battle-1");

        await using var provider = services.BuildServiceProvider();
        var runtimeOptions = provider.GetRequiredService<Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions>();

        Assert.Equal(["room"], runtimeOptions.ActorHosts);
        Assert.Equal("ActorLocationDirectory", provider.GetRequiredService<IActorDirectory>().GetType().Name);
        Assert.NotNull(provider.GetRequiredService<IClusterMembership>());
    }

    [Fact]
    public async Task BattleNodeRegistersRuntimeServicesWithoutControlPlaneServices()
    {
        var services = BuildNodeServices("battle-1");

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IActorRuntime>();
        provider.GetRequiredService<IGameSessionRegistry>();
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService(RequiredServerAppType("Server.App.Hotfix.Agar" + "Hotfix" + "Runtime" + "Events")));
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService(RequiredServerAppType("Server.App.Realtime.Room" + "Runtime" + "Host")));

        Assert.DoesNotContain(provider.GetServices<IRpcSessionLifecycleObserver>(),
            observer => string.Equals(
                observer.GetType().FullName,
                "Server.App.Hosting.PlayerSessionLifecycleObserver",
                StringComparison.Ordinal));
        Assert.NotNull(provider.GetRequiredService(
            RequiredServerAppType("Server.Hotfix.Matchmaking.MatchmakingNotifier")));
    }

    [Fact]
    public void ClusterEndpointRequiresMembership()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions
        {
            Cluster = new Lakona.Game.Server.Configuration.LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Peers = []
            }
        });

        var exception = Assert.Throws<InvalidOperationException>(services.AddLakonaGameClusterEndpoint);
        Assert.Contains("IClusterMembership", exception.Message, StringComparison.Ordinal);
    }

    private static JsonDocument Open(string fileName)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "App",
            fileName);

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static Type RequiredServerAppType(string typeName)
    {
        return typeof(PlayerService).Assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"Could not find server type '{typeName}'.");
    }

    private static async ValueTask<UserLoginResult> LoginAndAttachAsync(
        IServiceProvider provider,
        string playerId,
        string connectionId,
        string controlSessionId = "")
    {
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorIdentity.Create<UserActor, UserId>(new UserId(playerId)));
        return await actors.AskAsync<UserActor, UserLoginResult>(
            ActorIdentity.Create<UserActor, UserId>(new UserId(playerId)),
            (actor, _) => actor.LoginAndAttachAsync(new UserLoginAndAttachRequest
            {
                Password = "pw",
                ConnectionId = connectionId,
                ControlSessionId = controlSessionId
            }));
    }

    private static async ValueTask<MatchmakingEnqueueResult> EnqueueAsync(IServiceProvider provider, MatchmakingEnqueueRequest request)
    {
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<MatchmakingActor>(ActorId.From("default"));
        return await actors.AskAsync<MatchmakingActor, MatchmakingEnqueueResult>(
            ActorId.From("default"),
            (actor, _) => actor.EnqueueAsync(request));
    }

    private static async ValueTask<MatchmakingStatusSnapshot> GetMatchmakingStatusAsync(IServiceProvider provider)
    {
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<MatchmakingActor>(ActorId.From("default"));
        return await actors.AskAsync<MatchmakingActor, MatchmakingStatusSnapshot>(
            ActorId.From("default"),
            (actor, _) => actor.GetStatusAsync(new MatchmakingStatusRequest()));
    }

    private static async ValueTask<PlayerSessionSnapshot> GetSessionSnapshotAsync(IServiceProvider provider, string playerId)
    {
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(UserId(playerId));
        return await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            UserId(playerId),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()));
    }

    private static async Task ReleasePlayerThroughInternalBoundaryAsync(IServiceProvider provider, string playerId, string reason)
    {
        var method = typeof(PlayerService).GetMethod(
            "ReleasePlayerAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find PlayerService.ReleasePlayerAsync.");
        var task = method.Invoke(null, [
            provider.GetRequiredService<ActorAccess>(),
            provider.GetService<MatchmakingNotifier>() ??
                ActivatorUtilities.CreateInstance<MatchmakingNotifier>(provider),
            new LocalActorNodeIdentity(new NodeId("gateway-1")),
            provider.GetRequiredService<ILogger<PlayerService>>(),
            playerId,
            reason,
            TestContext.Current.CancellationToken
        ]) as Task
            ?? throw new InvalidOperationException("PlayerService.ReleasePlayerAsync did not return a Task.");

        await task.ConfigureAwait(false);
    }

    private static ActorId UserId(string userId) => ActorIdentity.Create<UserActor, UserId>(new UserId(userId));

    private static IServiceCollection BuildNodeServices(
        string nodeName,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var configuration = BuildNodeEnvironmentConfiguration(nodeName, overrides);
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLakonaGameServer(configuration);
        services.AddGeneratedActorSelectorTestDependencies();

        return services;
    }

    private static IServiceCollection BuildProgramServices(
        string nodeName,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var configuration = nodeName.EndsWith(".json", StringComparison.Ordinal)
            ? BuildAppConfiguration(nodeName, overrides)
            : BuildNodeEnvironmentConfiguration(nodeName, overrides);
        var runtimeOptions = Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions.FromConfiguration(configuration);
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLakonaGameServer(configuration);
        services.AddGeneratedActorSelectorTestDependencies();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(runtimeOptions);
        services.AddSingleton(runtimeOptions.ToClusterOptions(configuration));
        services.AddLakonaGameRuntimeValidation();

        return services;
    }

    private static IConfigurationRoot BuildAppConfiguration(
        string fileName,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(FindRepositoryRoot(), "samples", "Game.Unity.Agar", "Server", "App"))
            .AddJsonFile(fileName);

        if (overrides is not null)
        {
            configurationBuilder.AddInMemoryCollection(overrides);
        }

        return configurationBuilder.Build();
    }

    private static IConfigurationRoot BuildNodeEnvironmentConfiguration(
        string nodeName,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ENVIRONMENT"] = nodeName
        };

        switch (nodeName)
        {
            case "data-1":
                values["Agar:Persistence:Postgres:ConnectionStringName"] = "AgarGamePostgres";
                values["Agar:Persistence:Redis:ConnectionStringName"] = "AgarGameRedis";
                values["ConnectionStrings:AgarGamePostgres"] =
                    "Host=postgres;Port=5432;Database=lakona-game;Username=lakona-game;Password=lakona-game_dev_password";
                values["ConnectionStrings:AgarGameRedis"] =
                    "redis:6379,password=lakona-game_redis_dev_password";
                values["Lakona:Node:Id"] = "data-1";
                values["Lakona:ActorHosts"] = """["user","matchmaking","leaderboard"]""";
                values["Lakona:Cluster:Endpoint"] = "tcp://10.0.0.1:21001";
                values["Lakona:Cluster:Peers"] =
                    """[{"Id":"gateway-1","Endpoint":"tcp://10.0.0.2:21002"}]""";
                break;
            case "gateway-1":
                values["Lakona:Node:Id"] = "gateway-1";
                values["Lakona:ActorHosts"] = "[]";
                values["Lakona:Endpoints"] =
                    """
                    [
                      {
                        "Transport": "websocket",
                        "Serializer": "memorypack",
                        "Host": "0.0.0.0",
                        "AdvertisedHost": "gateway-1",
                        "Port": 20000,
                        "Path": "/ws",
                        "RpcServices": [ "login", "player" ]
                      }
                    ]
                    """;
                values["Lakona:Cluster:Endpoint"] = "tcp://10.0.0.2:21002";
                values["Lakona:Cluster:Peers"] =
                    """[{"Id":"data-1","Endpoint":"tcp://10.0.0.1:21001"},{"Id":"battle-1","Endpoint":"tcp://10.0.0.3:21003"}]""";
                break;
            case "battle-1":
                values["Lakona:Node:Id"] = "battle-1";
                values["Lakona:ActorHosts"] = """["room"]""";
                values["Lakona:Endpoints"] =
                    """
                    [
                      {
                        "Transport": "kcp",
                        "Serializer": "memorypack",
                        "Host": "0.0.0.0",
                        "AdvertisedHost": "battle-1",
                        "Port": 20001,
                        "RpcServices": [ "battle" ]
                      }
                    ]
                    """;
                values["Lakona:Cluster:Endpoint"] = "tcp://10.0.0.3:21003";
                values["Lakona:Cluster:Peers"] =
                    """[{"Id":"gateway-1","Endpoint":"tcp://10.0.0.2:21002"}]""";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(nodeName), nodeName, "Unknown Agar node.");
        }

        if (overrides is not null)
        {
            foreach (var item in overrides)
            {
                values[item.Key] = item.Value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }

    private static string ExtractComposeService(string compose, string serviceName)
    {
        var marker = $"  {serviceName}:";
        var start = compose.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Could not find compose service '{serviceName}'.");
        }

        var next = compose.IndexOf("\n  ", start + marker.Length, StringComparison.Ordinal);
        while (next >= 0)
        {
            var lineEnd = compose.IndexOf('\n', next + 1);
            var line = lineEnd >= 0
                ? compose.Substring(next + 1, lineEnd - next - 1)
                : compose[(next + 1)..];
            if (!line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith("    ", StringComparison.Ordinal))
            {
                next = compose.IndexOf("\n  ", next + 1, StringComparison.Ordinal);
                continue;
            }

            break;
        }

        return next < 0
            ? compose[start..]
            : compose[start..next];
    }

    private static string ReadAllTextFiles(string root)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(IsTextSourceFile)
                     .Order(StringComparer.Ordinal))
        {
            builder.AppendLine(File.ReadAllText(path));
        }

        return builder.ToString();
    }

    private static bool IsTextSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".csproj" or ".json" or ".slnx" or ".props" or ".xml" or ".txt";
    }

}
