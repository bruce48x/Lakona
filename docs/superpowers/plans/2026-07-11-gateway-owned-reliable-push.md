# Gateway-Owned Reliable Push Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route every session notification intent to the session-owning node before assigning its reliable-push sequence, so local and remote producers share one gateway outbox, ACK endpoint, and replay order.

**Architecture:** `ClientNotifications` captures a callback command and hands it to the command router without touching an outbox. The router resolves session ownership first: local/no-cluster routes enter the local `IReliablePushRuntime`, while remote routes send an unsequenced command to the owner. The gateway cluster binder then enters that same local runtime, which assigns metadata and dispatches only through the local callback dispatcher.

**Tech Stack:** .NET 10, C# 13, xUnit, Lakona RPC TCP cluster transport, JSON/MemoryPack serializers, Docker Compose, Unity 2022.3 PlayMode tests, PowerShell 7.

---

## File Map

- Modify `src/Lakona.Game.Server/Sessions/ClientNotifications.cs`: capture notification intent and route it before reliable publication.
- Modify `src/Lakona.Game.Server/Sessions/ClientNotificationCommandRouter.cs`: resolve owner first, publish locally only for local ownership, relay unsequenced commands for remote ownership.
- Modify `src/Lakona.Game.Server/ReliablePush/ReliablePushRuntime.cs`: become an owner-local outbox/metadata/local-dispatch component with no cluster routing dependency.
- Modify `src/Lakona.Game.Server/Sessions/SessionServiceCollectionExtensions.cs`: wire the dependency inversion without a DI cycle.
- Modify `src/Lakona.Game.Server/Sessions/ClientNotificationCommandBinder.cs`: add an internal owner-publication binding for the framework cluster endpoint while retaining the existing direct binding used by low-level tests.
- Modify `src/Lakona.Game.Server/Hosting/LakonaClusterRpcServerConfigurator.cs`: bind inbound cluster intents to owner publication, not raw local callback dispatch.
- Create `tests/Lakona.Game.Server.Tests/ClientNotificationOwnerIntegrationTests.cs`: real TCP regression for one owner sequence stream and owner-side ACK state.
- Modify `tests/Lakona.Game.Server.Tests/ClientNotificationRelayTests.cs`: protect route-before-sequence behavior, route failures, disabled reliability, and remote intent shape.
- Modify `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`: retain cluster dispatcher replacement/custom preservation coverage already developed for this incident.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`: retain the real startup-timer partial-batch regression already developed for this incident.
- Modify `docs/session.md` and `docs/cluster.md`: make route-owner outbox semantics durable.
- Clean temporary diagnostics in `ClusterClientNotificationDispatcher.cs`, `LocalClientNotificationCommandDispatcher.cs`, `MatchmakingNotifier.cs`, `DotArenaGame.Callbacks.cs`, and restore the Unity E2E timeout in `DotArenaThreeNodePlayModeTests.cs`.
- Delete this completed plan and `docs/superpowers/specs/2026-07-10-gateway-owned-reliable-push-design.md` only after durable docs and final validation are complete.

### Task 1: Prove The Router Must Select The Owner Before Publication

**Files:**
- Modify: `tests/Lakona.Game.Server.Tests/ClientNotificationRelayTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj`

- [ ] **Step 1: Write the failing remote-owner routing test**

Add a test that constructs the desired router with a recording owner runtime instead of a local dispatcher:

```csharp
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
    var router = new ClientNotificationCommandRouter(ownerRuntime, routes, remote, new NodeId("data-1"));
    var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
        session,
        callback => callback.Notify("matched"))!;

    var status = await router.DispatchAsync(command, TestContext.Current.CancellationToken);

    Assert.Equal(ClientNotificationStatus.Delivered, status);
    Assert.Empty(ownerRuntime.Published);
    Assert.Same(command, remote.LastCommand);
    Assert.Null(remote.LastCommand!.Metadata);
}
```

Add these recording fakes near the existing test helpers:

```csharp
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

    public ValueTask ReplayPendingAsync(GameSessionKey session, CancellationToken cancellationToken = default) => default;

    public ValueTask<ReliablePushAckOutcome> AckAsync(
        GameSessionKey currentSession,
        GameSessionKey acknowledgedSession,
        long sequence,
        CancellationToken cancellationToken = default) =>
        new(new ReliablePushAckOutcome(ReliablePushAckStatus.Accepted));
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
```

- [ ] **Step 2: Run the test and verify RED**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Remote_session_notification_is_relayed_before_local_sequence_assignment"
```

