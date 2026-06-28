using System.Reflection;
using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Matchmaking;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Users;
using Agar.Sample.State.Leaderboard;
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
using Server.Hotfix.Services;
using Server.Hotfix.State.Leaderboard;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;
using Shared.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarHotfixTests
{
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
        await using var rootServices = services.BuildServiceProvider();
        var manager = new HotfixManager(source, HotfixSharedAssemblyNames(), rootServices: rootServices);

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
        var featureTransport = new CapturingFeatureMessageTransport();
        var remoteSerializer = new JsonRemoteActorSerializer();
        var remoteInvoker = new StateStoreRemoteActorInvoker(remoteSerializer, featureTransport);
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
            Feature = []
        });
        services.AddSingleton<IClusterNodeDiscovery>(new FixedClusterNodeDiscovery(stateStoreNodes));
        services.AddSingleton<IFeatureMessageTransport>(featureTransport);
        services.RemoveAll<IRemoteActorSerializer>();
        services.RemoveAll<IRemoteActorInvoker>();
        services.AddSingleton<IRemoteActorSerializer>(remoteSerializer);
        services.AddSingleton<IRemoteActorInvoker>(remoteInvoker);
        var matchmakingNotifierType = typeof(LoginService).Assembly.GetType("Server.Hotfix.Services.MatchmakingNotifier", throwOnError: true)!;
        services.AddSingleton(matchmakingNotifierType);

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var service = new LoginService(provider.GetRequiredService<UserActors>());
        var call = new HotfixServiceCall<LoginRequest, IControlCallback>(
            new LoginRequest { GuestLogin = true },
            "control-connection-1",
            new CapturingControlCallback(),
            provider,
            actors,
            new TestGameServer());

        var reply = await service.LoginAsync(call);

        Assert.Equal(LoginResultCodes.Ok, reply.Code);
        Assert.StartsWith("guest-", reply.Account, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(reply.Password));
        Assert.Equal(reply.Account, reply.PlayerId);
        Assert.Equal(reply.PlayerId, reply.SessionId);
        Assert.Equal(1, reply.SessionGeneration);

        var expectedOwner = SelectExpectedStateStoreOwner(reply.PlayerId, stateStoreNodes);
        Assert.NotNull(featureTransport.LastTarget);
        Assert.Equal(expectedOwner.Node, featureTransport.LastTarget.Node);
        Assert.Equal("state-store", featureTransport.LastRequest?.Feature.Value);
        Assert.Equal("agar.state-store.ensure-user-actor.v1", featureTransport.LastRequest?.Kind);
        Assert.Equal(ActorState.Dead, actors.GetState(ActorId.From(reply.PlayerId)));
    }

    [Fact]
    public async Task Leaderboard_query_creates_current_leaderboard_actor_on_state_store_node()
    {
        await TestHotfix.LoadCurrentAsync(TestContext.Current.CancellationToken);

        var stateStoreNodes = new[]
        {
            StateStoreNode("state-b", "tcp://127.0.0.1:22002"),
            StateStoreNode("state-a", "tcp://127.0.0.1:22001")
        };
        var featureTransport = new CapturingFeatureMessageTransport();
        var remoteSerializer = new JsonRemoteActorSerializer();
        var remoteInvoker = new StateStoreRemoteActorInvoker(remoteSerializer, featureTransport);
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
            Feature = []
        });
        services.AddSingleton<IClusterNodeDiscovery>(new FixedClusterNodeDiscovery(stateStoreNodes));
        services.AddSingleton<IFeatureMessageTransport>(featureTransport);
        services.RemoveAll<IRemoteActorSerializer>();
        services.RemoveAll<IRemoteActorInvoker>();
        services.AddSingleton<IRemoteActorSerializer>(remoteSerializer);
        services.AddSingleton<IRemoteActorInvoker>(remoteInvoker);
        var matchmakingNotifierType = typeof(LoginService).Assembly.GetType("Server.Hotfix.Services.MatchmakingNotifier", throwOnError: true)!;
        services.AddSingleton(matchmakingNotifierType);

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var service = new PlayerService(
            provider.GetRequiredService<UserActors>(),
            provider.GetRequiredService<RoomActors>(),
            provider.GetRequiredService<MatchmakingActors>(),
            provider.GetRequiredService<LeaderboardActors>());
        var call = new HotfixServiceCall<LeaderboardRequest>(
            new LeaderboardRequest { TopN = 5 },
            "control-connection-1",
            new GameSessionKey("player-1", "session-1", 1),
            provider,
            actors,
            new TestGameServer());

        var reply = await service.GetLeaderboardAsync(call);

        Assert.Equal(0, reply.Code);
        var expectedOwner = SelectExpectedStateStoreOwner("current", stateStoreNodes);
        Assert.NotNull(featureTransport.LastTarget);
        Assert.Equal(expectedOwner.Node, featureTransport.LastTarget.Node);
        Assert.Equal("state-store", featureTransport.LastRequest?.Feature.Value);
        Assert.Equal("agar.state-store.ensure-leaderboard-actor.v1", featureTransport.LastRequest?.Kind);
        Assert.Equal(ActorState.Dead, actors.GetState(ActorId.From("current")));
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
    public async Task Hotfix_reload_includes_room_behavior_settlement_rules()
    {
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var source = new CurrentDirectoryHotfixAssemblySource(
            Path.GetDirectoryName(hotfixAssemblyPath)!,
            Path.GetFileName(hotfixAssemblyPath));
        await using var rootServices = TestHotfix.CreateRootServiceProvider();
        var manager = new HotfixManager(source, HotfixSharedAssemblyNames(), rootServices: rootServices);

        var reload = await manager.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.True(reload.Succeeded, BuildReloadDiagnostics(reload));
        Assert.Contains(
            reload.Current.Methods,
            key => key.StateTypeName == typeof(Agar.Sample.State.Rooms.RoomActor).FullName &&
                   key.MethodName == "TickAsync");
        Assert.DoesNotContain(
            reload.Current.Methods,
            key => key.StateTypeName == typeof(ArenaSimulation).FullName);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        new global::GeneratedHotfixActorRegistration().Register(services);
        services.AddGeneratedActorSelectorTestDependencies();
        var roomNotifierType = typeof(RoomBehavior).Assembly.GetType("Server.Hotfix.Services.RoomNotifier", throwOnError: true)!;
        services.AddSingleton(roomNotifierType);
        await using var behaviorServices = services.BuildServiceProvider();
        var actors = behaviorServices.GetRequiredService<IActorRuntime>();
        var lifecycle = (IActorLifecycle)actors;
        var roomId = "settlement-rules-room";
        var matchId = "settlement-rules-match";
        var players = new[] { "p1", "p2" };

        foreach (var playerId in players)
        {
            await lifecycle.CreateLocalAsync<UserActor>(ActorId.From(playerId), cancellationToken: TestContext.Current.CancellationToken);
            await actors.AskAsync<UserActor, UserLoginResult>(
                ActorId.From(playerId),
                (actor, _) => actor.LoginAsync(new UserLoginRequest
                {
                    Password = "test-password"
                }),
                TestContext.Current.CancellationToken);
        }

        await lifecycle.CreateLocalAsync<RoomActor>(ActorId.From(roomId), cancellationToken: TestContext.Current.CancellationToken);
        await lifecycle.CreateLocalAsync<LeaderboardActor>(ActorId.From("current"), cancellationToken: TestContext.Current.CancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.CreateAsync(new RoomCreateRequest
            {
                RoomId = roomId,
                MatchId = matchId,
                CreatedByUserId = "p1",
                CreatedAtUtc = DateTime.UtcNow,
                MaxPlayers = 2,
                Players =
                [
                    BuildAssignment("p1", roomId, matchId, 0),
                    BuildAssignment("p2", roomId, matchId, 1)
                ]
            }),
            TestContext.Current.CancellationToken);
        await actors.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => actor.StartAsync(new RoomStartRequest
            {
                RoomId = roomId,
                StartedByUserId = "p1",
                StartedAtUtc = DateTime.UtcNow
            }),
            TestContext.Current.CancellationToken);
        await actors.TellAsync<RoomActor>(
            ActorId.From(roomId),
            (actor, _) =>
            {
                SeedSimulationRankingMasses(actor);
                return default;
            },
            TestContext.Current.CancellationToken);

        await actors.TellAsync<RoomActor>(
            ActorId.From(roomId),
            (actor, _) => actor.TickAsync(new HotfixActorTick
            {
                ObservedAtUtc = DateTime.UtcNow,
                Interval = TimeSpan.FromSeconds(121),
                DispatchTableVersion = reload.Current.DispatchTableVersion
            }),
            TestContext.Current.CancellationToken);

        var room = await actors.AskAsync<RoomActor, RoomSnapshot>(
            ActorId.From(roomId),
            (actor, _) => actor.GetSnapshotAsync(new RoomSnapshotRequest()),
            TestContext.Current.CancellationToken);
        var p1 = await actors.AskAsync<UserActor, UserProfileSnapshot>(
            ActorId.From("p1"),
            (actor, _) => actor.GetProfileAsync(new UserProfileRequest()),
            TestContext.Current.CancellationToken);
        var p2 = await actors.AskAsync<UserActor, UserProfileSnapshot>(
            ActorId.From("p2"),
            (actor, _) => actor.GetProfileAsync(new UserProfileRequest()),
            TestContext.Current.CancellationToken);
        var leaderboard = await actors.AskAsync<LeaderboardActor, LeaderboardSnapshot>(
            ActorId.From("current"),
            (actor, _) => actor.GetLeaderboardAsync(new LeaderboardQueryRequest { TopN = 10 }),
            TestContext.Current.CancellationToken);

        Assert.Equal(RoomStatus.Finished, room.Status);
        Assert.Equal("p1", room.WinnerUserId);
        Assert.Equal(1, room.Players.Single(player => player.UserId == "p1").Rank);
        Assert.Equal(2, room.Players.Single(player => player.UserId == "p2").Rank);
        Assert.Equal(1, p1.WinCount);
        Assert.Equal(10, p1.VictoryPoints);
        Assert.Equal(0, p2.WinCount);
        Assert.Equal(7, p2.VictoryPoints);
        Assert.Equal(
            ["p1", "p2"],
            leaderboard.Entries.Select(entry => entry.PlayerId).ToArray());
        Assert.Equal(10, leaderboard.Entries.Single(entry => entry.PlayerId == "p1").VictoryPoints);
        Assert.Equal(7, leaderboard.Entries.Single(entry => entry.PlayerId == "p2").VictoryPoints);
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

    private static void SeedSimulationRankingMasses(RoomActor actor)
    {
        var stateField = typeof(RoomActor).GetField("State", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = (RoomState)stateField.GetValue(actor)!;
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

    private static string[] HotfixSharedAssemblyNames()
    {
        return TestHotfix.SharedAssemblyNames();
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
            [new NodeFeatureDescriptor("state-store")]);
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

    private sealed class CapturingFeatureMessageTransport : IFeatureMessageTransport
    {
        public ClusterNodeDescriptor? LastTarget { get; private set; }

        public FeatureMessageRequest? LastRequest { get; private set; }

        public bool HasCreatedUserActorOn(NodeId node, string userId)
        {
            return LastTarget?.Node == node &&
                LastRequest is not null &&
                string.Equals(LastRequest.Kind, "agar.state-store.ensure-user-actor.v1", StringComparison.Ordinal) &&
                LastRequest.Payload.Length > 0 &&
                JsonSerializer.Deserialize<EnsureUserActorProbe>(
                    LastRequest.Payload.Span,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))?.UserId == userId;
        }

        public bool HasCreatedLeaderboardActorOn(NodeId node, string leaderboardId)
        {
            return LastTarget?.Node == node &&
                LastRequest is not null &&
                string.Equals(LastRequest.Kind, "agar.state-store.ensure-leaderboard-actor.v1", StringComparison.Ordinal) &&
                LastRequest.Payload.Length > 0 &&
                JsonSerializer.Deserialize<EnsureLeaderboardActorProbe>(
                    LastRequest.Payload.Span,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))?.LeaderboardId == leaderboardId;
        }

        public ValueTask<FeatureMessageReply> SendAsync(
            ClusterNodeDescriptor target,
            FeatureMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            LastTarget = target;
            LastRequest = request;
            return new ValueTask<FeatureMessageReply>(
                new FeatureMessageReply(ClusterSendStatus.Accepted, ReadOnlyMemory<byte>.Empty));
        }
    }

    private sealed class StateStoreRemoteActorInvoker : IRemoteActorInvoker
    {
        private readonly IRemoteActorSerializer _serializer;
        private readonly CapturingFeatureMessageTransport _featureTransport;

        public StateStoreRemoteActorInvoker(
            IRemoteActorSerializer serializer,
            CapturingFeatureMessageTransport featureTransport)
        {
            _serializer = serializer;
            _featureTransport = featureTransport;
        }

        public ValueTask<RemoteActorInvocationResult> AskAsync(
            RemoteActorInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (invocation.MethodName.Contains(".LoginAsync.", StringComparison.Ordinal) ||
                invocation.MethodName.EndsWith(".LoginAsync", StringComparison.Ordinal) ||
                string.Equals(invocation.MethodName, "LoginAsync", StringComparison.Ordinal))
            {
                if (!_featureTransport.HasCreatedUserActorOn(invocation.Node, invocation.ActorId.Value))
                {
                    return new ValueTask<RemoteActorInvocationResult>(RemoteActorInvocationResult.Failed(
                        RemoteActorStatus.HandlerUnavailable,
                        $"User actor {invocation.ActorId.Value} was not created on {invocation.Node.Value}."));
                }

                var result = new UserLoginResult
                {
                    UserId = invocation.ActorId.Value,
                    SessionToken = $"token-{invocation.ActorId.Value}",
                    LoginCount = 1,
                    LastLoginAtUtc = DateTime.UtcNow
                };
                return new ValueTask<RemoteActorInvocationResult>(
                    RemoteActorInvocationResult.Replied(_serializer.Serialize(result)));
            }

            if (invocation.MethodName.Contains(".AttachAsync.", StringComparison.Ordinal) ||
                invocation.MethodName.EndsWith(".AttachAsync", StringComparison.Ordinal) ||
                string.Equals(invocation.MethodName, "AttachAsync", StringComparison.Ordinal))
            {
                if (!_featureTransport.HasCreatedUserActorOn(invocation.Node, invocation.ActorId.Value))
                {
                    return new ValueTask<RemoteActorInvocationResult>(RemoteActorInvocationResult.Failed(
                        RemoteActorStatus.HandlerUnavailable,
                        $"User actor {invocation.ActorId.Value} was not created on {invocation.Node.Value}."));
                }

                var request = _serializer.Deserialize<PlayerSessionAttachRequest>(invocation.Payload);
                var snapshot = new PlayerSessionSnapshot
                {
                    UserId = request.UserId,
                    SessionToken = request.SessionToken,
                    ConnectionId = request.ConnectionId,
                    ControlSessionId = request.ControlSessionId,
                    ControlSessionGeneration = request.ControlSessionGeneration,
                    IsOnline = true,
                    AttachedAtUtc = request.AttachedAtUtc,
                    ControlGateway = request.ControlGateway
                };
                return new ValueTask<RemoteActorInvocationResult>(
                    RemoteActorInvocationResult.Replied(_serializer.Serialize(snapshot)));
            }

            if (invocation.MethodName.Contains(".GetLeaderboardAsync.", StringComparison.Ordinal) ||
                invocation.MethodName.EndsWith(".GetLeaderboardAsync", StringComparison.Ordinal) ||
                string.Equals(invocation.MethodName, "GetLeaderboardAsync", StringComparison.Ordinal))
            {
                if (!_featureTransport.HasCreatedLeaderboardActorOn(invocation.Node, invocation.ActorId.Value))
                {
                    return new ValueTask<RemoteActorInvocationResult>(RemoteActorInvocationResult.Failed(
                        RemoteActorStatus.HandlerUnavailable,
                        $"Leaderboard actor {invocation.ActorId.Value} was not created on {invocation.Node.Value}."));
                }

                var snapshot = new LeaderboardSnapshot
                {
                    PeriodStartLocalDate = "2026-06-22",
                    PeriodStartUtc = "2026-06-22",
                    SecondsUntilReset = 60,
                    Entries = []
                };
                return new ValueTask<RemoteActorInvocationResult>(
                    RemoteActorInvocationResult.Replied(_serializer.Serialize(snapshot)));
            }

            return new ValueTask<RemoteActorInvocationResult>(RemoteActorInvocationResult.Failed(
                RemoteActorStatus.HandlerUnavailable,
                $"Unexpected remote actor method {invocation.MethodName}."));
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
    }

    private sealed class EnsureUserActorProbe
    {
        public string UserId { get; set; } = "";
    }

    private sealed class EnsureLeaderboardActorProbe
    {
        public string LeaderboardId { get; set; } = "";
    }

    private sealed class CapturingControlCallback : IControlCallback
    {
        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
        }
    }

    private sealed class TestGameServer : ILakonaGameServer
    {
        public ValueTask<GameSessionKey> StartSessionAsync(
            string ownerKey,
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
