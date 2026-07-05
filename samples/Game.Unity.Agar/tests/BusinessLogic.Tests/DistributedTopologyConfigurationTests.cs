using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Diagnostics;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Tcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Agar.Sample.State;
using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Leaderboard;
using Agar.Sample.State.Matchmaking;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Users;
using Server.Hotfix.Features;
using Server.Hotfix.Services;
using Server.Hotfix.State.Matchmaking;
using Server.Hotfix.State.Users;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class DistributedTopologyConfigurationTests
{
    [Fact]
    public void DataNodeOwnsStateAndClusterEndpointWithoutClientEndpoints()
    {
        var configuration = BuildNodeEnvironmentConfiguration("data-1");
        var options = Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("data-1", options.Node.Id);
        Assert.Equal(new[] { "state-store", "matchmaking", "leaderboard" }, options.Feature);
        Assert.Empty(options.Endpoints);
        Assert.Equal("tcp://10.0.0.1:21001", options.Cluster!.Endpoint);
        Assert.Equal("memorypack", options.Cluster.Serializer);
        Assert.Equal("postgres", options.Cluster.Directory.Provider);
        Assert.Equal("LakonaClusterPostgres", options.Cluster.Directory.ConnectionStringName);
        Assert.Equal("postgres", configuration["Agar:Persistence:Provider"]);
        Assert.Equal("AgarGamePostgres", configuration["Agar:Persistence:ConnectionStringName"]);
    }

    [Fact]
    public void GatewayNodeOwnsOnlyWebSocketClientEndpoint()
    {
        var configuration = BuildNodeEnvironmentConfiguration("gateway-1");
        var options = Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("gateway-1", options.Node.Id);
        Assert.Empty(options.Feature!);
        Assert.Equal("memorypack", options.Cluster!.Serializer);

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
        Assert.Equal(new[] { "battle-runtime" }, options.Feature);
        Assert.Equal("memorypack", options.Cluster!.Serializer);

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
        var endpoints = lakona.GetProperty("Endpoints").EnumerateArray().ToArray();

        var control = endpoints.Single(endpoint =>
            string.Equals(endpoint.GetProperty("Transport").GetString(), "websocket", StringComparison.Ordinal));
        var battle = endpoints.Single(endpoint =>
            string.Equals(endpoint.GetProperty("Transport").GetString(), "kcp", StringComparison.Ordinal));

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
            "LakonaGameServer.cs"));

        Assert.Contains("new HostApplicationBuilderSettings", source, StringComparison.Ordinal);
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

    [Fact]
    public async Task MatchmakingAllocatesExpiredPartialBatchOnRemoteBattleRuntime()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var roomAllocator = new CapturingBattleRuntimeFeatureCommands();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
        var matchmakingNotifierType = typeof(PlayerService).Assembly.GetType("Server.Hotfix.Services.MatchmakingNotifier", throwOnError: true)!;
        services.AddSingleton(matchmakingNotifierType);
        services.AddSingleton(new LocalActorNodeIdentity(new NodeId("gateway-1")));
        services.AddSingleton(new Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions
        {
            Node = new Lakona.Game.Server.Configuration.LakonaGameNodeOptions { Id = "gateway-1" },
            Feature = [],
            Endpoints =
            [
                new Lakona.Game.Server.Configuration.LakonaGameEndpointOptions
                {
                    Transport = "kcp",
                    Serializer = "memorypack",
                    Host = "127.0.0.1",
                    Port = 20001,
                    RpcServices = ["battle"]
                }
            ]
        });
        services.AddSingleton<IClusterNodeDiscovery>(new FixedClusterNodeDiscovery(
        [
            new ClusterNodeDescriptor(
                new NodeId("battle-1"),
                NodeState.Ready,
                new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
                {
                    ["cluster"] = new NodeEndpoint("tcp://battle-1:21003"),
                    ["kcp"] = new NodeEndpoint("kcp://battle-1:20001")
                },
                [new NodeFeatureDescriptor("battle-runtime")])
        ]));
        services.AddSingleton<IFeatureCommandClient>(roomAllocator);

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var discoveredBattleNodes = await provider
            .GetRequiredService<IClusterNodeDiscovery>()
            .ListAsync(new FeatureName("battle-runtime"), TestContext.Current.CancellationToken);
        Assert.Single(discoveredBattleNodes);

        var login = await LoginAsync(provider, "remote-battle-player");

        await AttachSessionAsync(provider, new PlayerSessionAttachRequest
        {
            UserId = login.UserId,
            SessionToken = login.SessionToken,
            ConnectionId = "control-remote-battle",
            AttachedAtUtc = DateTime.UtcNow,
            ControlGateway = new Agar.Sample.State.Contracts.GatewayEndpointDescriptor
            {
                InstanceId = "gateway-1",
                Transport = "websocket",
                Host = "gateway-1",
                Port = 20000,
                Path = "/ws"
            }
        });

        var result = await EnqueueAsync(provider, new MatchmakingEnqueueRequest
        {
            UserId = login.UserId,
            SessionToken = login.SessionToken,
            EnqueuedAtUtc = DateTime.UtcNow.AddSeconds(-6)
        });

        Assert.NotNull(result);
        Assert.False(result.Matched);
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
            TestContext.Current.CancellationToken);

        Assert.NotNull(roomAllocator.LastRequest);
        Assert.Equal("battle-runtime", roomAllocator.LastFeatureName);
        Assert.Equal(10, roomAllocator.LastRequest.MaxPlayers);
        Assert.Single(roomAllocator.LastRequest.Players);
        Assert.Equal(login.UserId, roomAllocator.LastRequest.Players[0].UserId);

        var status = await GetMatchmakingStatusAsync(provider);
        Assert.Equal(0, status.QueuedCount);

        var session = await GetSessionSnapshotAsync(provider, login.UserId);
        Assert.Equal(roomAllocator.LastRequest.RoomId, session.CurrentRoomId);
        Assert.Equal(roomAllocator.LastRequest.Players[0].MatchId, session.CurrentMatchId);
        Assert.Equal("battle-1", session.RuntimeGateway.InstanceId);
        Assert.Equal("kcp", session.RuntimeGateway.Transport);
        Assert.Equal("battle-1", session.RuntimeGateway.Host);
        Assert.Equal(20001, session.RuntimeGateway.Port);
    }

    [Fact]
    public async Task MatchmakingKeepsTicketsQueuedWhenBattleRuntimeEndpointIsUnavailable()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();

        MatchmakingEnqueueResult? result = null;
        var playerIds = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var playerId = $"no-runtime-player-{i}";
            playerIds.Add(playerId);
            var login = await LoginAsync(provider, playerId);

            await AttachSessionAsync(provider, new PlayerSessionAttachRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                ConnectionId = $"control-{i}",
                AttachedAtUtc = DateTime.UtcNow,
                ControlGateway = new Agar.Sample.State.Contracts.GatewayEndpointDescriptor
                {
                    InstanceId = "gateway-1",
                    Transport = "websocket",
                    Host = "gateway-1",
                    Port = 20000,
                    Path = "/ws"
                }
            });

            result = await EnqueueAsync(provider, new MatchmakingEnqueueRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                EnqueuedAtUtc = DateTime.UtcNow
            });
        }

        Assert.NotNull(result);
        Assert.False(result.Matched);
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
    }

    [Fact]
    public async Task MatchmakingKeepsTicketsQueuedForLocalKcpEndpointWithoutBattleRuntimeDiscovery()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var services = BuildProgramServices("appsettings.json");

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();

        MatchmakingEnqueueResult? result = null;
        for (var i = 0; i < 10; i++)
        {
            var playerId = $"local-runtime-player-{i}";
            var login = await LoginAsync(provider, playerId);

            await AttachSessionAsync(provider, new PlayerSessionAttachRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                ConnectionId = $"control-{i}",
                AttachedAtUtc = DateTime.UtcNow,
                ControlGateway = new Agar.Sample.State.Contracts.GatewayEndpointDescriptor
                {
                    InstanceId = "gateway-1",
                    Transport = "websocket",
                    Host = "gateway-1",
                    Port = 20000,
                    Path = "/ws"
                }
            });

            result = await EnqueueAsync(provider, new MatchmakingEnqueueRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                EnqueuedAtUtc = DateTime.UtcNow
            });
        }

        Assert.NotNull(result);
        Assert.False(result.Matched);
        Assert.True(result.Queued);

        var status = await GetMatchmakingStatusAsync(provider);
        Assert.Equal(10, status.QueuedCount);

        for (var i = 0; i < 10; i++)
        {
            var snapshot = await GetSessionSnapshotAsync(provider, $"local-runtime-player-{i}");
            Assert.True(string.IsNullOrWhiteSpace(snapshot.CurrentRoomId));
            Assert.True(string.IsNullOrWhiteSpace(snapshot.CurrentMatchId));
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

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From("player-stale"), cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-stale"),
            (actor, _) => actor.AttachAsync(new PlayerSessionAttachRequest
            {
                UserId = "player-stale",
                SessionToken = "token-stale",
                ConnectionId = "control-stale",
                ControlSessionId = "control-session-stale",
                ControlSessionGeneration = 1,
                AttachedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
        await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            ActorId.From("player-stale"),
            (actor, _) => actor.AssignRoomAsync(new PlayerRoomAssignment
            {
                UserId = "player-stale",
                SessionToken = "token-stale",
                ConnectionId = "control-stale",
                RoomId = "stale-room",
                MatchId = "stale-match",
                SeatIndex = 0,
                AssignedAtUtc = DateTime.UtcNow,
                RuntimeGateway = new Agar.Sample.State.Contracts.GatewayEndpointDescriptor
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
        Assert.False(snapshot.IsOnline);
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
        services.AddMessageRecording();
        services.AddLakonaGameRuntimeValidation();
        services.AddLakonaGame(configuration, _ => { });
        services.AddLakonaGameServer(configuration);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ActorRuntimeOptions>();

        Assert.Equal(TimeSpan.FromSeconds(5), options.CallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), options.SlowMessageThreshold);
    }

    [Fact]
    public void DataNodeRegistersSqlNodeDirectoryFromFrameworkConfig()
    {
        var services = BuildFeatureServices("data-1");

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(SqlNodeDirectoryOptions));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(INodeDirectory)
            && descriptor.ImplementationType == typeof(SqlNodeDirectory));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRouteDirectory)
            && descriptor.ImplementationType == typeof(InMemoryRouteDirectory));

        using var provider = services.BuildServiceProvider();
        var sqlOptions = provider.GetRequiredService<SqlNodeDirectoryOptions>();
        var runtimeOptions = provider.GetRequiredService<Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions>();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();
        var routeDirectory = provider.GetRequiredService<IRouteDirectory>();

        Assert.Equal("lakona_cluster_nodes", sqlOptions.TableName);
        Assert.False(runtimeOptions.Cluster!.Directory.EnsureSchemaOnStartup);
        Assert.Equal(SqlNodeDirectoryDialect.Postgres, sqlOptions.Dialect);
        Assert.Empty(catalog.ActiveNames);
        Assert.IsType<InMemoryRouteDirectory>(routeDirectory);
        Assert.IsNotType<SeededRouteDirectoryClient>(routeDirectory);
    }

    [Fact]
    public void DataNodeCanEnableClusterDirectorySchemaCreationExplicitly()
    {
        var services = BuildFeatureServices(
            "data-1",
            new Dictionary<string, string?>
            {
                ["Lakona:Cluster:Directory:EnsureSchemaOnStartup"] = "true"
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions>();

        Assert.True(options.Cluster!.Directory.EnsureSchemaOnStartup);
    }

    [Fact]
    public void AgarPostgresInitIncludesLakonaClusterNodeDirectorySchema()
    {
        var root = FindRepositoryRoot();
        var frameworkScript = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Lakona.Game.Cluster.Sql",
            "schema",
            "postgres",
            "001-lakona-cluster-nodes.sql"));
        var sampleScript = File.ReadAllText(Path.Combine(
            root,
            "samples",
            "Game.Unity.Agar",
            "infra",
            "postgres",
            "init",
            "001-lakona-cluster-nodes.sql"));

        Assert.Contains(NormalizeSql(SqlNodeDirectorySchema.CreateTableSql(SqlNodeDirectoryDialect.Postgres)), NormalizeSql(frameworkScript), StringComparison.Ordinal);
        Assert.Contains(NormalizeSql(SqlNodeDirectorySchema.CreateTableSql(SqlNodeDirectoryDialect.Postgres)), NormalizeSql(sampleScript), StringComparison.Ordinal);
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
        Assert.Contains("Lakona__Feature: '[\"state-store\",\"matchmaking\",\"leaderboard\"]'", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.0.0.1:21001", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Serializer: memorypack", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Seeds: '[\"tcp://10.0.0.1:21001\"]'", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Directory__Provider: postgres", data, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Directory__ConnectionStringName: LakonaClusterPostgres", data, StringComparison.Ordinal);
        Assert.Contains("Agar__Persistence__Provider: postgres", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Feature__", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Endpoints__", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Cluster__Seeds__", data, StringComparison.Ordinal);

        var gateway = ExtractComposeService(compose, "gateway-1");
        Assert.Contains("Lakona__Node__Id: gateway-1", gateway, StringComparison.Ordinal);
        Assert.Contains("Lakona__Feature: '[]'", gateway, StringComparison.Ordinal);
        Assert.Contains("Lakona__Endpoints: >-", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Transport\": \"websocket\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Serializer\": \"memorypack\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Host\": \"0.0.0.0\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"AdvertisedHost\": \"gateway-1\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Port\": 20000", gateway, StringComparison.Ordinal);
        Assert.Contains("\"Path\": \"/ws\"", gateway, StringComparison.Ordinal);
        Assert.Contains("\"RpcServices\": [ \"login\", \"player\" ]", gateway, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.0.0.2:21002", gateway, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Seeds: '[\"tcp://10.0.0.1:21001\"]'", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Endpoints__0__", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Cluster__Seeds__", gateway, StringComparison.Ordinal);

        var battle = ExtractComposeService(compose, "battle-1");
        Assert.Contains("Lakona__Node__Id: battle-1", battle, StringComparison.Ordinal);
        Assert.Contains("Lakona__Feature: '[\"battle-runtime\"]'", battle, StringComparison.Ordinal);
        Assert.Contains("Lakona__Endpoints: >-", battle, StringComparison.Ordinal);
        Assert.Contains("\"Transport\": \"kcp\"", battle, StringComparison.Ordinal);
        Assert.Contains("\"Serializer\": \"memorypack\"", battle, StringComparison.Ordinal);
        Assert.Contains("\"Host\": \"0.0.0.0\"", battle, StringComparison.Ordinal);
        Assert.Contains("\"AdvertisedHost\": \"battle-1\"", battle, StringComparison.Ordinal);
        Assert.Contains("\"Port\": 20001", battle, StringComparison.Ordinal);
        Assert.Contains("\"RpcServices\": [ \"battle\" ]", battle, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.0.0.3:21003", battle, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Seeds: '[\"tcp://10.0.0.1:21001\"]'", battle, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Feature__", battle, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Endpoints__0__", battle, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Cluster__Seeds__", battle, StringComparison.Ordinal);
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
    public async Task GatewayNodeDoesNotRegisterDatabaseServicesOrApplicationFeatures()
    {
        var services = BuildFeatureServices("gateway-1");

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Empty(catalog.ActiveNames);
        Assert.IsAssignableFrom<INodeDirectory>(provider.GetRequiredService<INodeDirectory>());
        Assert.IsAssignableFrom<IRouteDirectory>(provider.GetRequiredService<IRouteDirectory>());
        Assert.IsType<SeededNodeDirectoryClient>(provider.GetRequiredService<INodeDirectory>());
        Assert.IsType<SeededRouteDirectoryClient>(provider.GetRequiredService<IRouteDirectory>());
    }

    [Fact]
    public async Task BattleNodeDoesNotRegisterDatabaseServices()
    {
        var services = BuildFeatureServices("battle-1");

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Empty(catalog.ActiveNames);
        Assert.IsType<SeededNodeDirectoryClient>(provider.GetRequiredService<INodeDirectory>());
        Assert.IsType<SeededRouteDirectoryClient>(provider.GetRequiredService<IRouteDirectory>());
    }

    [Fact]
    public async Task BattleNodeRegistersRuntimeServicesWithoutControlPlaneServices()
    {
        var services = BuildFeatureServices("battle-1");

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
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService(RequiredServerAppType("Server.Hotfix.Services.MatchmakingNotifier")));
    }

    [Fact]
    public async Task GatewayRegistrationAndBattleLookupUseDataNodeLocalRouteDirectory()
    {
        var port = GetFreePort();
        var dataRoutes = new InMemoryRouteDirectory();
        using var stopServer = new CancellationTokenSource();
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new TcpConnectionAcceptor(port, "127.0.0.1"));
        RouteDirectoryBinder.Bind(builder.ServiceRegistry, dataRoutes);
        var serverTask = builder.RunAsync(stopServer.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        await using var clientFactory = new ClusterClientFactory(
            new TcpClusterTransportFactory(),
            new JsonRpcSerializer());
        var seed = $"tcp://127.0.0.1:{port}";
        var gatewayRoutes = new SeededRouteDirectoryClient(clientFactory, seed);
        var battleRoutes = new SeededRouteDirectoryClient(clientFactory, seed);
        var session = new GameSessionKey("player-1", "session-a", 3);
        var registrar = new ClientSessionRouteRegistrar(
            gatewayRoutes,
            new NodeId("gateway-1"),
            new NodeEndpoint("tcp://127.0.0.1:21002"));

        await registrar.RegisterAsync(session, TestContext.Current.CancellationToken);
        var resolvedByData = await dataRoutes.ResolveAsync(
            ClientNotificationRouteKey.FromSession(session),
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);
        var resolvedByBattle = await battleRoutes.ResolveAsync(
            ClientNotificationRouteKey.FromSession(session),
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        stopServer.Cancel();
        await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.NotNull(resolvedByData);
        Assert.NotNull(resolvedByBattle);
        Assert.Equal(new NodeId("gateway-1"), resolvedByData.Node);
        Assert.Equal(resolvedByData.Route, resolvedByBattle.Route);
        Assert.Equal(resolvedByData.Node, resolvedByBattle.Node);
        Assert.Equal(resolvedByData.Endpoint.Address, resolvedByBattle.Endpoint.Address);
        Assert.Empty(resolvedByBattle.Endpoint.Metadata);
    }

    [Fact]
    public void ClusterEndpointDoesNotRegisterSeededDirectoriesForSelfSeed()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions
        {
            Cluster = new Lakona.Game.Server.Configuration.LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Serializer = "json",
                Seeds = ["tcp://127.0.0.1:21001"]
            }
        });

        services.AddLakonaGameClusterEndpoint();

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(INodeDirectory)
            && descriptor.ImplementationFactory is not null);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IRouteDirectory)
            && descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void RemoteNotificationExampleUsesClusterDispatcher()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "tests",
            "BusinessLogic.Tests",
            "RemoteNotificationRelayExampleTests.cs");
        var source = File.ReadAllText(path);

        Assert.Contains(nameof(ClusterClientNotificationDispatcher), source, StringComparison.Ordinal);
        Assert.DoesNotContain("GatewayProcess" + "NotificationDispatcher", source, StringComparison.Ordinal);
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

    private static async ValueTask<UserLoginResult> LoginAsync(IServiceProvider provider, string playerId)
    {
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(ActorId.From(playerId));
        return await actors.AskAsync<UserActor, UserLoginResult>(
            ActorId.From(playerId),
            (actor, _) => actor.LoginAsync(new UserLoginRequest { Password = "pw", Reconnect = false }));
    }

    private static async ValueTask<PlayerSessionSnapshot> AttachSessionAsync(IServiceProvider provider, PlayerSessionAttachRequest request)
    {
        var actors = provider.GetRequiredService<IActorRuntime>();
        await provider.GetRequiredService<ActorHosting>().EnsureAsync<UserActor>(UserId(request.UserId));
        return await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            UserId(request.UserId),
            (actor, _) => actor.AttachAsync(request));
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
            provider.GetRequiredService<UserActors>(),
            provider.GetRequiredService<RoomActors>(),
            provider.GetRequiredService<MatchmakingActors>(),
            provider.GetService<MatchmakingNotifier>() ??
                ActivatorUtilities.CreateInstance<MatchmakingNotifier>(provider),
            new LocalActorNodeIdentity(new NodeId("gateway-1")),
            provider.GetRequiredService<ILogger<PlayerService>>(),
            playerId,
            reason
        ]) as Task
            ?? throw new InvalidOperationException("PlayerService.ReleasePlayerAsync did not return a Task.");

        await task.ConfigureAwait(false);
    }

    private static ActorId UserId(string userId) => ActorId.From(userId);

    private static IServiceCollection BuildFeatureServices(
        string nodeName,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var configuration = BuildNodeEnvironmentConfiguration(nodeName, overrides);
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLakonaGameServer(configuration);
        services.AddGeneratedActorSelectorTestDependencies();
        services.AddLakonaGame(configuration, _ => { });

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
        services.AddMessageRecording();
        services.AddLakonaGameRuntimeValidation();
        services.AddLakonaGame(configuration, _ => { });

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
                values["Lakona:Node:Id"] = "data-1";
                values["Lakona:Feature"] = """["state-store","matchmaking","leaderboard"]""";
                values["Lakona:Cluster:Endpoint"] = "tcp://10.0.0.1:21001";
                values["Lakona:Cluster:Serializer"] = "memorypack";
                values["Lakona:Cluster:Seeds"] = """["tcp://10.0.0.1:21001"]""";
                values["Lakona:Cluster:Directory:Provider"] = "postgres";
                values["Lakona:Cluster:Directory:ConnectionStringName"] = "LakonaClusterPostgres";
                values["Lakona:Cluster:Directory:NodeTable"] = "lakona_cluster_nodes";
                values["Lakona:Cluster:Directory:EnsureSchemaOnStartup"] = "false";
                values["Agar:Persistence:Provider"] = "postgres";
                values["Agar:Persistence:ConnectionStringName"] = "AgarGamePostgres";
                values["ConnectionStrings:LakonaClusterPostgres"] =
                    "Host=postgres;Port=5432;Database=lakona-game;Username=lakona-game;Password=lakona-game_dev_password";
                values["ConnectionStrings:AgarGamePostgres"] =
                    "Host=postgres;Port=5432;Database=agar-game;Username=agar;Password=agar_dev_password";
                break;
            case "gateway-1":
                values["Lakona:Node:Id"] = "gateway-1";
                values["Lakona:Feature"] = "[]";
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
                values["Lakona:Cluster:Serializer"] = "memorypack";
                values["Lakona:Cluster:Seeds"] = """["tcp://10.0.0.1:21001"]""";
                break;
            case "battle-1":
                values["Lakona:Node:Id"] = "battle-1";
                values["Lakona:Feature"] = """["battle-runtime"]""";
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
                values["Lakona:Cluster:Serializer"] = "memorypack";
                values["Lakona:Cluster:Seeds"] = """["tcp://10.0.0.1:21001"]""";
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

    private sealed class CapturingBattleRuntimeFeatureCommands : IFeatureCommandClient
    {
        public string LastFeatureName { get; private set; } = "";

        public BattleRuntimeRoomAllocationRequest? LastRequest { get; private set; }

        public ValueTask<TReply> SendAsync<TRequest, TReply>(
            string featureName,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            return CaptureAndReply<TRequest, TReply>(featureName, request);
        }

        public ValueTask<TReply> SendToNodeAsync<TRequest, TReply>(
            ClusterNodeDescriptor target,
            string featureName,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = target;
            return CaptureAndReply<TRequest, TReply>(featureName, request);
        }

        private ValueTask<TReply> CaptureAndReply<TRequest, TReply>(string featureName, TRequest request)
        {
            LastFeatureName = featureName;
            LastRequest = request as BattleRuntimeRoomAllocationRequest;
            if (LastRequest is null)
            {
                throw new InvalidOperationException($"Unexpected request type {typeof(TRequest).FullName}.");
            }

            object reply = new BattleRuntimeRoomAllocationReply
            {
                Succeeded = true,
                RoomId = LastRequest.RoomId
            };

            return new ValueTask<TReply>((TReply)reply);
        }
    }

    private sealed class FixedClusterNodeDiscovery : IClusterNodeDiscovery
    {
        private readonly IReadOnlyList<ClusterNodeDescriptor> _nodes;

        public FixedClusterNodeDiscovery(IReadOnlyList<ClusterNodeDescriptor> nodes)
        {
            _nodes = nodes;
        }

        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default)
        {
            var matches = _nodes
                .Where(node => node.Features.Any(item =>
                    string.Equals(item.Name, feature.Value, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            return new ValueTask<IReadOnlyList<ClusterNodeDescriptor>>(matches);
        }

        public async ValueTask<ClusterNodeDescriptor?> AnyAsync(
            FeatureName feature,
            CancellationToken cancellationToken = default)
        {
            return (await ListAsync(feature, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }
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

    private static string NormalizeSql(string sql)
    {
        var normalized = string.Join(
                " ",
                sql.Replace(";", string.Empty, StringComparison.Ordinal)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        return normalized
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal);
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

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