Expected: compilation fails because `ClientNotificationCommandRouter` still requires `LocalClientNotificationCommandDispatcher`; this proves the owner-publication boundary does not yet exist.

- [ ] **Step 3: Add local-owner and missing-route expectations before implementation**

Add:

```csharp
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
    var router = new ClientNotificationCommandRouter(ownerRuntime, routes, remote, new NodeId("gateway-1"));
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
```

### Task 2: Invert Notification Routing And Owner Publication

**Files:**
- Modify: `src/Lakona.Game.Server/Sessions/ClientNotifications.cs`
- Modify: `src/Lakona.Game.Server/Sessions/ClientNotificationCommandRouter.cs`
- Modify: `src/Lakona.Game.Server/ReliablePush/ReliablePushRuntime.cs`
- Modify: `src/Lakona.Game.Server/Sessions/SessionServiceCollectionExtensions.cs`
- Test: `tests/Lakona.Game.Server.Tests/ClientNotificationRelayTests.cs`

- [ ] **Step 1: Make `ClientNotifications` route the captured intent**

Replace its runtime dependency and final call with:

```csharp
private readonly IClientNotificationCommandRouter _router;

public ClientNotifications(IClientNotificationCommandRouter router)
{
    _router = router ?? throw new ArgumentNullException(nameof(router));
}

// At the end of NotifyAsync:
return await _router.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 2: Make the command router select ownership before publication**

Replace the router's local-dispatch dependency and `DispatchAsync` body with:

```csharp
private readonly IReliablePushRuntime _localOwner;

public ClientNotificationCommandRouter(
    IReliablePushRuntime localOwner,
    IRouteDirectory? routes = null,
    IClientNotificationRemoteDispatcher? remoteDispatcher = null,
    NodeId? localNode = null)
{
    _localOwner = localOwner ?? throw new ArgumentNullException(nameof(localOwner));
    _routes = routes;
    _remoteDispatcher = remoteDispatcher;
    _localNode = localNode;
}

