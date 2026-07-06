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
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Services;
using Server.Hotfix.Features;
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
    public void AgarHotfixFeatures_DoNotRegisterFeatureMessageHandlers()
    {
        var root = FindRepositoryRoot();
        var hotfixFiles = Directory.GetFiles(
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Hotfix"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in hotfixFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("IFeatureMessageHandler", text, StringComparison.Ordinal);
            Assert.DoesNotContain("FeatureMessageReply", text, StringComparison.Ordinal);
            Assert.DoesNotContain("FeatureMessageRequest", text, StringComparison.Ordinal);
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
        var featureCommands = new CapturingFeatureCommandClient();
        var remoteSerializer = new JsonRemoteActorSerializer();
        var remoteInvoker = new StateStoreRemoteActorInvoker(remoteSerializer, featureCommands);
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
        services.AddSingleton<IFeatureCommandClient>(featureCommands);
        services.RemoveAll<IRemoteActorSerializer>();
        services.RemoveAll<IRemoteActorInvoker>();
        services.AddSingleton<IRemoteActorSerializer>(remoteSerializer);
        services.AddSingleton<IRemoteActorInvoker>(remoteInvoker);
        var matchmakingNotifierType = typeof(LoginService).Assembly.GetType("Server.Hotfix.Services.MatchmakingNotifier", throwOnError: true)!;
        services.AddSingleton(matchmakingNotifierType);

        await using var provider = services.BuildServiceProvider();
        featureCommands.UseActorDirectory(provider.GetRequiredService<IActorDirectory>());
        var actors = provider.GetRequiredService<IActorRuntime>();
        var service = new LoginService(
            provider.GetRequiredService<UserActors>(),
            provider.GetRequiredService<LakonaGameRuntimeOptions>(),
            provider.GetRequiredService<LocalActorNodeIdentity>(),
            provider.GetRequiredService<ActorHosting>(),
            provider.GetServices<IClusterNodeDiscovery>(),
            provider.GetRequiredService<ILogger<LoginService>>());
        var call = new HotfixServiceCall<LoginRequest, ILoginCallback>(
            new LoginRequest { GuestLogin = true },
            "control-connection-1",
            new CapturingLoginCallback(),
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
        Assert.NotNull(featureCommands.LastTarget);
        Assert.Equal(expectedOwner.Node, featureCommands.LastTarget.Node);
        Assert.Equal("state-store", featureCommands.LastFeatureName);
        Assert.Equal("CreateUserActorRequest", featureCommands.LastRequestTypeName);
        Assert.Equal(ActorState.Dead, actors.GetState(ActorId.From(reply.PlayerId)));
    }

    [Fact]
    public async Task Leaderboard_query_uses_existing_global_leaderboard_actor()
    {
        var stateStoreNodes = new[]
        {
            StateStoreNode("state-b", "tcp://127.0.0.1:22002"),
            StateStoreNode("state-a", "tcp://127.0.0.1:22001")
        };
        var featureCommands = new CapturingFeatureCommandClient();
        var remoteSerializer = new JsonRemoteActorSerializer();
        var remoteInvoker = new StateStoreRemoteActorInvoker(remoteSerializer, featureCommands);
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
            Feature = ["state-store"]
        });
        services.AddSingleton<IClusterNodeDiscovery>(new FixedClusterNodeDiscovery(stateStoreNodes));
        services.AddSingleton<IFeatureCommandClient>(featureCommands);
        services.RemoveAll<IRemoteActorSerializer>();
        services.RemoveAll<IRemoteActorInvoker>();
        services.AddSingleton<IRemoteActorSerializer>(remoteSerializer);
        services.AddSingleton<IRemoteActorInvoker>(remoteInvoker);
        var matchmakingNotifierType = typeof(LoginService).Assembly.GetType("Server.Hotfix.Services.MatchmakingNotifier", throwOnError: true)!;
        services.AddSingleton(matchmakingNotifierType);

        await using var provider = services.BuildServiceProvider();
        featureCommands.UseActorDirectory(provider.GetRequiredService<IActorDirectory>());
        var expectedOwner = SelectExpectedStateStoreOwner("current", stateStoreNodes);
        featureCommands.RegisterExistingLeaderboardActor(expectedOwner.Node, "current");
        await provider.GetRequiredService<IActorDirectory>()
            .RegisterAsync(ActorId.From("current"), expectedOwner.Node, TestContext.Current.CancellationToken);
        var actors = provider.GetRequiredService<IActorRuntime>();
        var hotfixRuntime = await TestHotfix.LoadCurrentRuntimeAsync(provider, TestContext.Current.CancellationToken);
        var gameServer = new TestGameServer();
        var call = new HotfixServiceCall<LeaderboardRequest, IPlayerCallback>(
            new LeaderboardRequest { TopN = 5 },
            "control-connection-1",
            new CapturingPlayerCallback(),
            new GameSessionKey("player-1", "session-1", 1),
            hotfixRuntime.HotfixServices,
            actors,
            gameServer);

        var reply = await hotfixRuntime.Invoker
            .InvokeAsync<IPlayerService, HotfixServiceCall<LeaderboardRequest, IPlayerCallback>, LeaderboardReply>(
                4,
                call,
                TestContext.Current.CancellationToken);

        Assert.Equal(0, reply.Code);
        Assert.Equal(typeof(IPlayerCallback), gameServer.LastBoundSessionCallbackType);
        Assert.Null(featureCommands.LastTarget);
        Assert.Equal("", featureCommands.LastFeatureName);
        Assert.Equal("", featureCommands.LastRequestTypeName);
        Assert.Equal(ActorState.Dead, actors.GetState(ActorId.From("current")));
    }

    [Fact]
    public async Task Leaderboard_feature_start_creates_current_leaderboard_actor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        new global::GeneratedHotfixActorRegistration().Register(services);
        services.AddGeneratedActorSelectorTestDependencies();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "state-store-1" },
            Feature = ["leaderboard"]
        });

        await using var provider = services.BuildServiceProvider();
        var actors = provider.GetRequiredService<IActorRuntime>();
        var call = new HotfixFeatureStartCall(
            "leaderboard",
            new HotfixFeatureState(),
            provider,
            TestContext.Current.CancellationToken);

        await LeaderboardFeature.StartAsync(call);

        Assert.Equal(ActorState.Active, actors.GetState(ActorId.From("current")));
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

    private sealed class CapturingFeatureCommandClient : IFeatureCommandClient
    {
        private readonly HashSet<(NodeId Node, string ActorId)> _createdLeaderboardActors = [];
        private readonly HashSet<(NodeId Node, string ActorId)> _createdUserActors = [];
        private IActorDirectory? _directory;

        public ClusterNodeDescriptor? LastTarget { get; private set; }

        public string LastFeatureName { get; private set; } = "";

        public string LastRequestTypeName { get; private set; } = "";

        public string LastUserId { get; private set; } = "";

        public string LastLeaderboardId { get; private set; } = "";

        public void UseActorDirectory(IActorDirectory directory)
        {
            _directory = directory;
        }

        public bool HasCreatedUserActorOn(NodeId node, string userId)
        {
            return _createdUserActors.Contains((node, userId));
        }

        public bool HasCreatedLeaderboardActorOn(NodeId node, string leaderboardId)
        {
            return _createdLeaderboardActors.Contains((node, leaderboardId));
        }

        public void RegisterExistingLeaderboardActor(NodeId node, string leaderboardId)
        {
            _createdLeaderboardActors.Add((node, leaderboardId));
        }

        public ValueTask<TReply> SendAsync<TRequest, TReply>(
            string featureName,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            return CaptureAndReply<TRequest, TReply>(null, featureName, request, cancellationToken);
        }

        public ValueTask<TReply> SendToNodeAsync<TRequest, TReply>(
            ClusterNodeDescriptor target,
            string featureName,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            return CaptureAndReply<TRequest, TReply>(target, featureName, request, cancellationToken);
        }

        private async ValueTask<TReply> CaptureAndReply<TRequest, TReply>(
            ClusterNodeDescriptor? target,
            string featureName,
            TRequest request,
            CancellationToken cancellationToken)
        {
            LastTarget = target;
            LastFeatureName = featureName;
            LastRequestTypeName = typeof(TRequest).Name;
            LastUserId = ReadStringProperty(request, "UserId");
            LastLeaderboardId = ReadStringProperty(request, "LeaderboardId");
            if (target is not null && _directory is not null)
            {
                var createdActorId = !string.IsNullOrWhiteSpace(LastUserId)
                    ? ActorId.From(LastUserId)
                    : !string.IsNullOrWhiteSpace(LastLeaderboardId)
                        ? ActorId.From(LastLeaderboardId)
                        : default;
                if (!string.IsNullOrWhiteSpace(createdActorId.Value))
                {
                    if (!string.IsNullOrWhiteSpace(LastUserId))
                    {
                        _createdUserActors.Add((target.Node, LastUserId));
                    }

                    if (!string.IsNullOrWhiteSpace(LastLeaderboardId))
                    {
                        _createdLeaderboardActors.Add((target.Node, LastLeaderboardId));
                    }

                    await _directory.RegisterAsync(createdActorId, target.Node, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var reply = Activator.CreateInstance(typeof(TReply), nonPublic: true)
                ?? throw new InvalidOperationException($"Could not create reply type {typeof(TReply).FullName}.");
            typeof(TReply).GetProperty("Succeeded")?.SetValue(reply, true);
            typeof(TReply).GetProperty("Message")?.SetValue(reply, "Actor ready.");
            return (TReply)reply;
        }

        private static string ReadStringProperty<TRequest>(TRequest request, string propertyName)
        {
            return typeof(TRequest).GetProperty(propertyName)?.GetValue(request) as string ?? "";
        }
    }

    private sealed class StateStoreRemoteActorInvoker : IRemoteActorInvoker
    {
        private readonly IRemoteActorSerializer _serializer;
        private readonly CapturingFeatureCommandClient _featureCommands;

        public StateStoreRemoteActorInvoker(
            IRemoteActorSerializer serializer,
            CapturingFeatureCommandClient featureCommands)
        {
            _serializer = serializer;
            _featureCommands = featureCommands;
        }

        public ValueTask<RemoteActorInvocationResult> AskAsync(
            RemoteActorInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (IsBehaviorMethod(invocation, "LoginAsync"))
            {
                if (!_featureCommands.HasCreatedUserActorOn(invocation.Node, invocation.ActorId.Value))
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

            if (IsBehaviorMethod(invocation, "AttachAsync"))
            {
                if (!_featureCommands.HasCreatedUserActorOn(invocation.Node, invocation.ActorId.Value))
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

            if (IsBehaviorMethod(invocation, "GetLeaderboardAsync"))
            {
                if (!_featureCommands.HasCreatedLeaderboardActorOn(invocation.Node, invocation.ActorId.Value))
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

    private sealed class CapturingLoginCallback : ILoginCallback
    {
        public void OnPlaceHolder(MatchmakingStatusUpdate matchmakingStatus)
        {
        }
    }

    private sealed class CapturingPlayerCallback : IPlayerCallback
    {
        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
        }
    }

    private sealed class TestGameServer : ILakonaGameServer
    {
        public Type? LastBoundSessionCallbackType { get; private set; }

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
            LastBoundSessionCallbackType = typeof(TCallback);
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
