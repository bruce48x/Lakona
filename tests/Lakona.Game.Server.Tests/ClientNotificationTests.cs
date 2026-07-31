using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClientNotificationTests
{
    [Fact]
    public void RouteKeyIncludesOwnerAndSession()
    {
        var session = new GameSessionKey("player-1", "session-a");

        var route = ClientNotificationRouteKey.FromSession(session);

        Assert.Equal("client-session:player-1/session-a", route.Value);
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
    public async Task LocalCommandDispatcherRoutesGeneratedCommandBytesToSerializedOverload()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new SerializedDispatchTargetCallback();
        await directory.BindSessionAsync(session, "conn-1", TestContext.Current.CancellationToken);
        await using var connection = new TestCallbackConnection(directory, "conn-1", callback);
        var dispatcher = new LocalClientNotificationCommandDispatcher(connection.Resolver);
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "memorypack");
        command.Metadata = new ClientNotificationMetadata
        {
            Type = "lakona.game.reliable-push",
            Payload = new byte[] { 4, 5, 6 }
        };

        var status = await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, status);
        Assert.Equal(0, callback.TypedDispatchCount);
        Assert.Equal(1, callback.SerializedDispatchCount);
        Assert.Equal("memorypack", JsonSerializer.Deserialize<string>(callback.LastPayload.Span));
        Assert.Equal(command.Metadata.Type, callback.LastMetadata?.Type);
        Assert.Equal(command.Metadata.Payload, callback.LastMetadata?.Payload);
    }

    [Fact]
    public async Task LocalCommandDispatcherPropagatesCallerCancellationFromDispatchTarget()
    {
        var directory = new InMemoryGameSessionRegistry();
        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();
        var callback = new CancelingDispatchTargetCallback(cts);
        await directory.BindSessionAsync(session, "conn-1", TestContext.Current.CancellationToken);
        await using var connection = new TestCallbackConnection(directory, "conn-1", callback);
        var dispatcher = new LocalClientNotificationCommandDispatcher(connection.Resolver);
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "cancel");

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
        var session = new GameSessionKey("player-1", "session-a");

        await registrar.RegisterAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("client-session:player-1/session-a", routes.LastRoute);
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
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership();
        services.RemoveAll<IRouteDirectory>();
        services.AddSingleton<IRouteDirectory>(routes);
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var session = await server.StartSessionAsync(
            "player-1",
            "conn-1",
            TestContext.Current.CancellationToken);
        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("client-session:player-1/" + session.SessionId, routes.LastRoute);
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
        services.UseReadySingleNodeMembership();
        services.RemoveAll<IRouteDirectory>();
        services.AddSingleton<IRouteDirectory>(new FailingRegisterRouteDirectory());
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await server.StartSessionAsync(
                "player-1",
                "conn-1",
                TestContext.Current.CancellationToken));

        Assert.Equal("route registration failed", ex.Message);
        var sessions = provider.GetRequiredService<IGameSessionRegistry>();
        Assert.Null(await sessions.GetCurrentSessionAsync(
            "conn-1",
            TestContext.Current.CancellationToken));
        Assert.Equal(0, sessions.GetDiagnosticsSnapshot().TotalSessions);
    }

    [Fact]
    public async Task SessionEstablishmentFailureRollsBackRouteTicketAndNewSession()
    {
        var routes = new CapturingRouteDirectory();
        var tickets = new RecordingResumeTicketStore();
        var services = new ServiceCollection();
        services.AddSingleton<IRouteDirectory>(routes);
        services.AddSingleton<IGameSessionResumeTicketStore>(tickets);
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
        services.UseReadySingleNodeMembership();
        services.RemoveAll<IRouteDirectory>();
        services.AddSingleton<IRouteDirectory>(routes);
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await server.StartSessionAsync(
                "player-1",
                "missing-connection",
                TestContext.Current.CancellationToken));

        Assert.Contains("is not available", exception.Message, StringComparison.Ordinal);
        Assert.True(tickets.Issued);
        Assert.True(tickets.Revoked);
        Assert.Equal(routes.LastRoute, routes.LastUnregisteredRoute);
        var sessions = provider.GetRequiredService<IGameSessionRegistry>();
        Assert.Equal(0, sessions.GetDiagnosticsSnapshot().TotalSessions);
        Assert.Null(await sessions.GetCurrentSessionAsync(
            "missing-connection",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistingSessionBindingFailureRestoresDisconnectedSession()
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
        services.UseReadySingleNodeMembership();
        services.RemoveAll<IRouteDirectory>();
        services.AddSingleton<IRouteDirectory>(new FailingRegisterRouteDirectory());
        await using var provider = services.BuildServiceProvider();
        var sessions = provider.GetRequiredService<IGameSessionRegistry>();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var session = await sessions.StartNewSessionAsync(
            "player-1",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await server.BindSessionAsync(
                session,
                "conn-1",
                TestContext.Current.CancellationToken));

        Assert.Null(await sessions.GetCurrentSessionAsync(
            "conn-1",
            TestContext.Current.CancellationToken));
        Assert.Equal(
            SessionResumeStatus.Resumed,
            (await sessions.TryResumeAsync(
                session,
                TestContext.Current.CancellationToken)).Status);
        var diagnostics = sessions.GetDiagnosticsSnapshot();
        Assert.Equal(1, diagnostics.TotalSessions);
        Assert.Equal(0, diagnostics.ActiveSessions);
    }

    [Fact]
    public async Task ClientNotifications_delivers_in_background_without_outbox_when_reliable_push_is_disabled()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServerSessions();
        services.AddLakonaGameServerReliablePush();
        await using var provider = services.BuildServiceProvider();
        var directory = provider.GetRequiredService<IGameSessionRegistry>();
        var notifications = provider.GetRequiredService<IClientNotifications>();
        var outbox = provider.GetRequiredService<IReliablePushOutbox>();
        var callback = new SerializedFallbackCallback();

        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        await directory.BindSessionAsync(session, "conn-1", TestContext.Current.CancellationToken);
        await using var connection = new TestCallbackConnection(
            directory,
            provider.GetRequiredService<GameFrameworkConnectionRegistry>(),
            provider.GetRequiredService<GameSessionCallbackProxyRegistry>(),
            "conn-1",
            callback);

        var status = notifications
            .ForSession<ITestPlayerCallback>(session)
            .EnqueueGenerated(
                1,
                1,
                nameof(ITestPlayerCallback.Notify),
                "payload");
        await ((ClientNotificationCommandRouter)provider.GetRequiredService<IClientNotificationCommandRouter>())
            .WaitForIdleAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, status);
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
        services.AddLakonaGameServerReliablePush();
        await using var provider = services.BuildServiceProvider();
        var sessions = provider.GetRequiredService<IGameSessionRegistry>();
        var session = await sessions.StartNewSessionAsync(
            "player-1",
            TestContext.Current.CancellationToken);
        var callback = new DispatchTargetCallback();
        await sessions.BindSessionAsync(session, "control-1", TestContext.Current.CancellationToken);
        await using var connection = new TestCallbackConnection(
            sessions,
            provider.GetRequiredService<GameFrameworkConnectionRegistry>(),
            provider.GetRequiredService<GameSessionCallbackProxyRegistry>(),
            "control-1",
            callback);
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "best-effort");
        command.Metadata = new ClientNotificationMetadata
        {
            Type = "untrusted",
            Payload = new byte[] { 9 }
        };

        var status = await provider.GetRequiredService<IReliablePushRuntime>()
            .PublishAsync(session, command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, status);
        Assert.Null(callback.LastMetadata);
        Assert.Equal("best-effort", callback.LastArguments.Single());
    }

    [Fact]
    public async Task Remote_session_notification_is_relayed_before_local_sequence_assignment()
    {
        var session = new GameSessionKey("player-1", "session-a");
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                DateTimeOffset.UtcNow.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var ownerRuntime = new RecordingReliablePushRuntime();
        var remote = new RecordingRemoteNotificationDispatcher();
        await using var router = new ClientNotificationCommandRouter(
            ownerRuntime,
            routes,
            remote,
            new NodeId("data-1"));
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "matched");

        var status = router.Enqueue(command);
        await router.WaitForIdleAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, status);
        Assert.Empty(ownerRuntime.Published);
        Assert.Same(command, remote.LastCommand);
        Assert.Null(remote.LastCommand!.Metadata);
    }

    [Fact]
    public async Task Local_session_route_publishes_through_owner_runtime()
    {
        var session = new GameSessionKey("player-1", "session-a");
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                DateTimeOffset.UtcNow.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var ownerRuntime = new RecordingReliablePushRuntime();
        var remote = new RecordingRemoteNotificationDispatcher();
        await using var router = new ClientNotificationCommandRouter(
            ownerRuntime,
            routes,
            remote,
            new NodeId("gateway-1"));
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "queued");

        var status = router.Enqueue(command);
        await router.WaitForIdleAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, status);
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
        var session = new GameSessionKey("player-1", "session-a");
        var ownerRuntime = new RecordingReliablePushRuntime();
        var remote = new RecordingRemoteNotificationDispatcher();
        await using var router = new ClientNotificationCommandRouter(
            ownerRuntime,
            new InMemoryRouteDirectory(),
            remote,
            new NodeId("data-1"));
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "matched");

        var status = router.Enqueue(command);
        await router.WaitForIdleAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, status);
        Assert.Empty(ownerRuntime.Published);
        Assert.Null(remote.LastCommand);
    }

    [Fact]
    public async Task Owner_ingress_rejects_missing_route_without_publishing()
    {
        var session = new GameSessionKey("player-1", "session-a");
        var ownerRuntime = new RecordingReliablePushRuntime();
        var ingress = new ClientNotificationOwnerDispatcher(
            ownerRuntime,
            new InMemoryRouteDirectory(),
            new NodeId("gateway-1"));
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "matched");

        var status = await ingress.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
        Assert.Empty(ownerRuntime.Published);
    }

    [Fact]
    public async Task Owner_ingress_accepts_current_session_route_regardless_of_route_directory_version()
    {
        var session = new GameSessionKey("player-1", "session-a");
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                DateTimeOffset.UtcNow.AddMinutes(1),
                generation: 2),
            TestContext.Current.CancellationToken);
        var ownerRuntime = new RecordingReliablePushRuntime();
        var ingress = new ClientNotificationOwnerDispatcher(
            ownerRuntime,
            routes,
            new NodeId("gateway-1"));
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "matched");

        var status = await ingress.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, status);
        Assert.Single(ownerRuntime.Published);
    }

    [Fact]
    public async Task Owner_ingress_rejects_expired_route_without_publishing()
    {
        var session = new GameSessionKey("player-1", "session-a");
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-1"),
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                DateTimeOffset.UtcNow.AddSeconds(-1)),
            TestContext.Current.CancellationToken);
        var ownerRuntime = new RecordingReliablePushRuntime();
        var ingress = new ClientNotificationOwnerDispatcher(
            ownerRuntime,
            routes,
            new NodeId("gateway-1"));
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "matched");

        var status = await ingress.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
        Assert.Empty(ownerRuntime.Published);
    }

    [Fact]
    public async Task Owner_ingress_rejects_route_moved_to_another_node_without_publishing()
    {
        var session = new GameSessionKey("player-1", "session-a");
        var routes = new InMemoryRouteDirectory();
        await routes.RegisterAsync(
            new RouteLocation(
                ClientNotificationRouteKey.FromSession(session),
                new NodeId("gateway-2"),
                new NodeEndpoint("tcp://127.0.0.1:22002"),
                DateTimeOffset.UtcNow.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        var ownerRuntime = new RecordingReliablePushRuntime();
        var ingress = new ClientNotificationOwnerDispatcher(
            ownerRuntime,
            routes,
            new NodeId("gateway-1"));
        var command = ClientNotificationCommandFactory.CreateGenerated<ITestPlayerCallback, string>(
            session,
            serviceId: 7,
            methodId: 11,
            nameof(ITestPlayerCallback.Notify),
            "matched");

        var status = await ingress.DispatchAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
        Assert.Empty(ownerRuntime.Published);
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

    [Fact]
    public void Legacy_client_notification_sink_is_not_a_public_contract()
    {
        Assert.Null(typeof(IClientNotifications).Assembly.GetType(
            "Lakona.Game.Server.Sessions.IClientNotificationSink`1",
            throwOnError: false));
    }

    private sealed class SerializedFallbackCallback :
        ITestPlayerCallback,
        IRpcNotificationDispatchTarget
    {
        public List<string> Delivered { get; } = [];

        public void Notify(string message)
        {
        }

        public ValueTask DispatchNotificationAsync(
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            Delivered.Add(JsonSerializer.Deserialize<string>(payload.Span)!);
            return default;
        }

        public ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private interface ITestPlayerCallback
    {
        void Notify(string message);
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
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            LastMethodName = nameof(ITestPlayerCallback.Notify);
            LastArguments = [JsonSerializer.Deserialize<string>(payload.Span)];
            LastMetadata = metadata;
            return default;
        }

        public ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            _source.Cancel();
            return new ValueTask(Task.FromCanceled(cancellationToken));
        }

        public ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class SerializedDispatchTargetCallback : ITestPlayerCallback, IRpcNotificationDispatchTarget
    {
        public int TypedDispatchCount { get; private set; }

        public int SerializedDispatchCount { get; private set; }

        public ReadOnlyMemory<byte> LastPayload { get; private set; }

        public RpcPushMetadata? LastMetadata { get; private set; }

        public void Notify(string message)
        {
        }

        public ValueTask DispatchNotificationAsync<TPayload>(
            int serviceId,
            int methodId,
            TPayload payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            TypedDispatchCount++;
            return default;
        }

        public ValueTask DispatchNotificationAsync(
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            SerializedDispatchCount++;
            LastPayload = payload;
            LastMetadata = metadata;
            return default;
        }

        public ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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

    private sealed class RecordingResumeTicketStore : IGameSessionResumeTicketStore
    {
        public bool Issued { get; private set; }

        public bool Revoked { get; private set; }

        public ValueTask<string> IssueAsync(
            GameSessionKey session,
            string endpointScope,
            CancellationToken cancellationToken = default)
        {
            Issued = true;
            return new ValueTask<string>("test-resume-ticket");
        }

        public ValueTask<GameSessionKey?> ResolveAsync(
            string ticket,
            string endpointScope,
            CancellationToken cancellationToken = default) =>
            new((GameSessionKey?)null);

        public ValueTask RevokeAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            Revoked = true;
            return default;
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
            return new ValueTask<ClientNotificationStatus>(ClientNotificationStatus.Accepted);
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
            return new ValueTask<ClientNotificationStatus>(ClientNotificationStatus.Accepted);
        }
    }
}