public async ValueTask<ClientNotificationStatus> DispatchAsync(
    ClientNotificationCommand command,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(command);
    var session = ToSessionKey(command);

    if (_routes is null || _remoteDispatcher is null || _localNode is null)
    {
        return await _localOwner.PublishAsync(session, command, cancellationToken).ConfigureAwait(false);
    }

    var route = await _routes.ResolveAsync(
        ClientNotificationRouteKey.FromSession(session),
        DateTimeOffset.UtcNow,
        cancellationToken).ConfigureAwait(false);
    if (route is null || route.Generation != session.Generation)
    {
        return ClientNotificationStatus.RouteNotFound;
    }

    if (route.Node == _localNode.Value)
    {
        return await _localOwner.PublishAsync(session, command, cancellationToken).ConfigureAwait(false);
    }

    command.Metadata = null;
    return await _remoteDispatcher.DispatchAsync(route, command, cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 3: Make reliable publication dispatch only to the local callback**

In `ReliablePushRuntime`, replace `IClientNotificationCommandRouter` with `LocalClientNotificationCommandDispatcher`. Preserve the disabled branch but make it owner-local and strip non-authoritative metadata:

```csharp
if (!_options.Enabled)
{
    command.Metadata = null;
    return await _localDispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
}
```

Then replace the last line of `DispatchRecordAsync` with:

```csharp
return await _localDispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
```

Keep outbox publication, metadata creation, replay, and ACK code unchanged.

- [ ] **Step 4: Update DI construction without a cycle**

Change `CreateClientNotificationCommandRouter` to:

```csharp
private static IClientNotificationCommandRouter CreateClientNotificationCommandRouter(IServiceProvider services)
{
    var localOwner = services.GetRequiredService<IReliablePushRuntime>();
    var routes = services.GetService<IRouteDirectory>();
    var remoteDispatcher = services.GetService<IClientNotificationRemoteDispatcher>();
    var cluster = services.GetService<ClusterOptions>();
    NodeId? localNode = cluster is null ? (NodeId?)null : new NodeId(cluster.NodeId);
    return new ClientNotificationCommandRouter(localOwner, routes, remoteDispatcher, localNode);
}
```

The runtime itself resolves only `IReliablePushOutbox`, `IReliablePushAckService`, `LocalClientNotificationCommandDispatcher`, and `ReliablePushOptions`, so resolving `IClientNotifications` no longer forms a router/runtime cycle.

- [ ] **Step 5: Run focused tests and verify GREEN**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~session_notification|FullyQualifiedName~Missing_cluster_route|FullyQualifiedName~ClientNotifications_delivers"
```

Expected: all selected tests pass, including single-node disabled-reliability delivery.

- [ ] **Step 6: Commit the routing inversion**

```powershell
git add src/Lakona.Game.Server/Sessions/ClientNotifications.cs src/Lakona.Game.Server/Sessions/ClientNotificationCommandRouter.cs src/Lakona.Game.Server/ReliablePush/ReliablePushRuntime.cs src/Lakona.Game.Server/Sessions/SessionServiceCollectionExtensions.cs tests/Lakona.Game.Server.Tests/ClientNotificationRelayTests.cs
git commit -m "Route notifications before reliable publication"
```

### Task 3: Make The Gateway Cluster Handler The Reliable-Push Owner

**Files:**
- Create: `tests/Lakona.Game.Server.Tests/ClientNotificationOwnerIntegrationTests.cs`
- Modify: `src/Lakona.Game.Server/Sessions/ClientNotificationCommandBinder.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaClusterRpcServerConfigurator.cs`
- Test: `tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj`

- [ ] **Step 1: Write a failing real-TCP owner sequence regression**

Create the complete file:

```csharp
using System.Net;
using System.Net.Sockets;
using Lakona.Game.Abstractions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClientNotificationOwnerIntegrationTests
{
    [Fact]
    public async Task Gateway_assigns_one_sequence_stream_to_local_and_remote_notifications()
    {
        var port = GetFreePort();
        var gatewayServices = new ServiceCollection();
        gatewayServices.AddLakonaGameServerSessions();
        gatewayServices.AddLakonaGameServerReliablePush();
        await using var gateway = gatewayServices.BuildServiceProvider();
        var sessions = gateway.GetRequiredService<IGameSessionRegistry>();
        var session = await sessions.StartNewSessionAsync(
            "player-1",
            TestContext.Current.CancellationToken);
        var callback = new SequenceCapturingDispatchTarget();
        await sessions.BindSessionAsync<ITestPlayerCallback>(
            session,
            "control-1",
            callback,
            TestContext.Current.CancellationToken);

        var localStatus = await gateway.GetRequiredService<IClientNotifications>()
            .ForSession(session)
            .NotifyAsync<ITestPlayerCallback>(
                target =>
                {
                    target.Notify("queued");
                    return default;
                },
                TestContext.Current.CancellationToken);

        using var stop = new CancellationTokenSource();
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new TcpConnectionAcceptor(port, "127.0.0.1"));
        ClientNotificationCommandBinder.BindOwned(
            builder.ServiceRegistry,
            gateway.GetRequiredService<IReliablePushRuntime>());
        var serverTask = builder.RunAsync(stop.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        ClientNotificationStatus remoteStatus;
        try
        {
            await using var clients = new ClusterClientFactory(
                new TcpClusterTransportFactory(),
                new JsonRpcSerializer());
            var remote = new ClusterClientNotificationDispatcher(clients);
            var command = ClientNotificationCommandFactory.Create<ITestPlayerCallback>(
                session,
                target => target.Notify("matched"))!;
            command.Metadata = new RpcPushMetadata
            {
                Type = "untrusted",
                Payload = new byte[] { 9 }
            };

            remoteStatus = await remote.DispatchAsync(
                new RouteLocation(
                    ClientNotificationRouteKey.FromSession(session),
                    new NodeId("gateway-1"),
                    new NodeEndpoint($"tcp://127.0.0.1:{port}"),
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    generation: session.Generation),
                command,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            stop.Cancel();
            await Task.WhenAny(
                serverTask,
                Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }

        var ack = await gateway.GetRequiredService<IReliablePushRuntime>().AckAsync(
            session,
            session,
            2,
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, localStatus);
        Assert.Equal(ClientNotificationStatus.Delivered, remoteStatus);
        Assert.Equal([1L, 2L], callback.Sequences);
        Assert.Equal(["queued", "matched"], callback.Messages);
        Assert.Equal(ReliablePushAckStatus.Accepted, ack.Status);
    }

    private interface ITestPlayerCallback
    {
        void Notify(string message);
    }

    private sealed class SequenceCapturingDispatchTarget :
        ITestPlayerCallback,
        IRpcNotificationDispatchTarget
    {
        public List<long> Sequences { get; } = [];

        public List<string> Messages { get; } = [];

        public void Notify(string message)
        {
        }

        public ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(nameof(ITestPlayerCallback.Notify), methodName);
            Assert.NotNull(metadata);
            var reliable = LakonaInternalCodec.DecodeReliablePushMetadata(metadata.Payload);
            Sequences.Add(reliable.Sequence.Value);
            Messages.Add(Assert.IsType<string>(arguments[0]));
            return default;
        }
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
```

The remote command deliberately carries untrusted metadata. The final callback must decode gateway-generated reliable metadata and observe sequence 2.

- [ ] **Step 2: Run the test and verify RED**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Gateway_assigns_one_sequence_stream"
```

Expected: compilation fails because `BindOwned` does not exist. Do not change the test to call the raw local-dispatch binding.

- [ ] **Step 3: Add the owner-side binder path**

In `ClientNotificationCommandBinder`, retain the current public raw-dispatch constructor/static overload for low-level compatibility, and add an internal owner delegate path:

```csharp
private readonly Func<ClientNotificationCommand, CancellationToken, ValueTask<ClientNotificationStatus>> _dispatch;

public ClientNotificationCommandBinder(LocalClientNotificationCommandDispatcher dispatcher)
{
    ArgumentNullException.ThrowIfNull(dispatcher);
    _dispatch = dispatcher.DispatchAsync;
}

private ClientNotificationCommandBinder(IReliablePushRuntime owner)
{
    ArgumentNullException.ThrowIfNull(owner);
    _dispatch = (command, cancellationToken) => owner.PublishAsync(
        new GameSessionKey(command.OwnerKey, command.SessionId, command.Generation),
        command,
        cancellationToken);
}

internal static void BindOwned(RpcServiceRegistry registry, IReliablePushRuntime owner)
{
    new ClientNotificationCommandBinder(owner).Bind(registry);
}
```

In the RPC handler, call `_dispatch(dto.Command, cancellationToken)`. The owner runtime overwrites caller-provided metadata with metadata generated from its own outbox record.

- [ ] **Step 4: Bind the framework cluster endpoint to owner publication**

Replace the local-dispatch binding in `LakonaClusterRpcServerConfigurator` with:

```csharp
if (context.Services.GetService(typeof(IReliablePushRuntime)) is IReliablePushRuntime reliablePush)
{
    ClientNotificationCommandBinder.BindOwned(context.Builder.ServiceRegistry, reliablePush);
}
```

Add the `Lakona.Game.Server.ReliablePush` using. Do not register both handlers for the same service/method.

- [ ] **Step 5: Run focused owner tests and verify GREEN**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Gateway_assigns_one_sequence_stream|FullyQualifiedName~LocalCommandDispatcherUsesGeneratedDispatchTargetWithMetadata"
```

Expected: both tests pass; the raw dispatcher metadata unit test remains valid and the real cluster path produces sequences 1 and 2.

- [ ] **Step 6: Commit owner-side cluster publication**

```powershell
git add src/Lakona.Game.Server/Sessions/ClientNotificationCommandBinder.cs src/Lakona.Game.Server/Hosting/LakonaClusterRpcServerConfigurator.cs tests/Lakona.Game.Server.Tests/ClientNotificationOwnerIntegrationTests.cs
git commit -m "Own reliable push at the session gateway"
```

### Task 4: Preserve Registration, Disabled Mode, Replay, And Sample Timer Regressions

**Files:**
- Modify: `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`
- Modify: `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`

- [ ] **Step 1: Retain the already-observed RED/GREEN dispatcher registration fix**

Keep `RemoveSessionOnlyNotificationDispatcher(services)` immediately before registering `ClusterClientNotificationDispatcher`. Keep the tests `Cluster_endpoint_replaces_session_only_notification_dispatcher` and `Cluster_endpoint_preserves_custom_notification_dispatcher`. The first has already been observed RED with `NoopClientNotificationRemoteDispatcher` and GREEN after targeted removal; do not weaken it into descriptor-only assertions.

- [ ] **Step 2: Retain the real startup timer integration regression**

Keep `MatchmakingStartupTimerAllocatesExpiredPartialBatch` in `DistributedTopologyConfigurationTests`. It must start the real timer scheduler and startup actor, enqueue a ticket older than five seconds, and wait conditionally for room allocation. It proves the matchmaking startup timer is not the remaining fault and protects the five-second partial-batch behavior.

- [ ] **Step 3: Add disabled-owner metadata coverage**

Add to `ClientNotificationRelayTests`:

```csharp
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
```

- [ ] **Step 4: Run all focused suites**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Cluster_endpoint_"
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Disabled_reliable_push_owner"
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore --filter "FullyQualifiedName~MatchmakingStartupTimerAllocatesExpiredPartialBatch"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit the prerequisite regressions**

```powershell
git add src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs tests/Lakona.Game.Server.Tests/ClientNotificationRelayTests.cs samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs
git commit -m "Cover Agar cluster notification prerequisites"
```

### Task 5: Remove Diagnostics And Update Durable Documentation

**Files:**
- Modify: `src/Lakona.Game.Server/Sessions/ClusterClientNotificationDispatcher.cs`
- Modify: `src/Lakona.Game.Server/Sessions/LocalClientNotificationCommandDispatcher.cs`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Services/MatchmakingNotifier.cs`
- Modify: `samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaGame.Callbacks.cs`
- Modify: `samples/Game.Unity.Agar/Client/Assets/Tests/PlayMode/DotArenaThreeNodePlayModeTests.cs`
- Modify: `docs/session.md`
- Modify: `docs/cluster.md`

- [ ] **Step 1: Remove temporary console/client diagnostics**

Remove only the temporary lines introduced during root-cause tracing:

```text
[ClusterClientNotificationDispatcher]
[LocalClientNotificationCommandDispatcher]
[MatchmakingNotificationStatus]
[DotArenaMatchmakingCallback]
```

Restore exception catches to their pre-diagnostic form without swallowing caller cancellation. Do not remove existing structured `ILogger` calls.

- [ ] **Step 2: Restore the real Unity timeout**

In `DotArenaThreeNodePlayModeTests`, restore the KCP endpoint wait from temporary `15f` to `60f`. The assertion remains unchanged.

- [ ] **Step 3: Document the route-owner contract**

Add to `docs/session.md` under “Reliable Push And Resume”:

```text
The current owner of the GameSessionKey route is the only node that assigns
reliable-push sequences, retains pending records, accepts acknowledgements, and
replays records. Remote business nodes relay an unsequenced notification intent
to that owner. If no valid owner route exists, publication returns RouteNotFound
without creating an outbox on the caller. The built-in in-memory outbox is not
migrated when an owner process fails or a session generation moves.
```

Update `docs/cluster.md` with:

```text
local producer -> local route owner -> owner outbox -> callback
remote producer -> cluster intent -> route owner outbox -> callback
```

State explicitly that cluster commands do not carry authoritative reliable metadata.

- [ ] **Step 4: Run hygiene scans**

```powershell
rg -n "MatchmakingNotificationStatus|DotArenaMatchmakingCallback|LocalClientNotificationCommandDispatcher\]|ClusterClientNotificationDispatcher\]" src samples tests
git diff --check
```

Expected: `rg` has no matches; `git diff --check` exits 0.

- [ ] **Step 5: Commit cleanup and durable docs**

```powershell
git add src/Lakona.Game.Server/Sessions/ClusterClientNotificationDispatcher.cs src/Lakona.Game.Server/Sessions/LocalClientNotificationCommandDispatcher.cs samples/Game.Unity.Agar/Server/Hotfix/Services/MatchmakingNotifier.cs samples/Game.Unity.Agar/Client/Assets/Scripts/Gameplay/DotArenaGame.Callbacks.cs samples/Game.Unity.Agar/Client/Assets/Tests/PlayMode/DotArenaThreeNodePlayModeTests.cs docs/session.md docs/cluster.md
git commit -m "Document gateway-owned reliable push"
```

### Task 6: Run Standard Verification

**Files:**
- Verify: `tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj`
- Verify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`
- Verify: `docs/**`
- Verify: package version graph

- [ ] **Step 1: Run full affected tests**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore
```

Expected: both projects pass with zero failures.

- [ ] **Step 2: Run repository consistency checks**

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
git diff --check
```

Expected: every command exits 0. Confirm `src/Lakona.Game.Server/Lakona.Game.Server.csproj` remains `0.12.0`; do not bump it again within the same unreleased release.

### Task 7: Run The Dedicated Three-Node Unity Acceptance Test

**Files:**
- Verify: `scripts/game/ci/test-agar-three-node.ps1`
- Verify: `.tmp/agar-three-node/TestResults.xml`

- [ ] **Step 1: Run the real E2E script**

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1 -TimeoutSeconds 900
```

Expected: Docker Compose starts all three nodes; Unity receives the KCP endpoint, attaches realtime, enters the match, receives world state, and moves the local player.

- [ ] **Step 2: Audit the result XML**

```powershell
$cases = Select-Xml -Path .tmp/agar-three-node/TestResults.xml -XPath '//test-case'
if ($cases.Count -ne 1) { throw "Expected exactly one Unity test case, found $($cases.Count)." }
if ($cases[0].Node.GetAttribute('result') -ne 'Passed') { throw "Unity test case did not pass." }
```

Expected: exactly one `<test-case result="Passed">`.

- [ ] **Step 3: Inspect final worktree scope**

```powershell
git status --short
git diff --stat HEAD
git diff --check HEAD
```

Expected: only intended framework, tests, docs, and Agar files are present. Never stage `samples/Game.Unity.Agar/docker-compose.yml`; its debug-watcher lines are user-owned changes.

### Task 8: Final Integration Commit And Strong Review

**Files:**
- Delete: `docs/superpowers/plans/2026-07-11-gateway-owned-reliable-push.md`
- Delete: `docs/superpowers/specs/2026-07-10-gateway-owned-reliable-push-design.md`
- Review: complete diff from `d1bcc562` through final head, emphasizing the reliable-push commits

- [ ] **Step 1: Remove completed temporary docs after durable docs are authoritative**

Verify `docs/session.md` and `docs/cluster.md` capture ownership, route failure, queue loss, ACK, and replay semantics. Remove the completed plan and spec with `apply_patch`; do not use checkout/reset commands.

- [ ] **Step 2: Stage only intended changes and inspect them**

```powershell
git add -- docs/superpowers/plans/2026-07-11-gateway-owned-reliable-push.md docs/superpowers/specs/2026-07-10-gateway-owned-reliable-push-design.md
git diff --cached --check
git diff --cached --name-only
```

Expected: the user-owned Compose file and `.tmp/**` artifacts are absent.

- [ ] **Step 3: Commit the final integration state**

```powershell
git commit -m "Fix gateway-owned reliable push sequencing"
```

If task commits already contain every implementation file, this commit contains only temporary-document cleanup. Do not create an empty commit.

- [ ] **Step 4: Dispatch the requested strongest available reviewer subagent after commit**

Use this review brief:

```text
Review base d1bcc562 through current HEAD. Requirements: the session route owner
must be the only reliable-push sequence/outbox/ACK/replay authority; remote
nodes relay unsequenced intents; missing routes create no caller-side record;
reliable-disabled mode remains best effort; Startup Actor implementation stays
paused. Verify TDD coverage, DI cycles, cancellation, stale generations,
metadata trust, public API impact, package versioning, diagnostic cleanup, and
the audited three-node Unity result. Report findings by severity with exact
files/lines; say Approved only if no actionable findings remain.
```

- [ ] **Step 5: Address review findings rigorously**

For each finding, reproduce or prove it, add a failing test when behavior changes, implement the smallest root fix, rerun focused and affected full tests, commit, and ask the same reviewer to re-review. Finish only after an `Approved` result and a clean final status excluding the preserved user Compose change.
