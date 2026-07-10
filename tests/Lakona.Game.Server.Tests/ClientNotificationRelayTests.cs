using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClientNotificationRelayTests
{
    [Fact]
    public void RouteKeyIncludesOwnerSessionAndGeneration()
    {
        var session = new GameSessionKey("player-1", "session-a", 7);

        var route = ClientNotificationRouteKey.FromSession(session);

        Assert.Equal("client-session:player-1/session-a/7", route.Value);
    }

    [Fact]
    public async Task RelayInvokesLocalCallbackOnGateway()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new TestPlayerCallback();
        await directory.BindSessionAsync(session, "conn-1", callback, TestContext.Current.CancellationToken);
        var relay = new ClientNotificationRelay(directory);

        var status = await relay.NotifyAsync<TestPlayerCallback>(
            session,
            cb => cb.Notify("hello"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Equal("hello", callback.LastMessage);
    }

    [Fact]
    public async Task RelayPropagatesCallerCancellationFromLocalCallback()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "conn-1", new TestPlayerCallback(), TestContext.Current.CancellationToken);
        var relay = new ClientNotificationRelay(directory);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await relay.NotifyAsync<TestPlayerCallback>(
                    session,
                    _ =>
                    {
                        cts.Cancel();
                        return new ValueTask(Task.FromCanceled(cts.Token));
                    },
                    cts.Token)
                .AsTask());
    }

    [Fact]
    public async Task RelayReturnsRouteNotFoundForStaleGeneration()
    {
        var directory = new InMemoryGameSessionRegistry();
        var current = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var stale = new GameSessionKey(current.OwnerKey, current.SessionId, current.Generation + 1);
        var relay = new ClientNotificationRelay(directory);

        var status = await relay.NotifyAsync<TestPlayerCallback>(
            stale,
            cb => cb.Notify("stale"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
    }

    [Fact]
    public async Task RelayResolvesRemoteGatewayRouteAndInvokesGatewayLocalCallbackOnly()
    {
        var gatewaySessions = new InMemoryGameSessionRegistry();
        var session = await gatewaySessions.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new TestPlayerCallbackContract();
        await gatewaySessions.BindSessionAsync(session, "gateway-conn-1", callback, TestContext.Current.CancellationToken);
        var route = new RouteLocation(
            ClientNotificationRouteKey.FromSession(session),
            new NodeId("gateway-1"),
            new NodeEndpoint("tcp://10.0.0.2:21002"),
            DateTimeOffset.UtcNow.AddMinutes(1),
            generation: session.Generation);
        var routes = new ResolvingRouteDirectory(route);
        var dispatcher = new DelegatingRemoteNotificationDispatcher(
            new LocalClientNotificationCommandDispatcher(gatewaySessions));
        var remoteRelay = new ClientNotificationRelay(
            new InMemoryGameSessionRegistry(),
            routes,
            dispatcher,
            new NodeId("battle-1"));

        var status = await remoteRelay.NotifyAsync<ITestPlayerCallback>(
            session,
            cb => cb.Notify("remote"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Equal("remote", callback.LastMessage);
        Assert.Equal("client-session:player-1/" + session.SessionId + "/1", routes.LastResolvedRoute);
        Assert.Empty(route.Metadata);
        Assert.Equal(new NodeId("gateway-1"), dispatcher.LastTarget?.Node);
        Assert.Equal("tcp://10.0.0.2:21002", dispatcher.LastTarget?.Endpoint.Address);
        Assert.Equal(
            typeof(ITestPlayerCallback).AssemblyQualifiedName,
            dispatcher.LastCommand?.CallbackContractType);
        Assert.Equal(nameof(ITestPlayerCallback.Notify), dispatcher.LastCommand?.MethodName);
    }

    [Fact]
    public void RemoteDispatcherPublicApiDoesNotRequireProcessLocalActionDelegate()
    {
        var methods = typeof(IClientNotificationRemoteDispatcher).GetMethods();

        Assert.DoesNotContain(methods, method => method
            .GetParameters()
            .Any(parameter => parameter.ParameterType.IsGenericType
                && parameter.ParameterType.GetGenericTypeDefinition() == typeof(Action<>)));
    }

    [Fact]
    public void CommandFactoryCapturesCallbackMethodAndPayload()
    {
        var session = new GameSessionKey("player-1", "session-a", 7);

        var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
            session,
            callback => callback.Notify("remote"));

        Assert.NotNull(command);
        Assert.Equal("player-1", command.OwnerKey);
        Assert.Equal("session-a", command.SessionId);
        Assert.Equal(7, command.Generation);
        Assert.Equal(typeof(ITestPlayerCallback).AssemblyQualifiedName, command.CallbackContractType);
        Assert.Equal(nameof(ITestPlayerCallback.Notify), command.MethodName);
        var argument = Assert.Single(command.Arguments);
        Assert.Equal(typeof(string).AssemblyQualifiedName, argument.TypeName);
    }

    [Fact]
    public async Task LocalCommandDispatcherUsesGeneratedDispatchTargetWithMetadata()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new DispatchTargetCallback();
        await directory.BindSessionAsync(session, "conn-1", callback, TestContext.Current.CancellationToken);
        var dispatcher = new LocalClientNotificationCommandDispatcher(directory);
        var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
            session,
            cb => cb.Notify("metadata"))!;
        command.Metadata = new RpcPushMetadata
        {
            Type = "lakona.game.reliable-push",
            Payload = new byte[] { 1, 2, 3 }
        };

        var status = await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Equal(nameof(ITestPlayerCallback.Notify), callback.LastMethodName);
        Assert.Equal("metadata", callback.LastArguments.Single());
        Assert.NotNull(callback.LastMetadata);
        Assert.Equal("lakona.game.reliable-push", callback.LastMetadata.Type);
        Assert.Equal(new byte[] { 1, 2, 3 }, callback.LastMetadata.Payload.ToArray());
    }

    [Fact]
    public async Task LocalCommandDispatcherPropagatesCallerCancellationFromDispatchTarget()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();
        var callback = new CancelingDispatchTargetCallback(cts);
        await directory.BindSessionAsync(session, "conn-1", callback, TestContext.Current.CancellationToken);
        var dispatcher = new LocalClientNotificationCommandDispatcher(directory);
        var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
            session,
            cb => cb.Notify("cancel"))!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await dispatcher.DispatchAsync(command, cts.Token).AsTask());
    }

    [Fact]
    public async Task RegistrarRegistersGatewayOwnedClientSessionRoute()
    {
        var routes = new CapturingRouteDirectory();
        var registrar = new ClientSessionRouteRegistrar(
            routes,
            new NodeId("gateway-1"),
            new NodeEndpoint("tcp://10.0.0.2:21002"));
        var session = new GameSessionKey("player-1", "session-a", 7);

        await registrar.RegisterAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("client-session:player-1/session-a/7", routes.LastRoute);
        Assert.Equal(new NodeId("gateway-1"), routes.LastNode);
        Assert.Equal("tcp://10.0.0.2:21002", routes.LastEndpoint);
    }

    [Fact]
    public async Task SessionBindRegistersAndTerminationRemovesClientSessionRoute()
    {
        var routes = new CapturingRouteDirectory();
        var services = new ServiceCollection();
        services.AddSingleton<IRouteDirectory>(routes);
        services.AddSingleton(new ClusterOptions
        {
            NodeId = "gateway-1",
            AdvertisedEndpoints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cluster"] = "tcp://10.0.0.2:21002"
            },
            RouteLeaseSeconds = 30
        });
        services.AddLakonaGameServer();
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var session = await server.StartSessionAsync(
            "player-1",
            "conn-1",
            new TestPlayerCallback(),
            TestContext.Current.CancellationToken);
        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("client-session:player-1/" + session.SessionId + "/1", routes.LastRoute);
        Assert.Equal(routes.LastRoute, routes.LastUnregisteredRoute);
        Assert.Equal(new NodeId("gateway-1"), routes.LastNode);
        Assert.Equal("tcp://10.0.0.2:21002", routes.LastEndpoint);
    }

    [Fact]
    public async Task SessionBindFailsWhenClientSessionRouteRegistrationFails()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRouteDirectory>(new FailingRegisterRouteDirectory());
        services.AddSingleton(new ClusterOptions
        {
            NodeId = "gateway-1",
            AdvertisedEndpoints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cluster"] = "tcp://10.0.0.2:21002"
            },
            RouteLeaseSeconds = 30
        });
        services.AddLakonaGameServer();
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.StartSessionAsync(
                "player-1",
                "conn-1",
                new TestPlayerCallback(),
                TestContext.Current.CancellationToken));

        Assert.Equal("route registration failed", ex.Message);
    }

    [Fact]
    public async Task ClientNotifications_delivers_immediately_without_outbox_when_reliable_push_is_disabled()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServerSessions();
        services.AddLakonaGameServerReliablePush(options => options.Enabled = false);
        await using var provider = services.BuildServiceProvider();
        var directory = provider.GetRequiredService<IGameSessionRegistry>();
        var notifications = provider.GetRequiredService<IClientNotifications>();
        var outbox = provider.GetRequiredService<IReliablePushOutbox>();
        var callback = new NotificationSink();

        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync<IClientNotificationSink<string>>(
            session,
            "conn-1",
            callback,
            TestContext.Current.CancellationToken);

        var status = await notifications
            .ForSession(session)
            .NotifyAsync<IClientNotificationSink<string>>(
                sink => sink.OnNotificationAsync("payload"),
                TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Equal(["payload"], callback.Delivered);
        var pending = new List<ReliablePushRecord>();
        await outbox.ReplayPendingAsync(
            ReliablePushSessionOwnerKey.Create(session),
            record =>
            {
                pending.Add(record);
                return default;
            },
            TestContext.Current.CancellationToken);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Disabled_reliable_push_owner_dispatches_without_incoming_metadata()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServerSessions();
        services.AddLakonaGameServerReliablePush(options => options.Enabled = false);
        await using var provider = services.BuildServiceProvider();
        var sessions = provider.GetRequiredService<IGameSessionRegistry>();
        var session = await sessions.StartNewSessionAsync(
            "player-1",
            TestContext.Current.CancellationToken);
        var callback = new DispatchTargetCallback();
        await sessions.BindSessionAsync<ITestPlayerCallback>(
            session,
            "control-1",
            callback,
            TestContext.Current.CancellationToken);
        var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
            session,
            target => target.Notify("best-effort"))!;
        command.Metadata = new RpcPushMetadata
        {
            Type = "untrusted",
            Payload = new byte[] { 9 }
        };

        var status = await provider.GetRequiredService<IReliablePushRuntime>()
            .PublishAsync(session, command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Null(callback.LastMetadata);
        Assert.Equal("best-effort", callback.LastArguments.Single());
    }

    [Fact]
    public async Task Remote_session_notification_is_relayed_before_local_sequence_assignment()
    {
        var session = new GameSessionKey("player-1", "session-a", 1);
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: session.Generation),
            TestContext.Current.CancellationToken);
        var ownerRuntime = new RecordingReliablePushRuntime();
        var remote = new RecordingRemoteNotificationDispatcher();
        var router = new ClientNotificationCommandRouter(
            ownerRuntime,
            routes,
            remote,
            new NodeId("data-1"));
        var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
            session,
            callback => callback.Notify("matched"))!;

        var status = await router.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Empty(ownerRuntime.Published);
        Assert.Same(command, remote.LastCommand);
        Assert.Null(remote.LastCommand!.Metadata);
    }

    [Fact]
    public async Task Local_session_route_publishes_through_owner_runtime()
    {
        var session = new GameSessionKey("player-1", "session-a", 1);
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: session.Generation),
            TestContext.Current.CancellationToken);
        var ownerRuntime = new RecordingReliablePushRuntime();
        var remote = new RecordingRemoteNotificationDispatcher();
        var router = new ClientNotificationCommandRouter(
            ownerRuntime,
            routes,
            remote,
            new NodeId("gateway-1"));
        var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
            session,
            callback => callback.Notify("queued"))!;

        var status = await router.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Collection(ownerRuntime.Published, item =>
        {
            Assert.Equal(session, item.Session);
            Assert.Same(command, item.Command);
        });
        Assert.Null(remote.LastCommand);
    }

    [Fact]
    public async Task Missing_cluster_route_does_not_create_non_owner_outbox_record()
    {
        var session = new GameSessionKey("player-1", "session-a", 1);
        var ownerRuntime = new RecordingReliablePushRuntime();
        var remote = new RecordingRemoteNotificationDispatcher();
        var router = new ClientNotificationCommandRouter(
            ownerRuntime,
            new InMemoryRouteDirectory(),
            remote,
            new NodeId("data-1"));
        var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
            session,
            callback => callback.Notify("matched"))!;

        var status = await router.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
        Assert.Empty(ownerRuntime.Published);
        Assert.Null(remote.LastCommand);
    }

    [Fact]
    public async Task Session_index_is_not_registered_by_framework_sessions()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddLakonaGameServer()
            .BuildServiceProvider();

        Assert.Null(typeof(ILakonaGameServer).Assembly.GetType("Lakona.Game.Server.Sessions.IClientSessionIndex"));
        Assert.Null(typeof(ILakonaGameServer).Assembly.GetType("Lakona.Game.Server.Sessions.InMemoryClientSessionIndex"));
    }

    private sealed class TestPlayerCallback
    {
        public string LastMessage { get; private set; } = "";

        public void Notify(string message)
        {
            LastMessage = message;
        }
    }

    private sealed class NotificationSink : IClientNotificationSink<string>
    {
        public List<string> Delivered { get; } = [];

        public ValueTask OnNotificationAsync(string payload, CancellationToken cancellationToken = default)
        {
            Delivered.Add(payload);
            return default;
        }
    }

    private interface ITestPlayerCallback
    {
        void Notify(string message);
    }

    private sealed class TestPlayerCallbackContract : ITestPlayerCallback
    {
        public string LastMessage { get; private set; } = "";

        public void Notify(string message)
        {
            LastMessage = message;
        }
    }

    private sealed class DispatchTargetCallback : ITestPlayerCallback, IRpcNotificationDispatchTarget
    {
        public string LastMessage { get; private set; } = "";

        public string LastMethodName { get; private set; } = "";

        public object?[] LastArguments { get; private set; } = [];

        public RpcPushMetadata? LastMetadata { get; private set; }

        public void Notify(string message)
        {
            LastMessage = message;
        }

        public ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            LastMethodName = methodName;
            LastArguments = arguments;
            LastMetadata = metadata;
            return default;
        }
    }

    private sealed class CancelingDispatchTargetCallback : ITestPlayerCallback, IRpcNotificationDispatchTarget
    {
        private readonly CancellationTokenSource _source;

        public CancelingDispatchTargetCallback(CancellationTokenSource source)
        {
            _source = source;
        }

        public void Notify(string message)
        {
        }

        public ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            _source.Cancel();
            return new ValueTask(Task.FromCanceled(cancellationToken));
        }
    }

    private sealed class CapturingRouteDirectory : IRouteDirectory
    {
        public string LastRoute { get; private set; } = "";

        public NodeId LastNode { get; private set; }

        public string LastEndpoint { get; private set; } = "";

        public string LastUnregisteredRoute { get; private set; } = "";

        public ValueTask<RouteRegistrationStatus> RegisterAsync(
            RouteLocation location,
            CancellationToken cancellationToken = default)
        {
            LastRoute = location.Route.Value;
            LastNode = location.Node;
            LastEndpoint = location.Endpoint.Address;
            return new ValueTask<RouteRegistrationStatus>(RouteRegistrationStatus.Registered);
        }

        public ValueTask<RouteUnregisterStatus> UnregisterAsync(
            RouteKey route,
            CancellationToken cancellationToken = default)
        {
            LastUnregisteredRoute = route.Value;
            return new ValueTask<RouteUnregisterStatus>(RouteUnregisterStatus.Removed);
        }

        public ValueTask<RouteLocation?> ResolveAsync(
            RouteKey route,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
            RouteLocation expectedLocation,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ExpireAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ClearByNodeAsync(
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ClearByNodeEpochAsync(
            NodeId node,
            long nodeEpoch,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FailingRegisterRouteDirectory : IRouteDirectory
    {
        public ValueTask<RouteRegistrationStatus> RegisterAsync(
            RouteLocation location,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("route registration failed");
        }

        public ValueTask<RouteUnregisterStatus> UnregisterAsync(
            RouteKey route,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<RouteUnregisterStatus>(RouteUnregisterStatus.Removed);
        }

        public ValueTask<RouteLocation?> ResolveAsync(
            RouteKey route,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
            RouteLocation expectedLocation,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ExpireAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ClearByNodeAsync(
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ClearByNodeEpochAsync(
            NodeId node,
            long nodeEpoch,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ResolvingRouteDirectory : IRouteDirectory
    {
        private readonly RouteLocation _location;

        public ResolvingRouteDirectory(RouteLocation location)
        {
            _location = location;
        }

        public string LastResolvedRoute { get; private set; } = "";

        public ValueTask<RouteRegistrationStatus> RegisterAsync(
            RouteLocation location,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<RouteUnregisterStatus> UnregisterAsync(
            RouteKey route,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<RouteLocation?> ResolveAsync(
            RouteKey route,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            LastResolvedRoute = route.Value;
            return ValueTask.FromResult<RouteLocation?>(_location.IsExpired(now) ? null : _location);
        }

        public ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
            RouteLocation expectedLocation,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ExpireAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ClearByNodeAsync(
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ClearByNodeEpochAsync(
            NodeId node,
            long nodeEpoch,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class DelegatingRemoteNotificationDispatcher : IClientNotificationRemoteDispatcher
    {
        private readonly LocalClientNotificationCommandDispatcher _gatewayDispatcher;

        public DelegatingRemoteNotificationDispatcher(LocalClientNotificationCommandDispatcher gatewayDispatcher)
        {
            _gatewayDispatcher = gatewayDispatcher;
        }

        public RouteLocation? LastTarget { get; private set; }

        public ClientNotificationCommand? LastCommand { get; private set; }

        public ValueTask<ClientNotificationStatus> DispatchAsync(
            RouteLocation target,
            ClientNotificationCommand command,
            CancellationToken cancellationToken = default)
        {
            LastTarget = target;
            LastCommand = command;
            return _gatewayDispatcher.DispatchAsync(command, cancellationToken);
        }
    }

    private sealed class RecordingReliablePushRuntime : IReliablePushRuntime
    {
        public List<(GameSessionKey Session, ClientNotificationCommand Command)> Published { get; } = [];

        public ValueTask<ClientNotificationStatus> PublishAsync(
            GameSessionKey session,
            ClientNotificationCommand command,
            CancellationToken cancellationToken = default)
        {
            Published.Add((session, command));
            return new ValueTask<ClientNotificationStatus>(ClientNotificationStatus.Delivered);
        }

        public ValueTask ReplayPendingAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<ReliablePushAckOutcome> AckAsync(
            GameSessionKey currentSession,
            GameSessionKey acknowledgedSession,
            long sequence,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.Accepted());
        }
    }

    private sealed class RecordingRemoteNotificationDispatcher : IClientNotificationRemoteDispatcher
    {
        public ClientNotificationCommand? LastCommand { get; private set; }

        public ValueTask<ClientNotificationStatus> DispatchAsync(
            RouteLocation target,
            ClientNotificationCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return new ValueTask<ClientNotificationStatus>(ClientNotificationStatus.Delivered);
        }
    }
}
