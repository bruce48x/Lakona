using System.Reflection;
using Server.App.State.Contracts;
using Server.App.State.Contracts.Leaderboard;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Contracts.Rooms;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Server.App.State.Matchmaking;
using Server.App.State.Rooms;
using Server.App.State.Users;
using Server.App.State.Leaderboard;
using Lakona.Game.Abstractions;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Shared.Gameplay;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Server.App.Generated;
using Server.Hotfix.Services;
using Server.Hotfix.State.Leaderboard;
using Server.Hotfix.State.Matchmaking;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;
using Server.Hotfix.Timers;
using Shared.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarHotfixTests
{
    [Fact]
    public async Task StartingMatchmakingTimerWithoutHotfixScopeFailsFast()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        services.AddGeneratedActorSelectorTestDependencies();

        await using var provider = services.BuildServiceProvider();
        var actorId = ActorId.From("missing-timer-scope");
        await provider.GetRequiredService<ActorHosting>()
            .EnsureAsync<MatchmakingActor>(actorId, cancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<IActorRuntime>()
                .TellAsync<MatchmakingActor>(
                    actorId,
                    (actor, _) => actor.StartTimerAsync(new MatchmakingTimerStartRequest(), cancellationToken),
                    cancellationToken)
                .AsTask());

        Assert.Equal(
            "Lakona timers can only be used inside an active hotfix execution scope.",
            exception.Message);
    }

    [Fact]
    public void AgarHotfixTimers_DoNotRegisterRemovedMessageHandlers()
    {
        var root = FindRepositoryRoot();
        var hotfixFiles = Directory.GetFiles(
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Hotfix"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in hotfixFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(string.Concat("I", "Fea", "ture", "MessageHandler"), text, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Fea", "ture", "MessageReply"), text, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Concat("Fea", "ture", "MessageRequest"), text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Hotfix_reload_succeeds_without_cluster_remote_actor_services()
    {
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var source = new CurrentDirectoryHotfixAssemblySource(
            Path.GetDirectoryName(hotfixAssemblyPath)!,
            Path.GetFileName(hotfixAssemblyPath));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        new global::GeneratedHotfixActorRegistration().Register(services);
        services.AddGeneratedActorSelectorTestDependencies();
        await using var rootServices = services.BuildServiceProvider();
        var manager = new HotfixManager(source, HotfixHostAssemblyNames(), rootServices: rootServices);

        var reload = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(reload.Succeeded, BuildReloadDiagnostics(reload));
    }

    [Fact]
    public async Task Guest_login_creates_user_actor_on_hashed_state_store_node()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var stateStoreNodes = new[]
        {
            StateStoreNode("state-b", "tcp://127.0.0.1:22002"),
            StateStoreNode("state-a", "tcp://127.0.0.1:22001")
        };
        var remoteSerializer = new JsonRemoteActorSerializer();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        new global::GeneratedHotfixActorRegistration().Register(services);
        services.AddGeneratedActorSelectorTestDependencies();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "gateway-1" },
            Endpoints =
            [
                new LakonaGameEndpointOptions
                {
                    Transport = "websocket",
                    Serializer = "memorypack",
                    Host = "127.0.0.1",
                    Port = 20000,
                    Path = "/ws",
                    RpcServices = ["login", "player"]
                }
            ],
            ActorHosts = []
        });
        services.AddSingleton<IClusterNodeDiscovery>(new FixedClusterNodeDiscovery(stateStoreNodes));
        services.RemoveAll<IRemoteActorSerializer>();
        services.RemoveAll<IRemoteActorInvoker>();
        services.AddSingleton<IRemoteActorSerializer>(remoteSerializer);
        services.AddSingleton<IRemoteActorInvoker>(provider => new StateStoreRemoteActorInvoker(
            remoteSerializer,
            provider.GetRequiredService<IActorDirectory>()));
        var matchmakingNotifierType = typeof(LoginService).Assembly.GetType("Server.Hotfix.Services.MatchmakingNotifier", throwOnError: true)!;
        services.AddSingleton(matchmakingNotifierType);

        await using var provider = services.BuildServiceProvider();
        var now = DateTimeOffset.UtcNow;
        await provider.GetRequiredService<INodeDirectory>().RegisterAsync(
            new NodeRegistration(
                "local",
                new NodeId("gateway-1"),
                new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
                {
                    ["cluster"] = new NodeEndpoint("tcp://127.0.0.1:21002")
                },
                [new NodeActorHostDescriptor("user", "placement:Server.App.State.Users.UserActor", "hotfix")],
                now.AddMinutes(1),
                NodeState.Ready),
            now,
            TestContext.Current.CancellationToken);
        var actors = provider.GetRequiredService<IActorRuntime>();
        var service = new LoginService(
            provider.GetRequiredService<ActorAccess>(),
            provider.GetRequiredService<ILogger<LoginService>>());
        var call = new LoginServiceCall<LoginRequest>(
            new LoginRequest { GuestLogin = true },
            "control-connection-1",
            currentSession: null,
            GameSessionItems.Empty,
            provider,
            actors,
            new TestGameServer());

        var reply = await service.LoginAsync(call);

        Assert.Equal(LoginResultCodes.Ok, reply.Code);
        Assert.StartsWith("guest-", reply.Account, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(reply.Password));
        Assert.Equal(reply.Account, reply.PlayerId);

        Assert.Equal(ActorState.Active, actors.GetState(ActorId.From(reply.PlayerId)));
    }

    [Fact]
    public async Task Leaderboard_query_uses_existing_global_leaderboard_actor()
    {
        var stateStoreNodes = new[]
        {
            StateStoreNode("state-b", "tcp://127.0.0.1:22002"),
            StateStoreNode("state-a", "tcp://127.0.0.1:22001")
        };
        var remoteSerializer = new JsonRemoteActorSerializer();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        new global::GeneratedHotfixActorRegistration().Register(services);
        services.AddGeneratedActorSelectorTestDependencies();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "gateway-1" },
            Endpoints =
            [
                new LakonaGameEndpointOptions
                {
                    Transport = "websocket",
                    Serializer = "memorypack",
                    Host = "127.0.0.1",
                    Port = 20000,
                    Path = "/ws",
                    RpcServices = ["login", "player"]
                }
            ],
            ActorHosts = []
        });
        services.AddSingleton<IClusterNodeDiscovery>(new FixedClusterNodeDiscovery(stateStoreNodes));
        services.RemoveAll<IRemoteActorSerializer>();
        services.RemoveAll<IRemoteActorInvoker>();
        services.AddSingleton<IRemoteActorSerializer>(remoteSerializer);
        services.AddSingleton<IRemoteActorInvoker>(provider => new StateStoreRemoteActorInvoker(
            remoteSerializer,
            provider.GetRequiredService<IActorDirectory>()));
        var matchmakingNotifierType = typeof(LoginService).Assembly.GetType("Server.Hotfix.Services.MatchmakingNotifier", throwOnError: true)!;
        services.AddSingleton(matchmakingNotifierType);

        await using var provider = services.BuildServiceProvider();
        var now = DateTimeOffset.UtcNow;
        await provider.GetRequiredService<INodeDirectory>().RegisterAsync(
            new NodeRegistration(
                "local",
                new NodeId("gateway-1"),
                new Dictionary<string, NodeEndpoint> { ["cluster"] = new("tcp://127.0.0.1:21001") },
                [],
                [new StartupActorDescriptor(
                    "leaderboard",
                    $"startup:v1:{typeof(LeaderboardActor).FullName}:{typeof(LeaderboardId).FullName}",
                    "hotfix")],
                now.AddMinutes(1),
                NodeState.Ready),
            now,
            TestContext.Current.CancellationToken);
        await provider.GetRequiredService<ActorHosting>()
            .EnsureAsync<LeaderboardActor>(ActorId.From("leaderboard/@startup/gateway-1"), TestContext.Current.CancellationToken);
        var actors = provider.GetRequiredService<IActorRuntime>();
        var hotfixRuntime = await TestHotfix.LoadCurrentRuntimeAsync(provider, TestContext.Current.CancellationToken);
        var gameServer = new TestGameServer();
        var call = new PlayerServiceCall<LeaderboardRequest>(
            new LeaderboardRequest { TopN = 5 },
            "control-connection-1",
            new CapturingPlayerCallback(),
            new GameSessionKey("player-1", "session-1", 1),
            GameSessionItems.Empty,
            hotfixRuntime.HotfixServices,
            actors,
            gameServer);

        var reply = await hotfixRuntime.Invoker
            .InvokeAsync<IPlayerService, PlayerServiceCall<LeaderboardRequest>, LeaderboardReply>(
                4,
                call,
                TestContext.Current.CancellationToken);

        Assert.Equal(0, reply.Code);
        Assert.Equal(ActorState.Active, actors.GetState(ActorId.From("leaderboard/@startup/gateway-1")));
    }

    [Fact]
    public async Task Battle_submit_input_routes_when_session_items_are_valid()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await CreateBattleInputContextAsync("battle-service-input-valid", cancellationToken);
        await SetBattleSessionItemsAsync(
            context.GameServer,
            context.Session,
            context.RoomId,
            "realtime-session-1",
            3,
            cancellationToken);

        var after = await InvokeBattleInputAndReadAsync(
            context,
            moveX: 0.5f,
            moveY: -0.75f,
            tick: 77,
            cancellationToken);

        Assert.Equal(0.5f, after.InputX);
        Assert.Equal(-0.75f, after.InputY);
        Assert.Equal(77, after.LastInputTick);
    }

    [Theory]
    [InlineData(BattleSessionItemsCase.Missing)]
    [InlineData(BattleSessionItemsCase.BlankRoomId)]
    [InlineData(BattleSessionItemsCase.BlankRealtimeSessionId)]
    [InlineData(BattleSessionItemsCase.WrongKindRoomId)]
    [InlineData(BattleSessionItemsCase.WrongKindRealtimeGeneration)]
    [InlineData(BattleSessionItemsCase.ZeroRealtimeGeneration)]
    public async Task Battle_submit_input_rejects_invalid_session_items(BattleSessionItemsCase sessionItemsCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await CreateBattleInputContextAsync($"battle-service-input-reject-{sessionItemsCase}", cancellationToken);
        await SetBattleSessionItemsAsync(context.GameServer, context.Session, sessionItemsCase, context.RoomId, cancellationToken);
        var before = await ReadRoomSubmittedInputAsync(context.Actors, context.RoomId, cancellationToken);

        var after = await InvokeBattleInputAndReadAsync(
            context,
            moveX: 1f,
            moveY: 0.5f,
            tick: 88,
            cancellationToken);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Hotfix_startup_registers_current_leaderboard_actor()
    {
        var actors = new ActorHostBuilder();

        Server.Hotfix.HotfixStartup.ConfigureActors(actors);

        var declaration = Assert.Single(actors.Startups, startup => startup.ActorType == typeof(LeaderboardActor));
        Assert.Equal(typeof(LeaderboardId), declaration.KeyType);
    }

    [Fact]
    public void Hotfix_behavior_sources_do_not_use_system_class_names()
    {
        var root = Path.Combine(FindRepositoryRoot(), "samples", "Game.Unity.Agar");
        var hotfixRoots = new[]
        {
            Path.Combine(root, "Server", "Hotfix")
        };

        foreach (var file in hotfixRoots.SelectMany(static path => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("System.cs", file, StringComparison.Ordinal);
            Assert.DoesNotMatch("""\bclass\s+\w*System\b""", text);
        }
    }

    [Fact]
    public void Hotfix_actor_behaviors_resolve_hotfix_local_services_from_current_hotfix_provider()
    {
        var root = Path.Combine(FindRepositoryRoot(), "samples", "Game.Unity.Agar", "Server", "Hotfix", "State");
        var behaviorFiles = Directory.GetFiles(root, "*Behavior.cs", SearchOption.AllDirectories);

        foreach (var file in behaviorFiles)
        {
            var text = File.ReadAllText(file);

            Assert.DoesNotContain("self.Context.Services.GetRequiredService<UserActors>", text, StringComparison.Ordinal);
            Assert.DoesNotContain("self.Context.Services.GetRequiredService<RoomActors>", text, StringComparison.Ordinal);
            Assert.DoesNotContain("self.Context.Services.GetRequiredService<LeaderboardActors>", text, StringComparison.Ordinal);
            Assert.DoesNotContain("self.Context.Services.GetRequiredService<MatchmakingActors>", text, StringComparison.Ordinal);
            Assert.DoesNotContain("self.Context.Services.GetService<MatchmakingNotifier>", text, StringComparison.Ordinal);
            Assert.DoesNotContain("self.Context.Services.GetService<RoomNotifier>", text, StringComparison.Ordinal);
        }
    }

    private static PlayerRoomAssignment BuildAssignment(string userId, string roomId, string matchId, int seatIndex)
    {
        return new PlayerRoomAssignment
        {
            UserId = userId,
            SessionToken = $"token-{userId}",
            ConnectionId = $"connection-{userId}",
            RoomId = roomId,
            MatchId = matchId,
            SeatIndex = seatIndex,
            AssignedAtUtc = DateTime.UtcNow
        };
    }

    private static async Task<BattleInputContext> CreateBattleInputContextAsync(
        string roomId,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        new global::GeneratedHotfixActorRegistration().Register(services);
        services.AddGeneratedActorSelectorTestDependencies();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "gateway-1" }
        });

        var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var hotfixRuntime = await TestHotfix.LoadCurrentRuntimeAsync(provider, cancellationToken);
        await CreateReadyStartedRoomAsync(provider, roomId, cancellationToken);
        var gameServer = provider.GetRequiredService<ILakonaGameServer>();
        var session = await gameServer
            .StartSessionAsync("player-1", "battle-connection-1", cancellationToken)
            .ConfigureAwait(false);
        return new BattleInputContext(provider, hotfixRuntime, actors, gameServer, session, roomId);
    }

    private static async Task CreateReadyStartedRoomAsync(
        IServiceProvider services,
        string roomId,
        CancellationToken cancellationToken)
    {
        var actors = services.GetRequiredService<IActorRuntime>();
        var hosting = services.GetRequiredService<ActorHosting>();
        await hosting.EnsureAsync<RoomActor>(ActorId.From(roomId), cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.CreateAsync(new RoomCreateRequest
            {
                RoomId = roomId,
                MatchId = "match-1",
                CreatedByUserId = "player-1",
                CreatedAtUtc = DateTime.UtcNow,
                Players = [BuildAssignment("player-1", roomId, "match-1", 0)]
            }),
            cancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.SetReadyAsync(new RoomPlayerReadyRequest
            {
                UserId = "player-1",
                RoomId = roomId,
                IsReady = true,
                RealtimeSessionId = "realtime-session-1",
                RealtimeSessionGeneration = 3,
                UpdatedAtUtc = DateTime.UtcNow
            }),
            cancellationToken);
        await services.GetRequiredService<ActorAccess>()
            .Local<RoomActor>(new RoomId(roomId))
            .CallAsync(
                RoomBehavior.Entries.StartAsync,
                new RoomStartRequest
                {
                    RoomId = roomId,
                    StartedByUserId = "player-1",
                    StartedAtUtc = DateTime.UtcNow
                },
                cancellationToken);
    }

    private static async Task SetBattleSessionItemsAsync(
        ILakonaGameServer gameServer,
        GameSessionKey session,
        string roomId,
        string realtimeSessionId,
        long realtimeSessionGeneration,
        CancellationToken cancellationToken)
    {
        await gameServer.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString(roomId), cancellationToken);
        await gameServer.SetSessionItemAsync(session, "realtimeSessionId", GameSessionItemValue.FromString(realtimeSessionId), cancellationToken);
        await gameServer.SetSessionItemAsync(session, "realtimeSessionGeneration", GameSessionItemValue.FromInt64(realtimeSessionGeneration), cancellationToken);
    }

    private static async Task SetBattleSessionItemsAsync(
        ILakonaGameServer gameServer,
        GameSessionKey session,
        BattleSessionItemsCase sessionItemsCase,
        string validRoomId,
        CancellationToken cancellationToken)
    {
        switch (sessionItemsCase)
        {
            case BattleSessionItemsCase.Missing:
                return;
            case BattleSessionItemsCase.BlankRoomId:
                await SetBattleSessionItemsAsync(gameServer, session, "", "realtime-session-1", 3, cancellationToken);
                return;
            case BattleSessionItemsCase.BlankRealtimeSessionId:
                await SetBattleSessionItemsAsync(gameServer, session, validRoomId, "", 3, cancellationToken);
                return;
            case BattleSessionItemsCase.WrongKindRoomId:
                await gameServer.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromBoolean(true), cancellationToken);
                await gameServer.SetSessionItemAsync(session, "realtimeSessionId", GameSessionItemValue.FromString("realtime-session-1"), cancellationToken);
                await gameServer.SetSessionItemAsync(session, "realtimeSessionGeneration", GameSessionItemValue.FromInt64(3), cancellationToken);
                return;
            case BattleSessionItemsCase.WrongKindRealtimeGeneration:
                await gameServer.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString(validRoomId), cancellationToken);
                await gameServer.SetSessionItemAsync(session, "realtimeSessionId", GameSessionItemValue.FromString("realtime-session-1"), cancellationToken);
                await gameServer.SetSessionItemAsync(session, "realtimeSessionGeneration", GameSessionItemValue.FromString("3"), cancellationToken);
                return;
            case BattleSessionItemsCase.ZeroRealtimeGeneration:
                await SetBattleSessionItemsAsync(gameServer, session, validRoomId, "realtime-session-1", 0, cancellationToken);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(sessionItemsCase), sessionItemsCase, "Unknown session item case.");
        }
    }

    private static async Task<SubmittedInputState> InvokeBattleInputAndReadAsync(
        BattleInputContext context,
        float moveX,
        float moveY,
        int tick,
        CancellationToken cancellationToken)
    {
        var sessionItems = await context.GameServer.GetSessionItemsAsync(context.Session, cancellationToken);
        var call = new BattleServiceCall<InputMessage>(
            new InputMessage
            {
                MoveX = moveX,
                MoveY = moveY,
                Tick = tick
            },
            "battle-connection-1",
            new CapturingBattleCallback(),
            context.Session,
            sessionItems,
            context.HotfixRuntime.HotfixServices,
            context.Actors,
            context.GameServer);

        await context.HotfixRuntime.Invoker
            .InvokeAsync<IBattleService, BattleServiceCall<InputMessage>>(
                2,
                call,
                cancellationToken);

        return await ReadRoomSubmittedInputAsync(context.Actors, context.RoomId, cancellationToken);
    }

    private static ValueTask<SubmittedInputState> ReadRoomSubmittedInputAsync(
        IActorRuntime actors,
        string roomId,
        CancellationToken cancellationToken)
    {
        return actors.AskAsync<RoomActor, SubmittedInputState>(
            ActorId.From(roomId),
            (actor, _) =>
            {
                var state = GetRoomState(actor);
                var player = state.Simulation.Players.Single(player => string.Equals(player.PlayerId, "player-1", StringComparison.Ordinal));
                return new ValueTask<SubmittedInputState>(new SubmittedInputState(player.InputX, player.InputY, player.LastInputTick));
            },
            cancellationToken);
    }

    private static RoomState GetRoomState(RoomActor actor)
    {
        var stateField = typeof(RoomActor).GetField("State", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (RoomState)stateField.GetValue(actor)!;
    }

    private static void SeedSimulationRankingMasses(RoomActor actor)
    {
        var state = GetRoomState(actor);
        foreach (var player in state.Simulation.Players)
        {
            player.Mass = player.PlayerId switch
            {
                "p1" => 50f,
                "p2" => 25f,
                _ => player.Mass
            };
        }
    }

    private static string FindHotfixAssemblyPath(
        string assemblyFileName = "Server.Hotfix.dll",
        string hotfixProjectDirectoryName = "Hotfix")
    {
        var directCandidate = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
        if (File.Exists(directCandidate))
        {
            return directCandidate;
        }

        var root = FindRepositoryRoot();
        var configuration = GetConfigurationName();
        var candidates = new[]
        {
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", hotfixProjectDirectoryName, "bin", configuration, "net10.0", assemblyFileName),
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", hotfixProjectDirectoryName, "bin", "Debug", "net10.0", assemblyFileName),
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", hotfixProjectDirectoryName, "bin", "Release", "net10.0", assemblyFileName)
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate {assemblyFileName}. Checked:{Environment.NewLine}{string.Join(Environment.NewLine, candidates.Prepend(directCandidate))}",
            assemblyFileName);
    }

    private static string[] HotfixHostAssemblyNames()
    {
        return TestHotfix.HostAssemblyNames();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples", "Game.Unity.Agar")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }

    private static string GetConfigurationName()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string BuildReloadDiagnostics(Lakona.Game.Server.Hotfix.Abstractions.HotfixReloadResult reload)
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                $"Status: {reload.Status}",
                $"RequestedPath: {reload.RequestedPath}",
                $"ErrorMessage: {reload.ErrorMessage}",
                $"ExceptionType: {reload.ExceptionType}",
                "Diagnostics:",
                string.Join(Environment.NewLine, reload.Diagnostics)
            });
    }

    private static ClusterNodeDescriptor StateStoreNode(string nodeId, string clusterEndpoint)
    {
        return new ClusterNodeDescriptor(
            new NodeId(nodeId),
            NodeState.Ready,
            new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
            {
                ["cluster"] = new NodeEndpoint(clusterEndpoint)
            },
            labels: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["role"] = "state-store"
            });
    }

    private static ClusterNodeDescriptor SelectExpectedStateStoreOwner(
        string userId,
        IReadOnlyCollection<ClusterNodeDescriptor> nodes)
    {
        var ordered = nodes.OrderBy(node => node.Node.Value, StringComparer.Ordinal).ToArray();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userId));
        var value = 0UL;
        for (var index = 0; index < sizeof(ulong); index++)
        {
            value = (value << 8) | hash[index];
        }

        return ordered[(int)(value % (ulong)ordered.Length)];
    }

    private sealed class FixedClusterNodeDiscovery : IClusterNodeDiscovery
    {
        private readonly IReadOnlyList<ClusterNodeDescriptor> _nodes;

        public FixedClusterNodeDiscovery(IReadOnlyList<ClusterNodeDescriptor> nodes)
        {
            _nodes = nodes;
        }

        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default)
        {
            var matches = _nodes
                .Where(node => labels.All(label =>
                    node.Labels.TryGetValue(label.Key, out var value) &&
                    string.Equals(value, label.Value, StringComparison.Ordinal)))
                .ToArray();
            return new ValueTask<IReadOnlyList<ClusterNodeDescriptor>>(matches);
        }

        public async ValueTask<ClusterNodeDescriptor?> AnyAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default)
        {
            return (await ListAsync(labels, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }
    }

    private sealed class StateStoreRemoteActorInvoker : IRemoteActorInvoker
    {
        private readonly IRemoteActorSerializer _serializer;
        private readonly IActorDirectory _directory;

        public StateStoreRemoteActorInvoker(
            IRemoteActorSerializer serializer,
            IActorDirectory directory)
        {
            _serializer = serializer;
            _directory = directory;
        }

        public async ValueTask<RemoteActorInvocationResult> AskAsync(
            RemoteActorInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (IsBehaviorMethod(invocation, "LoginAndAttachAsync"))
            {
                if (!await IsRegisteredOnExpectedNodeAsync(invocation, cancellationToken).ConfigureAwait(false))
                {
                    return RemoteActorInvocationResult.Failed(
                        RemoteActorStatus.HandlerUnavailable,
                        $"User actor {invocation.ActorId.Value} was not created on {invocation.Node.Value}.");
                }

                _ = _serializer.Deserialize<UserLoginAndAttachRequest>(invocation.Payload);
                var result = new UserLoginResult
                {
                    UserId = invocation.ActorId.Value,
                    SessionToken = $"token-{invocation.ActorId.Value}",
                    LoginCount = 1,
                    LastLoginAtUtc = DateTime.UtcNow
                };
                return RemoteActorInvocationResult.Replied(_serializer.Serialize(result));
            }

            if (IsBehaviorMethod(invocation, "GetLeaderboardAsync"))
            {
                if (!await IsRegisteredOnExpectedNodeAsync(invocation, cancellationToken).ConfigureAwait(false))
                {
                    return RemoteActorInvocationResult.Failed(
                        RemoteActorStatus.HandlerUnavailable,
                        $"Leaderboard actor {invocation.ActorId.Value} was not created on {invocation.Node.Value}.");
                }

                var snapshot = new LeaderboardSnapshot
                {
                    PeriodStartLocalDate = "2026-06-22",
                    PeriodStartUtc = "2026-06-22",
                    SecondsUntilReset = 60,
                    Entries = []
                };
                return RemoteActorInvocationResult.Replied(_serializer.Serialize(snapshot));
            }

            return RemoteActorInvocationResult.Failed(
                RemoteActorStatus.HandlerUnavailable,
                $"Unexpected remote actor method {invocation.MethodName}.");
        }

        private async ValueTask<bool> IsRegisteredOnExpectedNodeAsync(
            RemoteActorInvocation invocation,
            CancellationToken cancellationToken)
        {
            var owner = await _directory.ResolveAsync(invocation.ActorId, cancellationToken)
                .ConfigureAwait(false);
            return owner is not null && owner.Node == invocation.Node;
        }

        private static bool IsBehaviorMethod(RemoteActorInvocation invocation, string methodName)
        {
            return invocation.MethodName.Contains($"|method:{methodName}|", StringComparison.Ordinal) ||
                invocation.MethodName.Contains($".{methodName}.", StringComparison.Ordinal) ||
                invocation.MethodName.EndsWith($".{methodName}", StringComparison.Ordinal) ||
                string.Equals(invocation.MethodName, methodName, StringComparison.Ordinal);
        }

        public ValueTask<RemoteActorInvocationResult> TellAsync(
            RemoteActorInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<RemoteActorInvocationResult>(RemoteActorInvocationResult.Accepted());
        }
    }

    private sealed class JsonRemoteActorSerializer : IRemoteActorSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload.Span) ??
                throw new InvalidOperationException($"Could not deserialize {typeof(T).FullName}.");
        }

        public ReadOnlyMemory<byte> Serialize(object? value, Type type)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, type);
        }

        public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
        {
            return JsonSerializer.Deserialize(payload.Span, type) ??
                throw new InvalidOperationException($"Could not deserialize {type.FullName}.");
        }
    }

    private sealed class CapturingPlayerCallback : IPlayerCallback
    {
        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
        }

        public void OnMatchProgress(MatchProgressUpdate update)
        {
        }
    }

    private sealed class CapturingBattleCallback : IBattleCallback
    {
        public void OnWorldState(WorldState worldState)
        {
        }

        public void OnPlayerDead(PlayerDead deadEvent)
        {
        }

        public void OnMatchEnd(MatchEnd matchEnd)
        {
        }
    }

    private sealed record BattleInputContext(
        ServiceProvider Provider,
        HotfixRuntimeSnapshot HotfixRuntime,
        IActorRuntime Actors,
        ILakonaGameServer GameServer,
        GameSessionKey Session,
        string RoomId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
        }
    }

    private sealed record SubmittedInputState(float InputX, float InputY, int LastInputTick);

    public enum BattleSessionItemsCase
    {
        Missing,
        BlankRoomId,
        BlankRealtimeSessionId,
        WrongKindRoomId,
        WrongKindRealtimeGeneration,
        ZeroRealtimeGeneration
    }

    private sealed class TestGameServer : ILakonaGameServer
    {
        public ValueTask<GameSessionKey> StartSessionAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<GameSessionKey>(new GameSessionKey(ownerKey, ownerKey, 1));
        }

        public ValueTask<GameSessionKey> StartSessionAsync(
            string ownerKey,
            string connectionId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<GameSessionKey>(new GameSessionKey(ownerKey, ownerKey, 1));
        }

        public ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
            string ownerKey,
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return new ValueTask<GameSessionKey>(new GameSessionKey(ownerKey, ownerKey, 1));
        }

        public ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
            GameSessionResumeRequest request,
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return new ValueTask<SessionResumeDecision>(SessionResumeDecision.StateLost("Not used."));
        }

        public ValueTask BindSessionAsync<TCallback>(
            GameSessionKey session,
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return default;
        }

        public ValueTask BindSessionAsync(
            GameSessionKey session,
            string connectionId,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask BindCurrentSessionAsync<TCallback>(
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return default;
        }

        public ValueTask MarkSessionDisconnectedAsync(
            GameSessionKey session,
            string? connectionId = null,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<TCallback?> GetCallbackAsync<TCallback>(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return new ValueTask<TCallback?>((TCallback?)null);
        }

        public ValueTask SetSessionItemAsync(
            GameSessionKey session,
            string key,
            GameSessionItemValue value,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<GameSessionItemValue?> GetSessionItemAsync(
            GameSessionKey session,
            string key,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<GameSessionItemValue?>((GameSessionItemValue?)null);
        }

        public ValueTask<GameSessionItems> GetSessionItemsAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<GameSessionItems>(GameSessionItems.Empty);
        }

        public ValueTask RemoveSessionItemAsync(
            GameSessionKey session,
            string key,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask TerminateSessionAsync(
            GameSessionKey session,
            SessionTerminationReason reason,
            string? message = null,
            SessionTerminationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

}
