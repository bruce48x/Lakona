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
using Server.Hotfix.Services;
using Server.Hotfix.State.Matchmaking;
using Server.Hotfix.State.Sessions;
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
        using var document = Open("appsettings.data-1.json");
        var lakona = document.RootElement.GetProperty("Lakona");

        Assert.Equal("data-1", lakona.GetProperty("Node").GetProperty("Id").GetString());
        AssertFeatureSet(lakona, "state-store", "matchmaking", "leaderboard");
        Assert.False(lakona.TryGetProperty("Endpoints", out _));
        Assert.Equal("tcp://10.0.0.1:21001", lakona.GetProperty("Cluster").GetProperty("Endpoint").GetString());
        Assert.Equal("memorypack", lakona.GetProperty("Cluster").GetProperty("Serializer").GetString());
        Assert.True(document.RootElement.GetProperty("ConnectionStrings").TryGetProperty("LakonaClusterPostgres", out _));
        Assert.True(document.RootElement.GetProperty("ConnectionStrings").TryGetProperty("AgarGamePostgres", out _));
        Assert.True(lakona.GetProperty("Cluster").TryGetProperty("Directory", out _));
        Assert.True(document.RootElement.GetProperty("Agar").TryGetProperty("Persistence", out _));
    }

    [Fact]
    public void GatewayNodeOwnsOnlyWebSocketClientEndpoint()
    {
        using var document = Open("appsettings.gateway-1.json");
        var lakona = document.RootElement.GetProperty("Lakona");

        Assert.Equal("gateway-1", lakona.GetProperty("Node").GetProperty("Id").GetString());
        Assert.Empty(lakona.GetProperty("Feature").EnumerateArray());
        Assert.Equal("memorypack", lakona.GetProperty("Cluster").GetProperty("Serializer").GetString());

        var endpoint = Assert.Single(lakona.GetProperty("Endpoints").EnumerateArray());
        Assert.Equal("websocket", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("memorypack", endpoint.GetProperty("Serializer").GetString());
        Assert.Equal("/ws", endpoint.GetProperty("Path").GetString());
        Assert.Equal(new[] { "login", "player" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void BattleNodeOwnsRuntimeAndKcpEndpoint()
    {
        using var document = Open("appsettings.battle-1.json");
        var lakona = document.RootElement.GetProperty("Lakona");

        Assert.Equal("battle-1", lakona.GetProperty("Node").GetProperty("Id").GetString());
        AssertFeatureSet(lakona, "battle-runtime");
        Assert.Equal("memorypack", lakona.GetProperty("Cluster").GetProperty("Serializer").GetString());

        var endpoint = Assert.Single(lakona.GetProperty("Endpoints").EnumerateArray());
        Assert.Equal("kcp", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("memorypack", endpoint.GetProperty("Serializer").GetString());
        Assert.False(endpoint.TryGetProperty("Path", out _));
        Assert.Equal(new[] { "battle" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
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
        var services = BuildProgramServices("appsettings.gateway-1.json");

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
        var services = BuildProgramServices("appsettings.battle-1.json");

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
    public async Task MatchmakingKeepsTicketsQueuedWhenRemoteBattleIsAdvertisedButLocalFeatureExcludesBattleRuntime()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();
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
        services.AddSingleton<INodeDirectory>(provider =>
        {
            var directory = new InMemoryNodeDirectory();
            directory.RegisterAsync(
                new NodeRegistration(
                    "remote",
                    new NodeId("battle-1"),
                    new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
                    {
                        ["kcp"] = new NodeEndpoint("kcp://battle-1:20001")
                    },
                    [new NodeFeatureDescriptor("battle-runtime")],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    NodeState.Ready),
                DateTimeOffset.UtcNow,
                CancellationToken.None).AsTask().GetAwaiter().GetResult();
            return directory;
        });
        services.AddSingleton<IClusterNodeDiscovery, ClusterNodeDiscovery>();

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();

        MatchmakingEnqueueResult? result = null;
        for (var i = 0; i < 10; i++)
        {
            var playerId = $"player-{i}";
            var login = await LoginAsync(actors, playerId);

            await AttachSessionAsync(actors, new PlayerSessionAttachRequest
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

            result = await EnqueueAsync(actors, new MatchmakingEnqueueRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                EnqueuedAtUtc = DateTime.UtcNow
            });
        }

        Assert.NotNull(result);
        Assert.False(result.Matched);
        Assert.True(result.Queued);

        var status = await GetMatchmakingStatusAsync(actors);
        Assert.Equal(10, status.QueuedCount);
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
            var login = await LoginAsync(actors, playerId);

            await AttachSessionAsync(actors, new PlayerSessionAttachRequest
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

            result = await EnqueueAsync(actors, new MatchmakingEnqueueRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                EnqueuedAtUtc = DateTime.UtcNow
            });
        }

        Assert.NotNull(result);
        Assert.False(result.Matched);
        Assert.True(result.Queued);

        var status = await GetMatchmakingStatusAsync(actors);
        Assert.Equal(10, status.QueuedCount);
        foreach (var playerId in playerIds)
        {
            var snapshot = await GetSessionSnapshotAsync(actors, playerId);
            Assert.True(string.IsNullOrWhiteSpace(snapshot.CurrentRoomId));
            Assert.True(string.IsNullOrWhiteSpace(snapshot.CurrentMatchId));
            Assert.True(string.IsNullOrWhiteSpace(snapshot.RuntimeGateway.Host));
            Assert.Equal(0, snapshot.RuntimeGateway.Port);
        }
    }

    [Fact]
    public async Task MatchmakingUsesLocalKcpEndpointWhenConfiguredWithoutDiscovery()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var services = BuildProgramServices("appsettings.json");

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();

        MatchmakingEnqueueResult? result = null;
        for (var i = 0; i < 10; i++)
        {
            var playerId = $"local-runtime-player-{i}";
            var login = await LoginAsync(actors, playerId);

            await AttachSessionAsync(actors, new PlayerSessionAttachRequest
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

            result = await EnqueueAsync(actors, new MatchmakingEnqueueRequest
            {
                UserId = login.UserId,
                SessionToken = login.SessionToken,
                EnqueuedAtUtc = DateTime.UtcNow
            });
        }

        Assert.NotNull(result);
        Assert.True(result.Matched);
        Assert.Equal("gateway-1", result.RoomAssignment.RuntimeGateway.InstanceId);
        Assert.Equal("kcp", result.RoomAssignment.RuntimeGateway.Transport);
        Assert.Equal("127.0.0.1", result.RoomAssignment.RuntimeGateway.Host);
        Assert.Equal(20001, result.RoomAssignment.RuntimeGateway.Port);
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
        await ((IActorLifecycle)actors).CreateLocalAsync<UserActor>(ActorId.From("player-stale"), cancellationToken: cancellationToken);
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

        var snapshot = await GetSessionSnapshotAsync(actors, "player-stale");
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
        var services = BuildFeatureServices("appsettings.data-1.json");

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
            "appsettings.data-1.json",
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
    public async Task GatewayNodeDoesNotRegisterDatabaseServicesOrApplicationFeatures()
    {
        var services = BuildFeatureServices("appsettings.gateway-1.json");

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
        var services = BuildFeatureServices("appsettings.battle-1.json");

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Empty(catalog.ActiveNames);
        Assert.IsType<SeededNodeDirectoryClient>(provider.GetRequiredService<INodeDirectory>());
        Assert.IsType<SeededRouteDirectoryClient>(provider.GetRequiredService<IRouteDirectory>());
    }

    [Fact]
    public async Task BattleNodeRegistersRuntimeServicesWithoutControlPlaneServices()
    {
        var services = BuildFeatureServices("appsettings.battle-1.json");

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

    private static void AssertFeatureSet(JsonElement lakona, params string[] expected)
    {
        Assert.Equal(expected, lakona.GetProperty("Feature").EnumerateArray().Select(item => item.GetString()).ToArray());
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

    private static async ValueTask<UserLoginResult> LoginAsync(IActorRuntime actors, string playerId)
    {
        await ((IActorLifecycle)actors).CreateLocalAsync<UserActor>(ActorId.From(playerId));
        return await actors.AskAsync<UserActor, UserLoginResult>(
            ActorId.From(playerId),
            (actor, _) => actor.LoginAsync(new UserLoginRequest { Password = "pw", Reconnect = false }));
    }

    private static async ValueTask<PlayerSessionSnapshot> AttachSessionAsync(IActorRuntime actors, PlayerSessionAttachRequest request)
    {
        await ((IActorLifecycle)actors).CreateLocalAsync<UserActor>(UserId(request.UserId));
        return await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            UserId(request.UserId),
            (actor, _) => actor.AttachAsync(request));
    }

    private static async ValueTask<MatchmakingEnqueueResult> EnqueueAsync(IActorRuntime actors, MatchmakingEnqueueRequest request)
    {
        await ((IActorLifecycle)actors).CreateLocalAsync<MatchmakingActor>(ActorId.From("default"));
        return await actors.AskAsync<MatchmakingActor, MatchmakingEnqueueResult>(
            ActorId.From("default"),
            (actor, _) => actor.EnqueueAsync(request));
    }

    private static async ValueTask<MatchmakingStatusSnapshot> GetMatchmakingStatusAsync(IActorRuntime actors)
    {
        await ((IActorLifecycle)actors).CreateLocalAsync<MatchmakingActor>(ActorId.From("default"));
        return await actors.AskAsync<MatchmakingActor, MatchmakingStatusSnapshot>(
            ActorId.From("default"),
            (actor, _) => actor.GetStatusAsync(new MatchmakingStatusRequest()));
    }

    private static async ValueTask<PlayerSessionSnapshot> GetSessionSnapshotAsync(IActorRuntime actors, string playerId)
    {
        await ((IActorLifecycle)actors).CreateLocalAsync<UserActor>(UserId(playerId));
        return await actors.AskAsync<UserActor, PlayerSessionSnapshot>(
            UserId(playerId),
            (actor, _) => actor.GetSnapshotAsync(new PlayerSessionSnapshotRequest()));
    }

    private static async Task ReleasePlayerThroughInternalBoundaryAsync(IServiceProvider provider, string playerId, string reason)
    {
        var hotfixAssembly = typeof(PlayerService).Assembly;
        var dependenciesType = hotfixAssembly.GetType("Server.Hotfix.Services.AgarServiceDependencies")
            ?? throw new InvalidOperationException("Could not find Agar service dependency container.");
        var dependencies = Activator.CreateInstance(
            dependenciesType,
            provider.GetRequiredService<UserActors>(),
            provider.GetRequiredService<RoomActors>(),
            provider.GetRequiredService<MatchmakingActors>(),
            provider.GetRequiredService<LeaderboardActors>(),
            null,
            new LocalActorNodeIdentity(new NodeId("gateway-1")),
            provider.GetRequiredService<ILoggerFactory>())
            ?? throw new InvalidOperationException("Could not create Agar service dependency container.");
        var method = typeof(PlayerService).GetMethod(
            "ReleasePlayerAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find PlayerService.ReleasePlayerAsync.");
        var task = method.Invoke(null, [dependencies, playerId, reason]) as Task
            ?? throw new InvalidOperationException("PlayerService.ReleasePlayerAsync did not return a Task.");

        await task.ConfigureAwait(false);
    }

    private static ActorId UserId(string userId) => ActorId.From(userId);

    private static IServiceCollection BuildFeatureServices(
        string fileName,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var configuration = BuildAppConfiguration(fileName, overrides);
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLakonaGameServer(configuration);
        services.AddGeneratedActorSelectorTestDependencies();
        services.AddLakonaGame(configuration, _ => { });

        return services;
    }

    private static IServiceCollection BuildProgramServices(
        string fileName,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var configuration = BuildAppConfiguration(fileName, overrides);
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
