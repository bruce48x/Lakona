# Hotfix Feature Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move feature-addressed remote command handling into owning `HotfixGameFeature` classes with constructor-DI instance methods and remove user-authored hotfix-side `IFeatureMessageHandler` implementations from samples and generated projects.

**Architecture:** Hotfix feature declaration becomes static metadata collection through `HotfixFeatureContext`, while command invocation activates a fresh feature instance from the current hotfix provider. Stable cluster RPC still receives `FeatureMessageRequest`, but the default `IFeatureMessageHandler` parses numeric command ids, serializes/deserializes typed DTOs, and delegates execution to the current hotfix feature command invoker. Actor mailboxes remain separate; feature commands orchestrate capability-level placement and then call actors through generated actor refs.

**Tech Stack:** .NET 10, C#, Microsoft.Extensions.DependencyInjection, Lakona.Game.Cluster, Lakona.Game.Server, Lakona.Game.Server.Hotfix, xUnit, System.Text.Json.

---

## Workspace Safety

- A pre-existing user change is present at `samples/Game.Unity.Agar/Client/Assets/TextMesh Pro/Resources/Fonts & Materials/DotArenaCJK SDF.asset`. Do not modify, format, stage, or commit that file while executing this plan.
- `docs/superpowers/**` is temporary agent workspace. Keep this plan while the work is active. Move durable architecture rules into `docs/**` during Task 10 before cleanup.
- Feature command DTOs may be hotfix-owned types. Do not add them to `HotfixDispatchBoundaryValidator`; that validator protects stable RPC/service method boundaries, not hotfix-to-hotfix feature command payloads.
- When touching C# `ValueTask`, use `return default;`, `return new ValueTask<T>(value);`, or `async ValueTask<T>`. Do not add `ValueTask.CompletedTask` or `ValueTask.FromResult`.

## File Structure

### Cluster Messaging

- Modify `src/Lakona.Game.Cluster/Messaging/FeatureCommandId.cs`: add invariant numeric `TryParse`.
- Modify `src/Lakona.Game.Cluster/Messaging/FeatureMessageRequest.cs`: normalize null or blank `Kind` so wire-level invalid typed command ids can reach the handler and return `Rejected`.
- Modify `src/Lakona.Game.Cluster/Messaging/IFeatureMessageBus.cs`: add node-pinned send support.
- Modify `src/Lakona.Game.Cluster/Messaging/FeatureMessageBus.cs`: implement node-pinned sends with the same serializer, source-node, TTL, status mapping, and transport rules as feature-selected sends.

### Server Feature Client

- Modify `src/Lakona.Game.Server/Features/IFeatureCommandClient.cs`: add `SendToNodeAsync<TRequest, TReply>`.
- Modify `src/Lakona.Game.Server/Features/FeatureCommandClient.cs`: route both feature-selected and node-pinned commands through `IFeatureMessageBus`.
- Create `src/Lakona.Game.Server/Features/RpcFeatureMessageSerializer.cs`: adapt `IRpcSerializer` to `IFeatureMessageSerializer`.
- Create `src/Lakona.Game.Server/Features/FeatureMessageSerializerInvoker.cs`: internal reflection helper for non-generic handler serialization and deserialization.
- Modify `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`: register `IFeatureMessageSerializer` and `IFeatureMessageBus` when cluster endpoint is configured.

### Hotfix Authoring API

- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureCommandCall.cs`: per-request command context.
- Modify `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixGameFeature.cs`: make it a marker base.
- Modify `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureContext.cs`: add `Discoverable` and `Metadata`.
- Keep `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureCommandDeclaration.cs`: retain request type, reply type, command id, and method name.

### Hotfix Runtime Dispatch

- Create `src/Lakona.Game.Server.Hotfix/HotfixFeatureCommandDescriptor.cs`: public immutable descriptor returned by command resolution.
- Create `src/Lakona.Game.Server.Hotfix/IHotfixFeatureCommandInvoker.cs`: stable callable surface used by `Lakona.Game.Server`.
- Create `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixFeatureCommandBinding.cs`: internal dispatch binding with feature type, command types, method info, and key.
- Create `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixFeatureCommandInvoker.cs`: invoker facade over the current `HotfixDispatchTable`.
- Modify `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchTable.cs`: build command bindings from feature declarations, validate method shape, validate constructor activation, invoke commands, and dispose feature instances.
- Modify `src/Lakona.Game.Server.Hotfix/IHotfixRuntimeAccessor.cs`: add `HotfixRuntimeSnapshot.FeatureCommands` while preserving the existing two-argument constructor.
- Modify `src/Lakona.Game.Server.Hotfix/HotfixManager.cs`: publish feature command invoker with each hotfix runtime snapshot.

### Stable Handler

- Modify `src/Lakona.Game.Server/Hotfix/HotfixFeatureMessageHandler.cs`: remove fan-out through hotfix `IEnumerable<IFeatureMessageHandler>` and dispatch typed feature commands through `HotfixRuntimeSnapshot.FeatureCommands`.
- Keep `src/Lakona.Game.Cluster.Rpc/Messaging/FeatureMessageBinder.cs`: it still performs cluster RPC binding and expiration checks. Task 1 adds binder coverage for null wire `Kind` flowing to typed rejection.

### Samples And Templates

- Modify `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeFeature.cs`: move allocation command handling into the feature class with constructor DI and static `Configure`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeRoomAllocation.cs`: keep command DTOs/constants, add `[FeatureCommand]`, remove `BattleRuntimeFeatureMessageHandler`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Features/StateStoreFeatures.cs`: move ensure-user and ensure-leaderboard command handling into `StateStoreFeature`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Services/StateStoreUserActorPlacement.cs`: replace string `Kind` constants with numeric command ids and typed reply DTO.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj`: add MemoryPack generator/package references for hotfix-owned command DTOs.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Services/LoginService.cs`: use `IFeatureCommandClient.SendToNodeAsync`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Services/PlayerService.cs`: use `IFeatureCommandClient.SendToNodeAsync`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingBehavior.cs`: use `IFeatureCommandClient.SendToNodeAsync` for battle-runtime allocation.
- Modify `samples/Game.Godot.Chat/Server/Hotfix/Features/ChatFeature.cs`: switch to static `Configure`.
- Modify `samples/Game.Godot.Chat/Server/App/BuildTag.props`: bump `LakonaHotfixBuildTag` to `20260629.001`.
- Modify `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`: render static feature `Configure`.
- Modify `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`: render `LakonaHotfixBuildTag` as `20260629.001`.

### Tests

- Modify `tests/Lakona.Game.Cluster.Tests/FeatureMessageBusTests.cs`.
- Modify `tests/Lakona.Game.Server.Tests/Features/FeatureCommandClientTests.cs`.
- Modify `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`.
- Add `tests/Lakona.Game.Server.Tests/HotfixFeatureMessageHandlerTests.cs`.
- Modify `tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs`: replace hotfix handler fan-out expectations with stable handler replacement expectations.
- Modify `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureScannerTests.cs`.
- Modify `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs`.
- Modify `tests/Lakona.Game.Server.Hotfix.Tests/HotfixManagerTests.cs`.
- Modify `tests/Lakona.Tool.Tests/Rendering/HotfixRendererTests.cs`.
- Modify `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs`.
- Modify Agar business logic tests under `samples/Game.Unity.Agar/tests/BusinessLogic.Tests` only where compile failures or remote allocation tests require the new typed client path.

### Docs And Versions

- Modify `docs/cluster.md`: typed feature command wire semantics, numeric `Kind`, node-pinned client API.
- Modify `docs/hotfix/architecture.md`: static feature declaration, command invocation lifecycle, BuildTag reason.
- Modify `docs/hotfix/actor-behavior.md`: distinction between feature commands and actor calls.
- Modify `docs/configuration.md`: ordinary projects should not register hotfix-side `IFeatureMessageHandler`.
- Modify `src/Lakona.Game.Cluster/Lakona.Game.Cluster.csproj`: bump `0.3.1` to `0.3.2`.
- Modify `src/Lakona.Game.Cluster/Diagnostics/ClusterDiagnostics.cs`: bump diagnostic source/meter version to `0.3.2`.
- Modify `src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj`: bump `0.2.3` to `0.2.4`.
- Modify `src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj`: bump `0.3.5` to `0.3.6`.
- Modify `src/Lakona.Game.Server/Lakona.Game.Server.csproj`: bump `0.8.18` to `0.8.19`.
- Modify `src/Lakona.Tool/Lakona.Tool.csproj`: bump `0.14.7` to `0.14.8`.

---

## Task 1: Cluster Wire Helpers And Node-Pinned Bus

**Files:**
- Modify: `src/Lakona.Game.Cluster/Messaging/FeatureCommandId.cs`
- Modify: `src/Lakona.Game.Cluster/Messaging/FeatureMessageRequest.cs`
- Modify: `src/Lakona.Game.Cluster/Messaging/IFeatureMessageBus.cs`
- Modify: `src/Lakona.Game.Cluster/Messaging/FeatureMessageBus.cs`
- Test: `tests/Lakona.Game.Cluster.Tests/FeatureMessageBusTests.cs`
- Test: `tests/Lakona.Game.Cluster.Rpc.Tests/FeatureMessageTransportTests.cs`

- [ ] **Step 1: Write failing parser and blank-kind tests**

Add these tests to `FeatureMessageBusTests`:

```csharp
[Theory]
[InlineData("")]
[InlineData(" ")]
[InlineData("abc")]
[InlineData("0")]
[InlineData("-1")]
[InlineData("2147483648")]
public void FeatureCommandIdTryParseRejectsInvalidWireValues(string value)
{
    Assert.False(FeatureCommandId.TryParse(value, out var commandId));
    Assert.Equal(default, commandId);
}

[Fact]
public void FeatureCommandIdTryParseAcceptsInvariantDecimalWireValue()
{
    Assert.True(FeatureCommandId.TryParse("42", out var commandId));
    Assert.Equal(42, commandId.Value);
}

[Fact]
public void FeatureMessageRequestAllowsBlankKindForWireLevelTypedRejection()
{
    var request = new FeatureMessageRequest(
        new FeatureName("battle-runtime"),
        "",
        ReadOnlyMemory<byte>.Empty,
        DateTimeOffset.UtcNow.AddMinutes(1),
        new NodeId("data-1"),
        "corr-1");

    Assert.Equal("", request.Kind);
}

[Fact]
public void FeatureMessageRequestNormalizesNullKindForWireLevelTypedRejection()
{
    var request = new FeatureMessageRequest(
        new FeatureName("battle-runtime"),
        null!,
        ReadOnlyMemory<byte>.Empty,
        DateTimeOffset.UtcNow.AddMinutes(1),
        new NodeId("data-1"),
        "corr-1");

    Assert.Equal("", request.Kind);
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Cluster.Tests\Lakona.Game.Cluster.Tests.csproj --no-restore --filter "FeatureCommandIdTryParseRejectsInvalidWireValues|FeatureCommandIdTryParseAcceptsInvariantDecimalWireValue|FeatureMessageRequestAllowsBlankKindForWireLevelTypedRejection|FeatureMessageRequestNormalizesNullKindForWireLevelTypedRejection"
```

Expected: fail because `FeatureCommandId.TryParse` does not exist and `FeatureMessageRequest` rejects blank or null `Kind`.

- [ ] **Step 3: Implement numeric command id parsing**

Modify `FeatureCommandId.cs`:

```csharp
using System;
using System.Globalization;

namespace Lakona.Game.Cluster;

public readonly struct FeatureCommandId : IEquatable<FeatureCommandId>
{
    public FeatureCommandId(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Feature command id must be positive.");
        }

        Value = value;
    }

    public int Value { get; }

    public static FeatureCommandId From(int value)
    {
        return new FeatureCommandId(value);
    }

    public static bool TryParse(string? value, out FeatureCommandId commandId)
    {
        commandId = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed <= 0)
        {
            return false;
        }

        commandId = new FeatureCommandId(parsed);
        return true;
    }

    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }

    public bool Equals(FeatureCommandId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is FeatureCommandId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value;
    }

    public static bool operator ==(FeatureCommandId left, FeatureCommandId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FeatureCommandId left, FeatureCommandId right)
    {
        return !left.Equals(right);
    }
}
```

- [ ] **Step 4: Normalize blank and null wire kind at the message object boundary**

Remove the existing `string.IsNullOrWhiteSpace(kind)` rejection in `FeatureMessageRequest.cs`. Keep `kind` as a required constructor argument by convention, but normalize the runtime value before storing it:

```csharp
Kind = kind ?? string.Empty;
```

Keep the existing feature, source node, and correlation id validation. Do not reject blank `Kind` in the message object; typed-command admission happens in `HotfixFeatureMessageHandler`.

- [ ] **Step 5: Add RPC binder coverage for null feature kind**

Add this test to `FeatureMessageTransportTests`:

```csharp
[Fact]
public async Task BinderConvertsNullFeatureKindToBlankForTypedRejection()
{
    var registry = new RpcServiceRegistry();
    var handler = new InvalidKindRejectingFeatureHandler();
    FeatureMessageBinder.Bind(registry, handler);
    Assert.True(registry.TryGetHandler(
        ClusterProtocol.ServiceId,
        ClusterProtocol.FeatureMessageMethodId,
        out var rpcHandler));

    var serializer = new JsonTestSerializer();
    await using var session = new RpcSession(new FakeTransport(), serializer);
    using var payload = serializer.SerializeFrame(new FeatureSendRequest
    {
        Feature = "matchmaking",
        Kind = null!,
        Payload = Array.Empty<byte>(),
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
        SourceNode = "gateway-1",
        CorrelationId = "corr-1"
    });
    using var frame = await rpcHandler!(
        session,
        new RpcRequestFrame(
            1,
            ClusterProtocol.ServiceId,
            ClusterProtocol.FeatureMessageMethodId,
            payload),
        TestContext.Current.CancellationToken);

    using var response = RpcEnvelopeCodec.DecodeResponse(frame);
    var reply = serializer.Deserialize<FeatureSendReply>(response.Payload.Memory);
    var dispatched = Assert.Single(handler.Requests);
    Assert.Equal(RpcStatus.Ok, response.Status);
    Assert.Equal(ClusterSendStatus.Rejected, (ClusterSendStatus)reply.Status);
    Assert.Equal("", dispatched.Kind);
}

private sealed class InvalidKindRejectingFeatureHandler : IFeatureMessageHandler
{
    public List<FeatureMessageRequest> Requests { get; } = new();

    public ValueTask<FeatureMessageReply> HandleAsync(
        FeatureMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        var status = string.IsNullOrWhiteSpace(request.Kind)
            ? ClusterSendStatus.Rejected
            : ClusterSendStatus.Accepted;
        return new ValueTask<FeatureMessageReply>(
            new FeatureMessageReply(status, ReadOnlyMemory<byte>.Empty));
    }
}
```

- [ ] **Step 6: Run RPC binder null-kind test**

Run:

```powershell
dotnet test tests\Lakona.Game.Cluster.Rpc.Tests\Lakona.Game.Cluster.Rpc.Tests.csproj --no-restore --filter "BinderConvertsNullFeatureKindToBlankForTypedRejection"
```

Expected: passes after `FeatureMessageRequest` normalizes null `Kind` to `""`.

- [ ] **Step 7: Add node-pinned bus tests**

Add this test to `FeatureMessageBusTests`:

```csharp
[Fact]
public async Task SendToNodeUsesExplicitTargetAndDoesNotQueryDiscovery()
{
    var transport = new CapturingTransport();
    var target = new ClusterNodeDescriptor(
        new NodeId("runtime-7"),
        NodeState.Ready,
        new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
        {
            ["cluster"] = new NodeEndpoint("tcp://10.0.0.7:21001")
        },
        [new NodeFeatureDescriptor("battle-runtime")]);
    var bus = new FeatureMessageBus(
        new ThrowingDiscovery(),
        transport,
        new TestSerializer(),
        new NodeId("matchmaker-1"),
        TimeSpan.FromSeconds(12),
        () => new DateTimeOffset(2026, 6, 29, 8, 0, 0, TimeSpan.Zero));

    var reply = await bus.SendToNodeAsync<CommandRequest, CommandReply>(
        target,
        new FeatureName("battle-runtime"),
        "17",
        new CommandRequest("room-1"),
        TestContext.Current.CancellationToken);

    Assert.Equal(ClusterSendStatus.Accepted, reply.Status);
    Assert.Equal(new NodeId("runtime-7"), transport.LastNode);
    Assert.Equal("tcp://10.0.0.7:21001", transport.LastEndpoint);
    Assert.Equal("battle-runtime", transport.LastRequest?.Feature.Value);
    Assert.Equal("17", transport.LastRequest?.Kind);
    Assert.Equal(new NodeId("matchmaker-1"), transport.LastRequest?.SourceNode);
    Assert.Equal("room-1", JsonSerializer.Deserialize<CommandRequest>(transport.LastRequest!.Payload.Span)!.RoomId);
}

[Fact]
public async Task SendToNodeReturnsNodeUnavailableWhenExplicitTargetHasNoClusterEndpoint()
{
    var bus = new FeatureMessageBus(
        new ThrowingDiscovery(),
        new ThrowingTransport(),
        new TestSerializer());
    var target = new ClusterNodeDescriptor(
        new NodeId("runtime-7"),
        NodeState.Ready,
        new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal),
        [new NodeFeatureDescriptor("battle-runtime")]);

    var reply = await bus.SendToNodeAsync<CommandRequest, CommandReply>(
        target,
        new FeatureName("battle-runtime"),
        "17",
        new CommandRequest("room-1"),
        TestContext.Current.CancellationToken);

    Assert.Equal(ClusterSendStatus.NodeUnavailable, reply.Status);
}

private sealed class ThrowingDiscovery : IClusterNodeDiscovery
{
    public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
        FeatureName feature,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Discovery should not be queried for node-pinned sends.");
    }

    public ValueTask<ClusterNodeDescriptor?> AnyAsync(
        FeatureName feature,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Discovery should not be queried for node-pinned sends.");
    }
}

private sealed record CommandRequest(string RoomId);

private sealed record CommandReply(string Status);
```

While editing the same file, replace any touched helper returns using `ValueTask.FromResult(...)` with `new ValueTask<T>(...)`.

- [ ] **Step 8: Run node-pinned bus tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Cluster.Tests\Lakona.Game.Cluster.Tests.csproj --no-restore --filter "SendToNodeUsesExplicitTargetAndDoesNotQueryDiscovery|SendToNodeReturnsNodeUnavailableWhenExplicitTargetHasNoClusterEndpoint"
```

Expected: fail because `IFeatureMessageBus.SendToNodeAsync` does not exist.

- [ ] **Step 9: Add node-pinned bus API**

Modify `IFeatureMessageBus.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IFeatureMessageBus
    {
        ValueTask<FeatureMessageReply> SendToFeatureAsync<TRequest, TReply>(
            FeatureName feature,
            TRequest request,
            CancellationToken cancellationToken = default);

        ValueTask<FeatureMessageReply> SendToFeatureAsync<TRequest, TReply>(
            FeatureName feature,
            string kind,
            TRequest request,
            CancellationToken cancellationToken = default);

        ValueTask<FeatureMessageReply> SendToNodeAsync<TRequest, TReply>(
            ClusterNodeDescriptor target,
            FeatureName feature,
            string kind,
            TRequest request,
            CancellationToken cancellationToken = default);
    }
}
```

- [ ] **Step 10: Implement node-pinned bus dispatch**

Refactor `FeatureMessageBus.SendToFeatureAsync` so discovery selects a target, then both paths share this helper:

```csharp
public async ValueTask<FeatureMessageReply> SendToNodeAsync<TRequest, TReply>(
    ClusterNodeDescriptor target,
    FeatureName feature,
    string kind,
    TRequest request,
    CancellationToken cancellationToken = default)
{
    if (target is null)
    {
        throw new ArgumentNullException(nameof(target));
    }

    return await SendToReadyTargetAsync<TRequest, TReply>(
        target,
        feature,
        kind,
        request,
        cancellationToken).ConfigureAwait(false);
}

private async ValueTask<FeatureMessageReply> SendToReadyTargetAsync<TRequest, TReply>(
    ClusterNodeDescriptor target,
    FeatureName feature,
    string kind,
    TRequest request,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();

    if (target.State != NodeState.Ready || !target.Endpoints.ContainsKey("cluster"))
    {
        return new FeatureMessageReply(ClusterSendStatus.NodeUnavailable, Array.Empty<byte>());
    }

    ReadOnlyMemory<byte> payload;
    try
    {
        payload = _serializer.Serialize(request);
    }
    catch (Exception ex)
    {
        return new FeatureMessageReply(
            ClusterSendStatus.SerializationFailed,
            Array.Empty<byte>(),
            ex.Message);
    }

    var now = _utcNow();
    var message = new FeatureMessageRequest(
        feature,
        kind,
        payload,
        now.Add(_requestTtl),
        _sourceNode,
        Guid.NewGuid().ToString("N"));

    try
    {
        return await _transport.SendAsync(target, message, cancellationToken)
            .ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (TimeoutException ex)
    {
        return new FeatureMessageReply(ClusterSendStatus.Timeout, Array.Empty<byte>(), ex.Message);
    }
    catch (Exception ex)
    {
        return new FeatureMessageReply(ClusterSendStatus.Failed, Array.Empty<byte>(), ex.Message);
    }
}
```

Then change the discovered path to:

```csharp
return await SendToReadyTargetAsync<TRequest, TReply>(
    target,
    feature,
    kind,
    request,
    cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 11: Run cluster messaging tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Cluster.Tests\Lakona.Game.Cluster.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Cluster.Rpc.Tests\Lakona.Game.Cluster.Rpc.Tests.csproj --no-restore --filter "FeatureMessageTransportTests"
```

Expected: all tests pass.

- [ ] **Step 12: Commit cluster messaging changes**

Run:

```powershell
git add src\Lakona.Game.Cluster\Messaging\FeatureCommandId.cs src\Lakona.Game.Cluster\Messaging\FeatureMessageRequest.cs src\Lakona.Game.Cluster\Messaging\IFeatureMessageBus.cs src\Lakona.Game.Cluster\Messaging\FeatureMessageBus.cs tests\Lakona.Game.Cluster.Tests\FeatureMessageBusTests.cs tests\Lakona.Game.Cluster.Rpc.Tests\FeatureMessageTransportTests.cs
git commit -m "Add typed feature command wire helpers"
```

Expected: commit succeeds and does not stage the Unity font asset.

---

## Task 2: Typed Feature Command Client And Cluster Endpoint Registrations

**Files:**
- Modify: `src/Lakona.Game.Server/Features/IFeatureCommandClient.cs`
- Modify: `src/Lakona.Game.Server/Features/FeatureCommandClient.cs`
- Create: `src/Lakona.Game.Server/Features/RpcFeatureMessageSerializer.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`
- Test: `tests/Lakona.Game.Server.Tests/Features/FeatureCommandClientTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs`

- [ ] **Step 1: Update feature command client tests for node-pinned sends**

In `FeatureCommandClientTests`, add:

```csharp
[Fact]
public async Task SendToNodeAsyncUsesFeatureCommandIdAsMessageKindAndDeserializesReply()
{
    var serializer = new TestSerializer();
    var bus = new RecordingFeatureMessageBus(serializer);
    var client = new FeatureCommandClient(bus, serializer);
    var target = new ClusterNodeDescriptor(
        new NodeId("runtime-1"),
        NodeState.Ready,
        new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
        {
            ["cluster"] = new NodeEndpoint("tcp://127.0.0.1:21001")
        },
        [new NodeFeatureDescriptor("battle-runtime")]);

    var reply = await client.SendToNodeAsync<JoinRoomCommand, JoinRoomReply>(
        target,
        "battle-runtime",
        new JoinRoomCommand("room-1"),
        TestContext.Current.CancellationToken);

    Assert.Same(target, bus.Target);
    Assert.Equal("battle-runtime", bus.Feature.Value);
    Assert.Equal("17", bus.Kind);
    Assert.Equal("room-1", bus.Request?.RoomId);
    Assert.Equal("joined", reply.Status);
}
```

Extend `RecordingFeatureMessageBus`:

```csharp
public ClusterNodeDescriptor? Target { get; private set; }

public ValueTask<FeatureMessageReply> SendToNodeAsync<TRequest, TReply>(
    ClusterNodeDescriptor target,
    FeatureName feature,
    string kind,
    TRequest request,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    Target = target;
    Feature = feature;
    Kind = kind;
    Request = Assert.IsType<JoinRoomCommand>(request);
    var payload = _serializer.Serialize(new JoinRoomReply("joined"));
    return new ValueTask<FeatureMessageReply>(
        new FeatureMessageReply(ClusterSendStatus.Accepted, payload));
}
```

- [ ] **Step 2: Run client tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FeatureCommandClientTests"
```

Expected: fail because `IFeatureCommandClient.SendToNodeAsync` is missing.

- [ ] **Step 3: Add node-pinned client API**

Modify `IFeatureCommandClient.cs`:

```csharp
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Features;

public interface IFeatureCommandClient
{
    ValueTask<TReply> SendAsync<TRequest, TReply>(
        string featureName,
        TRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<TReply> SendToNodeAsync<TRequest, TReply>(
        ClusterNodeDescriptor target,
        string featureName,
        TRequest request,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement node-pinned client dispatch**

Add this method to `FeatureCommandClient.cs`:

```csharp
public async ValueTask<TReply> SendToNodeAsync<TRequest, TReply>(
    ClusterNodeDescriptor target,
    string featureName,
    TRequest request,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(target);
    ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

    var commandId = GetCommandId<TRequest>();
    var reply = await _messages.SendToNodeAsync<TRequest, TReply>(
        target,
        new FeatureName(featureName),
        commandId.ToString(),
        request,
        cancellationToken).ConfigureAwait(false);

    return reply.GetPayload<TReply>(_serializer);
}
```

- [ ] **Step 5: Write registration tests for serializer and bus**

In `LakonaClusterEndpointServiceCollectionExtensionsTests`, extend `AddLakonaGameClusterEndpoint_registers_feature_message_transport`:

```csharp
Assert.IsType<RpcFeatureMessageTransport>(provider.GetRequiredService<IFeatureMessageTransport>());
Assert.IsType<RpcFeatureMessageSerializer>(provider.GetRequiredService<IFeatureMessageSerializer>());
Assert.IsType<FeatureMessageBus>(provider.GetRequiredService<IFeatureMessageBus>());
```

- [ ] **Step 6: Run registration tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "AddLakonaGameClusterEndpoint_registers_feature_message_transport"
```

Expected: fail because `RpcFeatureMessageSerializer` and `FeatureMessageBus` are not registered.

- [ ] **Step 7: Add RPC-backed feature serializer**

Create `RpcFeatureMessageSerializer.cs`:

```csharp
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Features;

public sealed class RpcFeatureMessageSerializer : IFeatureMessageSerializer
{
    private readonly IRpcSerializer _serializer;

    public RpcFeatureMessageSerializer(IRpcSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public ReadOnlyMemory<byte> Serialize<T>(T value)
    {
        using var frame = _serializer.SerializeFrame(value);
        return frame.Memory.ToArray();
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        return _serializer.Deserialize<T>(payload);
    }
}
```

- [ ] **Step 8: Register serializer and bus in the cluster endpoint**

In `LakonaClusterEndpointServiceCollectionExtensions.cs`, add `using Lakona.Game.Server.Features;` and register after `IRpcSerializer`:

```csharp
services.TryAddSingleton<IFeatureMessageSerializer>(provider =>
    new RpcFeatureMessageSerializer(provider.GetRequiredService<LakonaClusterRpcSerializer>().Serializer));
```

Register the bus after `IFeatureMessageTransport`:

```csharp
services.TryAddSingleton<IFeatureMessageBus>(provider =>
{
    var sourceNode = provider.GetService<LocalActorNodeIdentity>()?.NodeId
        ?? new NodeId(runtimeOptions.Node.Id);
    return new FeatureMessageBus(
        provider.GetRequiredService<IClusterNodeDiscovery>(),
        provider.GetRequiredService<IFeatureMessageTransport>(),
        provider.GetRequiredService<IFeatureMessageSerializer>(),
        sourceNode);
});
```

- [ ] **Step 9: Run server feature tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FeatureCommandClientTests|AddLakonaGameClusterEndpoint_registers_feature_message_transport|AddLakonaGameServer_registers_feature_command_client"
```

Expected: all selected tests pass.

- [ ] **Step 10: Commit client and registration changes**

Run:

```powershell
git add src\Lakona.Game.Server\Features\IFeatureCommandClient.cs src\Lakona.Game.Server\Features\FeatureCommandClient.cs src\Lakona.Game.Server\Features\RpcFeatureMessageSerializer.cs src\Lakona.Game.Server\Hosting\LakonaClusterEndpointServiceCollectionExtensions.cs tests\Lakona.Game.Server.Tests\Features\FeatureCommandClientTests.cs tests\Lakona.Game.Server.Tests\Hosting\LakonaClusterEndpointServiceCollectionExtensionsTests.cs tests\Lakona.Game.Server.Tests\LakonaGameServerTests.cs
git commit -m "Add typed feature command client plumbing"
```

Expected: commit succeeds and does not stage the Unity font asset.

---

## Task 3: Hotfix Feature Authoring API

**Files:**
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureCommandCall.cs`
- Modify: `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixGameFeature.cs`
- Modify: `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureContext.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureScannerTests.cs`

- [ ] **Step 1: Write failing authoring API tests**

Replace the nested feature classes in `HotfixFeatureScannerTests` so they no longer override instance `Configure`. Use this shape:

```csharp
[HotfixFeature("battle-runtime")]
private sealed class BattleRuntimeFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
        context.EnsureLocalActor<MatchmakingActor>("default");
        context.ScheduleActorTick<MatchmakingActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce);
        context.ScheduleActiveActorTicks<RoomActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending);
    }
}

[HotfixFeature("state-store")]
private sealed class ServiceFeature : HotfixGameFeature
{
    public static void Configure(HotfixFeatureContext context)
    {
        context.Discoverable = false;
        context.Metadata["role"] = "state";
        context.Services.AddSingleton<ISampleHotfixService, SampleHotfixService>();
    }
}

[HotfixFeature("commands")]
private sealed class CommandFeature : HotfixGameFeature
{
    public CommandFeature(RequiredRuntimeDependency dependency)
    {
        Dependency = dependency;
    }

    public RequiredRuntimeDependency Dependency { get; }

    public static void Configure(HotfixFeatureContext context)
    {
        context.HandleCommand<StartMatchCommand, StartMatchReply>("ExecuteAsync");
    }

    public ValueTask<StartMatchReply> ExecuteAsync(HotfixFeatureCommandCall<StartMatchCommand> call)
    {
        return new ValueTask<StartMatchReply>(new StartMatchReply(true));
    }
}

private sealed class RequiredRuntimeDependency
{
}
```

Extend `Scanner_captures_hotfix_feature_service_declarations`:

```csharp
Assert.False(feature.Discoverable);
Assert.Equal("state", feature.Metadata["role"]);
```

Add:

```csharp
[Fact]
public void FeatureCommandCallCarriesRequestAndCommandContext()
{
    var services = new ServiceCollection().BuildServiceProvider();
    var commandId = FeatureCommandId.From(17);
    using var cts = new CancellationTokenSource();

    var call = new HotfixFeatureCommandCall<StartMatchCommand>(
        new StartMatchCommand("room-1"),
        "battle-runtime",
        commandId,
        "corr-1",
        new NodeId("data-1"),
        new DateTimeOffset(2026, 6, 29, 8, 0, 0, TimeSpan.Zero),
        cts.Token,
        services);

    Assert.Equal("room-1", call.Request.RoomId);
    Assert.Equal("battle-runtime", call.FeatureName);
    Assert.Equal(commandId, call.CommandId);
    Assert.Equal("corr-1", call.CorrelationId);
    Assert.Equal(new NodeId("data-1"), call.SourceNode);
    Assert.Same(services, call.Services);
    Assert.Equal(cts.Token, call.CancellationToken);
}
```

- [ ] **Step 2: Run scanner tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "HotfixFeatureScannerTests"
```

Expected: fail because `HotfixGameFeature` still requires instance `Configure`, `HotfixFeatureContext` lacks `Discoverable`/`Metadata`, and `HotfixFeatureCommandCall<TRequest>` does not exist.

- [ ] **Step 3: Add feature command call context**

Create `HotfixFeatureCommandCall.cs`:

```csharp
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class HotfixFeatureCommandCall<TRequest> : IHotfixCallContext
{
    public HotfixFeatureCommandCall(
        TRequest request,
        string featureName,
        FeatureCommandId commandId,
        string correlationId,
        NodeId sourceNode,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken,
        IServiceProvider services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(services);

        Request = request;
        FeatureName = featureName;
        CommandId = commandId;
        CorrelationId = correlationId;
        SourceNode = sourceNode;
        ExpiresAt = expiresAt;
        CancellationToken = cancellationToken;
        Services = services;
    }

    public TRequest Request { get; }

    public string FeatureName { get; }

    public FeatureCommandId CommandId { get; }

    public string CorrelationId { get; }

    public NodeId SourceNode { get; }

    public DateTimeOffset ExpiresAt { get; }

    public CancellationToken CancellationToken { get; }

    public IServiceProvider Services { get; }
}
```

- [ ] **Step 4: Make `HotfixGameFeature` a marker base**

Replace `HotfixGameFeature.cs` with:

```csharp
namespace Lakona.Game.Server.Hotfix.Abstractions;

public abstract class HotfixGameFeature
{
}
```

- [ ] **Step 5: Add declaration state to `HotfixFeatureContext`**

Add fields and properties:

```csharp
private readonly Dictionary<string, string> _metadata = new(StringComparer.Ordinal);

public bool Discoverable { get; set; } = true;

public IDictionary<string, string> Metadata => _metadata;
```

Keep `Services`, `EnsureLocalActor`, `HandleCommand`, and tick APIs unchanged.

- [ ] **Step 6: Run authoring API tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "FeatureCommandCallCarriesRequestAndCommandContext"
```

Expected: `FeatureCommandCallCarriesRequestAndCommandContext` passes. Scanner tests still fail until Task 4 changes scanning.

- [ ] **Step 7: Continue directly to scanner implementation**

Do not commit after Task 3. The authoring API change intentionally leaves scanner tests red until Task 4 replaces instance feature scanning. Keep the local changes and proceed directly to Task 4 so the next commit is a passing API-plus-scanner slice.

---

## Task 4: Static Feature Configure Scanner

**Files:**
- Modify: `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanner.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureScannerTests.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixManagerTests.cs`

- [ ] **Step 1: Add scanner rejection tests**

Add to `HotfixFeatureScannerTests`:

```csharp
[Fact]
public void Scanner_rejects_feature_without_static_configure()
{
    var result = HotfixBehaviorScanner.Scan(typeof(MissingConfigureFeature).Assembly, [
        typeof(MissingConfigureFeature)
    ]);

    Assert.False(result.Succeeded);
    Assert.Contains(result.Diagnostics, diagnostic =>
        diagnostic.Contains("public static Configure", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void Scanner_rejects_old_instance_configure()
{
    var result = HotfixBehaviorScanner.Scan(typeof(OldInstanceConfigureFeature).Assembly, [
        typeof(OldInstanceConfigureFeature)
    ]);

    Assert.False(result.Succeeded);
    Assert.Contains(result.Diagnostics, diagnostic =>
        diagnostic.Contains("must use public static Configure", StringComparison.OrdinalIgnoreCase));
}

[HotfixFeature("missing-configure")]
private sealed class MissingConfigureFeature : HotfixGameFeature
{
}

[HotfixFeature("old-configure")]
private sealed class OldInstanceConfigureFeature : HotfixGameFeature
{
    public void Configure(HotfixFeatureContext context)
    {
        context.EnsureLocalActor<MatchmakingActor>("default");
    }
}
```

- [ ] **Step 2: Run scanner tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "HotfixFeatureScannerTests"
```

Expected: fail because scanner still requires a public parameterless constructor and invokes instance `Configure`.

- [ ] **Step 3: Replace feature scanning lifecycle**

Replace `ScanFeatureType` in `HotfixBehaviorScanner.cs` with this shape:

```csharp
private static void ScanFeatureType(
    Type featureType,
    HotfixFeatureAttribute attribute,
    List<HotfixFeatureDeclaration> features,
    List<string> diagnostics,
    HashSet<string> featureNames)
{
    if (!typeof(HotfixGameFeature).IsAssignableFrom(featureType))
    {
        diagnostics.Add($"Hotfix feature '{featureType.FullName}' must inherit {typeof(HotfixGameFeature).FullName}.");
        return;
    }

    if (featureType.IsAbstract || featureType.IsInterface)
    {
        diagnostics.Add($"Hotfix feature '{featureType.FullName}' must be a concrete class.");
        return;
    }

    var instanceConfigure = featureType.GetMethod(
        "Configure",
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
        binder: null,
        types: [typeof(HotfixFeatureContext)],
        modifiers: null);
    if (instanceConfigure is not null)
    {
        diagnostics.Add($"Hotfix feature '{featureType.FullName}' must use public static Configure(HotfixFeatureContext context), not instance Configure.");
        return;
    }

    var configure = featureType.GetMethod(
        "Configure",
        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
        binder: null,
        types: [typeof(HotfixFeatureContext)],
        modifiers: null);
    if (configure is null)
    {
        diagnostics.Add($"Hotfix feature '{featureType.FullName}' must declare public static Configure(HotfixFeatureContext context).");
        return;
    }

    if (!featureNames.Add(attribute.Name))
    {
        diagnostics.Add($"Duplicate hotfix feature name '{attribute.Name}'.");
        return;
    }

    var context = new HotfixFeatureContext();
    try
    {
        configure.Invoke(null, [context]);
    }
    catch (TargetInvocationException ex) when (ex.InnerException is not null)
    {
        diagnostics.Add($"Hotfix feature '{featureType.FullName}' Configure failed: {ex.InnerException.Message}");
        return;
    }
    catch (Exception ex)
    {
        diagnostics.Add($"Hotfix feature '{featureType.FullName}' Configure failed: {ex.Message}");
        return;
    }

    features.Add(new HotfixFeatureDeclaration(
        attribute.Name,
        featureType,
        context.Discoverable,
        new Dictionary<string, string>(context.Metadata, StringComparer.Ordinal),
        context.LocalActors.ToArray(),
        context.ActorTicks.ToArray(),
        context.Commands.ToArray(),
        context.Services.ToArray()));
}
```

Keep `using System.Reflection;` at the top.

- [ ] **Step 4: Update dynamic hotfix fixture sources to static configure**

In `HotfixManagerTests.cs`, replace every source-string feature declaration:

```csharp
public override void Configure(HotfixFeatureContext context)
```

with:

```csharp
public static void Configure(HotfixFeatureContext context)
```

Run this search until it returns no results:

```powershell
rg -n "override void Configure\\(HotfixFeatureContext" tests\Lakona.Game.Server.Hotfix.Tests\HotfixManagerTests.cs
```

Expected: no matches.

- [ ] **Step 5: Run hotfix scanner and manager tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "HotfixFeatureScannerTests|Reload_uses_activator_utilities_constructor_attribute|Reload_fails_before_publish_when_service_has_multiple_unmarked_constructors"
```

Expected: selected tests pass.

- [ ] **Step 6: Commit authoring API and static scanner changes**

Run:

```powershell
git add src\Lakona.Game.Server.Hotfix.Abstractions\Features\HotfixFeatureCommandCall.cs src\Lakona.Game.Server.Hotfix.Abstractions\Features\HotfixGameFeature.cs src\Lakona.Game.Server.Hotfix.Abstractions\Features\HotfixFeatureContext.cs src\Lakona.Game.Server.Hotfix\Scanning\HotfixBehaviorScanner.cs tests\Lakona.Game.Server.Hotfix.Tests\HotfixFeatureScannerTests.cs tests\Lakona.Game.Server.Hotfix.Tests\HotfixManagerTests.cs
git commit -m "Scan hotfix features through static configure"
```

Expected: commit succeeds.

---

## Task 5: Hotfix Feature Command Dispatch Table

**Files:**
- Create: `src/Lakona.Game.Server.Hotfix/HotfixFeatureCommandDescriptor.cs`
- Create: `src/Lakona.Game.Server.Hotfix/IHotfixFeatureCommandInvoker.cs`
- Create: `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixFeatureCommandBinding.cs`
- Create: `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixFeatureCommandInvoker.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchTable.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs`

- [ ] **Step 1: Write failing dispatch table tests**

Add tests to `HotfixDispatchTests`:

```csharp
[Fact]
public async Task FeatureCommandDispatchActivatesFeatureWithConstructorDiAndDisposesAfterAwait()
{
    var services = new ServiceCollection()
        .AddSingleton(new FeatureDependency("runtime"))
        .BuildServiceProvider();
    var table = new HotfixDispatchTable(
        1,
        Array.Empty<HotfixMethodBinding>(),
        Array.Empty<HotfixServiceMethodBinding>(),
        [CreateFeatureDeclaration(typeof(DispatchFeature), "ExecuteAsync")]);
    var invoker = new HotfixFeatureCommandInvoker(table);

    Assert.True(invoker.TryResolve("commands", FeatureCommandId.From(101), out var descriptor));
    var reply = await invoker.InvokeAsync(
        descriptor,
        new DispatchCommand("room-1"),
        NewFeatureMessage("commands", "101"),
        services,
        TestContext.Current.CancellationToken);

    var typed = Assert.IsType<DispatchReply>(reply);
    Assert.Equal("runtime:room-1", typed.Value);
    Assert.Equal(1, DispatchFeature.DisposeCount);
}

[Fact]
public void FeatureCommandDispatchRejectsDuplicateFeatureCommandIds()
{
    var exception = Assert.Throws<InvalidOperationException>(() => new HotfixDispatchTable(
        1,
        Array.Empty<HotfixMethodBinding>(),
        Array.Empty<HotfixServiceMethodBinding>(),
        [
            CreateFeatureDeclaration(typeof(DispatchFeature), "ExecuteAsync"),
            CreateFeatureDeclaration(typeof(DispatchFeature), "ExecuteAsync")
        ]));

    Assert.Contains("Duplicate hotfix feature command", exception.Message);
}

[Fact]
public void FeatureCommandDispatchValidatesMethodShape()
{
    var exception = Assert.Throws<InvalidOperationException>(() => new HotfixDispatchTable(
        1,
        Array.Empty<HotfixMethodBinding>(),
        Array.Empty<HotfixServiceMethodBinding>(),
        [CreateFeatureDeclaration(typeof(InvalidDispatchFeature), "ExecuteAsync")]));

    Assert.Contains("Hotfix feature command", exception.Message);
    Assert.Contains("ValueTask", exception.Message);
}

private static HotfixFeatureDeclaration CreateFeatureDeclaration(Type featureType, string methodName)
{
    return new HotfixFeatureDeclaration(
        "commands",
        featureType,
        true,
        new Dictionary<string, string>(StringComparer.Ordinal),
        Array.Empty<HotfixLocalActorDeclaration>(),
        Array.Empty<HotfixActorTickDeclaration>(),
        [new HotfixFeatureCommandDeclaration(typeof(DispatchCommand), typeof(DispatchReply), 101, methodName)],
        Array.Empty<ServiceDescriptor>());
}

private static FeatureMessageRequest NewFeatureMessage(string feature, string kind)
{
    return new FeatureMessageRequest(
        new FeatureName(feature),
        kind,
        ReadOnlyMemory<byte>.Empty,
        DateTimeOffset.UtcNow.AddMinutes(1),
        new NodeId("data-1"),
        "corr-1");
}

private sealed class FeatureDependency
{
    public FeatureDependency(string value)
    {
        Value = value;
    }

    public string Value { get; }
}

private sealed class DispatchFeature : HotfixGameFeature, IDisposable
{
    private readonly FeatureDependency _dependency;

    public DispatchFeature(FeatureDependency dependency)
    {
        _dependency = dependency;
    }

    public static int DisposeCount { get; private set; }

    public ValueTask<DispatchReply> ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        return new ValueTask<DispatchReply>(new DispatchReply($"{_dependency.Value}:{call.Request.RoomId}"));
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}

private sealed class InvalidDispatchFeature : HotfixGameFeature
{
    public ValueTask ExecuteAsync(HotfixFeatureCommandCall<DispatchCommand> call)
    {
        return default;
    }
}

[FeatureCommand(101)]
private sealed record DispatchCommand(string RoomId);

private sealed record DispatchReply(string Value);
```

- [ ] **Step 2: Run dispatch tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "FeatureCommandDispatch"
```

Expected: fail because feature command table APIs do not exist.

- [ ] **Step 3: Add public command descriptor and invoker contract**

Create `HotfixFeatureCommandDescriptor.cs`:

```csharp
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix;

public sealed record HotfixFeatureCommandDescriptor(
    string Key,
    string FeatureName,
    FeatureCommandId CommandId,
    Type RequestType,
    Type ReplyType);
```

Create `IHotfixFeatureCommandInvoker.cs`:

```csharp
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix;

public interface IHotfixFeatureCommandInvoker
{
    bool TryResolve(
        string featureName,
        FeatureCommandId commandId,
        out HotfixFeatureCommandDescriptor descriptor);

    ValueTask<object?> InvokeAsync(
        HotfixFeatureCommandDescriptor descriptor,
        object? request,
        FeatureMessageRequest message,
        IServiceProvider services,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Add internal binding and invoker facade**

Create `Dispatch/HotfixFeatureCommandBinding.cs`:

```csharp
using System.Reflection;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal sealed record HotfixFeatureCommandBinding(
    string Key,
    string FeatureName,
    FeatureCommandId CommandId,
    Type FeatureType,
    Type RequestType,
    Type ReplyType,
    MethodInfo Method);
```

Create `Dispatch/HotfixFeatureCommandInvoker.cs`:

```csharp
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixFeatureCommandInvoker : IHotfixFeatureCommandInvoker
{
    private readonly Func<HotfixDispatchTable> _current;

    public HotfixFeatureCommandInvoker()
        : this(static () => HotfixDispatch.Current)
    {
    }

    public HotfixFeatureCommandInvoker(HotfixDispatchTable table)
        : this(() => table)
    {
        ArgumentNullException.ThrowIfNull(table);
    }

    private HotfixFeatureCommandInvoker(Func<HotfixDispatchTable> current)
    {
        _current = current;
    }

    public bool TryResolve(
        string featureName,
        FeatureCommandId commandId,
        out HotfixFeatureCommandDescriptor descriptor)
    {
        return _current().TryResolveFeatureCommand(featureName, commandId, out descriptor);
    }

    public ValueTask<object?> InvokeAsync(
        HotfixFeatureCommandDescriptor descriptor,
        object? request,
        FeatureMessageRequest message,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _current().InvokeFeatureCommandAsync(
            descriptor,
            request,
            message,
            services,
            cancellationToken);
    }
}
```

- [ ] **Step 5: Extend dispatch table construction**

Add a constructor overload to `HotfixDispatchTable`:

```csharp
public HotfixDispatchTable(
    long version,
    IEnumerable<HotfixMethodBinding> methods,
    IEnumerable<HotfixServiceMethodBinding> services,
    IEnumerable<HotfixFeatureDeclaration> features)
```

Have the existing constructors delegate with `Array.Empty<HotfixFeatureDeclaration>()`.

Add fields:

```csharp
private readonly IReadOnlyDictionary<string, HotfixFeatureCommandBinding> featureCommandBindings;
private readonly IReadOnlyDictionary<Type, ObjectFactory> featureActivationFactories;
```

Build command bindings with this key:

```csharp
private static string CreateFeatureCommandKey(string featureName, FeatureCommandId commandId)
{
    return $"{featureName}#{commandId.Value}";
}
```

Use `StringComparer.OrdinalIgnoreCase` for the dictionary so feature name matching follows scanner duplicate-name rules.

- [ ] **Step 6: Resolve and validate feature command methods**

Add method resolution:

```csharp
private static MethodInfo ResolveFeatureCommandMethod(
    HotfixFeatureDeclaration feature,
    HotfixFeatureCommandDeclaration command)
{
    var callType = typeof(HotfixFeatureCommandCall<>).MakeGenericType(command.RequestType);
    var returnType = typeof(ValueTask<>).MakeGenericType(command.ReplyType);
    var matches = feature.FeatureType
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(method => string.Equals(method.Name, command.MethodName, StringComparison.Ordinal))
        .ToArray();

    var method = matches.SingleOrDefault(method =>
    {
        var parameters = method.GetParameters();
        return method.ReturnType == returnType &&
            parameters.Length == 1 &&
            parameters[0].ParameterType == callType;
    });

    if (method is null)
    {
        throw new InvalidOperationException(
            $"Hotfix feature command '{feature.Name}#{command.CommandId}' must map to public instance or static method '{command.MethodName}' returning {returnType.FullName} and accepting {callType.FullName}.");
    }

    return method;
}
```

Add:

```csharp
public void ValidateFeatureCommandMethods()
{
    foreach (var binding in featureCommandBindings.Values)
    {
        var callType = typeof(HotfixFeatureCommandCall<>).MakeGenericType(binding.RequestType);
        var returnType = typeof(ValueTask<>).MakeGenericType(binding.ReplyType);
        var parameters = binding.Method.GetParameters();
        if (binding.Method.ReturnType != returnType ||
            parameters.Length != 1 ||
            parameters[0].ParameterType != callType)
        {
            throw new InvalidOperationException(
                $"Hotfix feature command '{binding.Key}' must return {returnType.FullName} and accept {callType.FullName}.");
        }
    }
}
```

- [ ] **Step 7: Implement activation validation**

Add:

```csharp
public void ValidateFeatureCommandActivation(IServiceProvider services)
{
    ArgumentNullException.ThrowIfNull(services);

    var validated = new HashSet<Type>();
    foreach (var binding in featureCommandBindings.Values)
    {
        if (binding.Method.IsStatic || !validated.Add(binding.FeatureType))
        {
            continue;
        }

        if (!featureActivationFactories.TryGetValue(binding.FeatureType, out var factory))
        {
            throw new InvalidOperationException($"Hotfix feature '{binding.FeatureType.FullName}' does not have an activation factory.");
        }

        ServiceTarget target = default;
        try
        {
            target = new ServiceTarget(factory(services, Array.Empty<object?>()));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Hotfix feature '{binding.FeatureType.FullName}' constructor activation failed: {ex.Message}",
                ex);
        }
        finally
        {
            DisposeServiceTargetAsync(target).AsTask().GetAwaiter().GetResult();
        }
    }
}
```

- [ ] **Step 8: Implement resolve and invoke**

Add:

```csharp
public bool TryResolveFeatureCommand(
    string featureName,
    FeatureCommandId commandId,
    out HotfixFeatureCommandDescriptor descriptor)
{
    var key = CreateFeatureCommandKey(featureName, commandId);
    if (!featureCommandBindings.TryGetValue(key, out var binding))
    {
        descriptor = default!;
        return false;
    }

    descriptor = new HotfixFeatureCommandDescriptor(
        binding.Key,
        binding.FeatureName,
        binding.CommandId,
        binding.RequestType,
        binding.ReplyType);
    return true;
}

public async ValueTask<object?> InvokeFeatureCommandAsync(
    HotfixFeatureCommandDescriptor descriptor,
    object? request,
    FeatureMessageRequest message,
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(descriptor);
    ArgumentNullException.ThrowIfNull(message);
    ArgumentNullException.ThrowIfNull(services);
    cancellationToken.ThrowIfCancellationRequested();

    if (!featureCommandBindings.TryGetValue(descriptor.Key, out var binding))
    {
        throw new HotfixMethodNotLoadedException($"Hotfix feature command '{descriptor.Key}' is not loaded.");
    }

    var callType = typeof(HotfixFeatureCommandCall<>).MakeGenericType(binding.RequestType);
    var call = Activator.CreateInstance(
        callType,
        request,
        binding.FeatureName,
        binding.CommandId,
        message.CorrelationId,
        message.SourceNode,
        message.ExpiresAt,
        cancellationToken,
        services);
    if (call is null)
    {
        throw new InvalidOperationException($"Could not create hotfix feature command call for '{binding.Key}'.");
    }

    var target = CreateFeatureCommandTarget(binding, services);
    object? result;
    try
    {
        result = binding.Method.Invoke(target.Instance, [call]);
    }
    catch (TargetInvocationException ex) when (ex.InnerException is not null)
    {
        await DisposeServiceTargetAsync(target).ConfigureAwait(false);
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
    }
    catch
    {
        await DisposeServiceTargetAsync(target).ConfigureAwait(false);
        throw;
    }

    try
    {
        return await AwaitFeatureCommandResultAsync(result, binding).ConfigureAwait(false);
    }
    finally
    {
        await DisposeServiceTargetAsync(target).ConfigureAwait(false);
    }
}
```

Add helpers:

```csharp
private ServiceTarget CreateFeatureCommandTarget(
    HotfixFeatureCommandBinding binding,
    IServiceProvider services)
{
    if (binding.Method.IsStatic)
    {
        return new ServiceTarget(null);
    }

    if (!featureActivationFactories.TryGetValue(binding.FeatureType, out var factory))
    {
        throw new InvalidOperationException($"Hotfix feature '{binding.FeatureType.FullName}' does not have an activation factory.");
    }

    return new ServiceTarget(factory(services, Array.Empty<object?>()));
}

private static async ValueTask<object?> AwaitFeatureCommandResultAsync(
    object? result,
    HotfixFeatureCommandBinding binding)
{
    if (result is null)
    {
        throw new InvalidOperationException($"Hotfix feature command '{binding.Key}' returned null instead of ValueTask<{binding.ReplyType.FullName}>.");
    }

    var asTask = result.GetType().GetMethod("AsTask", Type.EmptyTypes);
    if (asTask is null)
    {
        throw new InvalidOperationException($"Hotfix feature command '{binding.Key}' returned an invalid result.");
    }

    var task = (Task?)asTask.Invoke(result, Array.Empty<object?>());
    if (task is null)
    {
        throw new InvalidOperationException($"Hotfix feature command '{binding.Key}' returned an invalid task.");
    }

    await task.ConfigureAwait(false);
    return task.GetType().GetProperty("Result")!.GetValue(task);
}
```

- [ ] **Step 9: Run hotfix dispatch tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "FeatureCommandDispatch|HotfixDispatchTests"
```

Expected: selected tests pass.

- [ ] **Step 10: Commit dispatch table changes**

Run:

```powershell
git add src\Lakona.Game.Server.Hotfix\HotfixFeatureCommandDescriptor.cs src\Lakona.Game.Server.Hotfix\IHotfixFeatureCommandInvoker.cs src\Lakona.Game.Server.Hotfix\Dispatch\HotfixFeatureCommandBinding.cs src\Lakona.Game.Server.Hotfix\Dispatch\HotfixFeatureCommandInvoker.cs src\Lakona.Game.Server.Hotfix\Dispatch\HotfixDispatchTable.cs tests\Lakona.Game.Server.Hotfix.Tests\HotfixDispatchTests.cs
git commit -m "Add hotfix feature command dispatch table"
```

Expected: commit succeeds.

---

## Task 6: Hotfix Manager Runtime Snapshot Integration

**Files:**
- Modify: `src/Lakona.Game.Server.Hotfix/IHotfixRuntimeAccessor.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/HotfixManager.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixManagerTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/GameSessionLifecycleBridgeTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs`

- [ ] **Step 1: Add manager reload tests for feature commands**

In `HotfixManagerTests`, add a compiled fixture source that declares a hotfix-owned command DTO and a constructor-DI feature. The source string must include:

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;

[HotfixFeature("commands")]
public sealed class CommandFeature : HotfixGameFeature
{
    private readonly IGenerationMarker _marker;

    public CommandFeature(IGenerationMarker marker)
    {
        _marker = marker;
    }

    public static void Configure(HotfixFeatureContext context)
    {
        context.HandleCommand<ManagerCommand, ManagerReply>(nameof(ExecuteAsync));
        context.Services.AddSingleton<IGenerationMarker, FirstMarker>();
    }

    public ValueTask<ManagerReply> ExecuteAsync(HotfixFeatureCommandCall<ManagerCommand> call)
    {
        return new ValueTask<ManagerReply>(new ManagerReply(call.Request.Value + _marker.Generation.Length));
    }
}

[FeatureCommand(301)]
public sealed record ManagerCommand(int Value);

public sealed record ManagerReply(int Value);

public sealed class FirstMarker : IGenerationMarker
{
    public string Generation => "first";
}
```

Add a test:

```csharp
[Fact]
public async Task Reload_publishes_feature_command_invoker()
{
    using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
    var manager = new HotfixManager(
        new FixedAssemblySource(compiled.FeatureCommandHotfixAssemblyPath),
        [typeof(IGenerationMarker).Assembly.GetName().Name!]);

    var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);

    Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    var runtime = ((IHotfixRuntimeAccessor)manager).Current;
    Assert.True(runtime.FeatureCommands.TryResolve("commands", FeatureCommandId.From(301), out var descriptor));
    var request = Activator.CreateInstance(descriptor.RequestType, [7])!;
    var reply = await runtime.FeatureCommands.InvokeAsync(
        descriptor,
        request,
        NewFeatureMessage("commands", "301"),
        runtime.Services,
        TestContext.Current.CancellationToken);

    Assert.Equal(12, (int)descriptor.ReplyType.GetProperty("Value")!.GetValue(reply)!);
}
```

Use the existing fixture pattern to expose `FeatureCommandHotfixAssemblyPath`.

Add a second fixture source where the feature command instance constructor requires an unregistered dependency:

```csharp
[HotfixFeature("commands")]
public sealed class MissingDependencyCommandFeature : HotfixGameFeature
{
    public MissingDependencyCommandFeature(MissingDependency dependency)
    {
    }

    public static void Configure(HotfixFeatureContext context)
    {
        context.HandleCommand<ManagerCommand, ManagerReply>(nameof(ExecuteAsync));
    }

    public ValueTask<ManagerReply> ExecuteAsync(HotfixFeatureCommandCall<ManagerCommand> call)
    {
        return new ValueTask<ManagerReply>(new ManagerReply(call.Request.Value));
    }
}

public sealed class MissingDependency
{
}
```

Expose it as `MissingFeatureCommandDependencyHotfixAssemblyPath`, then add:

```csharp
[Fact]
public async Task Reload_rejects_feature_command_constructor_dependency_failure_and_keeps_previous_generation()
{
    using var compiled = await CompiledHotfixFixture.CreateAsync(TestContext.Current.CancellationToken);
    var source = new SwitchableAssemblySource(compiled.FeatureCommandHotfixAssemblyPath);
    var manager = new HotfixManager(
        source,
        [typeof(IGenerationMarker).Assembly.GetName().Name!]);

    var first = await manager.ReloadAsync(TestContext.Current.CancellationToken);
    Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
    var previousRuntime = ((IHotfixRuntimeAccessor)manager).Current;

    source.AssemblyPath = compiled.MissingFeatureCommandDependencyHotfixAssemblyPath;
    var failed = await manager.ReloadAsync(TestContext.Current.CancellationToken);

    Assert.False(failed.Succeeded);
    Assert.Contains(failed.Diagnostics, diagnostic =>
        diagnostic.Contains("constructor activation failed", StringComparison.OrdinalIgnoreCase));
    Assert.Same(previousRuntime, ((IHotfixRuntimeAccessor)manager).Current);
    Assert.Equal(first.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
}
```

- [ ] **Step 2: Run manager test and verify it fails**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "Reload_publishes_feature_command_invoker"
```

Expected: fail because `HotfixRuntimeSnapshot` does not expose feature command invoker and `HotfixManager` does not validate feature command constructor activation.

- [ ] **Step 3: Extend runtime snapshot without breaking existing test constructors**

Modify `IHotfixRuntimeAccessor.cs`:

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix;

public interface IHotfixRuntimeAccessor
{
    HotfixRuntimeSnapshot Current { get; }
}

public sealed class HotfixRuntimeSnapshot
{
    public HotfixRuntimeSnapshot(IHotfixServiceInvoker invoker, IServiceProvider services)
        : this(invoker, new HotfixFeatureCommandInvoker(), services)
    {
    }

    public HotfixRuntimeSnapshot(
        IHotfixServiceInvoker invoker,
        IHotfixFeatureCommandInvoker featureCommands,
        IServiceProvider services)
    {
        Invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        FeatureCommands = featureCommands ?? throw new ArgumentNullException(nameof(featureCommands));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IHotfixServiceInvoker Invoker { get; }

    public IHotfixFeatureCommandInvoker FeatureCommands { get; }

    public IServiceProvider Services { get; }
}
```

- [ ] **Step 4: Publish command invoker from manager reload**

In `HotfixManager.cs`, change table construction:

```csharp
var table = new HotfixDispatchTable(tableVersion, scan.Methods, scan.Services, scan.Features);
table.ValidateMethodShapes();
table.ValidateTypedDispatchDelegates();
table.ValidateFeatureTickMethods(scan.Features);
table.ValidateFeatureCommandMethods();
hotfixProvider = BuildHotfixProvider(scan);
table.ValidateServiceActivation(hotfixProvider);
table.ValidateFeatureCommandActivation(hotfixProvider);
```

Change runtime snapshot creation:

```csharp
var runtimeSnapshot = new HotfixRuntimeSnapshot(
    new HotfixServiceInvoker(table),
    new HotfixFeatureCommandInvoker(table),
    hotfixProvider);
```

Do not add feature command DTOs to `HotfixDispatchBoundaryValidator`.

- [ ] **Step 5: Run hotfix manager tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "Reload_publishes_feature_command_invoker|Reload_rejects_feature_command_constructor_dependency_failure_and_keeps_previous_generation|Reload_keeps_previous_generation_when_validation_fails|Reload_fails_before_publish_when_service_constructor_dependency_is_missing"
```

Expected: selected tests pass.

- [ ] **Step 6: Run server tests that construct snapshots**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "GameSessionLifecycleBridgeTests|AddLakonaGameServer_feature_message_handler_is_noop_without_hotfix"
```

Expected: selected tests pass; existing two-argument `HotfixRuntimeSnapshot` construction still works.

- [ ] **Step 7: Commit runtime snapshot integration**

Run:

```powershell
git add src\Lakona.Game.Server.Hotfix\IHotfixRuntimeAccessor.cs src\Lakona.Game.Server.Hotfix\HotfixManager.cs tests\Lakona.Game.Server.Hotfix.Tests\HotfixManagerTests.cs tests\Lakona.Game.Server.Tests\GameSessionLifecycleBridgeTests.cs tests\Lakona.Game.Server.Tests\LakonaGameServerTests.cs
git commit -m "Publish hotfix feature command runtime snapshots"
```

Expected: commit succeeds.

---

## Task 7: Stable Feature Message Handler Dispatch

**Files:**
- Create: `src/Lakona.Game.Server/Features/FeatureMessageSerializerInvoker.cs`
- Modify: `src/Lakona.Game.Server/Hotfix/HotfixFeatureMessageHandler.cs`
- Add: `tests/Lakona.Game.Server.Tests/HotfixFeatureMessageHandlerTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs`

- [ ] **Step 1: Write handler dispatch tests**

Create `HotfixFeatureMessageHandlerTests.cs`:

```csharp
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class HotfixFeatureMessageHandlerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task HandleAsyncRejectsInvalidTypedCommandKind(string kind)
    {
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new RecordingCommandInvoker()),
            new JsonFeatureSerializer());

        var reply = await handler.HandleAsync(
            NewRequest(kind, ReadOnlyMemory<byte>.Empty),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Rejected, reply.Status);
    }

    [Fact]
    public async Task HandleAsyncDispatchesTypedCommandAndSerializesReply()
    {
        var serializer = new JsonFeatureSerializer();
        var invoker = new RecordingCommandInvoker();
        var handler = new HotfixFeatureMessageHandler(new FixedAccessor(invoker), serializer);
        var payload = serializer.Serialize(new TestCommand("room-1"));

        var reply = await handler.HandleAsync(
            NewRequest("17", payload),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Accepted, reply.Status);
        Assert.Equal("room-1", Assert.IsType<TestCommand>(invoker.Request).RoomId);
        Assert.Equal("ok", serializer.Deserialize<TestReply>(reply.Payload).Status);
    }

    [Fact]
    public async Task HandleAsyncReturnsFeatureNotFoundForUnknownCommand()
    {
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new MissingCommandInvoker()),
            new JsonFeatureSerializer());

        var reply = await handler.HandleAsync(
            NewRequest("17", ReadOnlyMemory<byte>.Empty),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.FeatureNotFound, reply.Status);
    }

    [Fact]
    public async Task HandleAsyncMapsDeserializerFailure()
    {
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new RecordingCommandInvoker()),
            new JsonFeatureSerializer());

        var reply = await handler.HandleAsync(
            NewRequest("17", new byte[] { 0xFF }),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.DeserializationFailed, reply.Status);
    }

    [Fact]
    public async Task HandleAsyncReturnsExpiredBeforeCommandDispatch()
    {
        var serializer = new JsonFeatureSerializer();
        var invoker = new RecordingCommandInvoker();
        var handler = new HotfixFeatureMessageHandler(new FixedAccessor(invoker), serializer);

        var reply = await handler.HandleAsync(
            NewExpiredRequest("17", serializer.Serialize(new TestCommand("room-1"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Expired, reply.Status);
        Assert.Null(invoker.Request);
    }

    [Fact]
    public async Task HandleAsyncPropagatesCallerCancellationBeforeDispatch()
    {
        var serializer = new JsonFeatureSerializer();
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new RecordingCommandInvoker()),
            serializer);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler
            .HandleAsync(
                NewRequest("17", serializer.Serialize(new TestCommand("room-1"))),
                cts.Token)
            .AsTask());
    }

    [Fact]
    public async Task HandleAsyncPropagatesCommandCancellationWhenCallerTokenIsCanceledDuringDispatch()
    {
        var serializer = new JsonFeatureSerializer();
        using var cts = new CancellationTokenSource();
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new CancelingCommandInvoker(cts)),
            serializer);

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler
            .HandleAsync(
                NewRequest("17", serializer.Serialize(new TestCommand("room-1"))),
                cts.Token)
            .AsTask());
    }

    [Fact]
    public async Task HandleAsyncMapsDetachedOperationCanceledExceptionToFailed()
    {
        var serializer = new JsonFeatureSerializer();
        var handler = new HotfixFeatureMessageHandler(
            new FixedAccessor(new DetachedCancellationCommandInvoker()),
            serializer);

        var reply = await handler.HandleAsync(
            NewRequest("17", serializer.Serialize(new TestCommand("room-1"))),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.Failed, reply.Status);
    }

    private static FeatureMessageRequest NewRequest(string kind, ReadOnlyMemory<byte> payload)
    {
        return new FeatureMessageRequest(
            new FeatureName("battle-runtime"),
            kind,
            payload,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("data-1"),
            "corr-1");
    }

    private static FeatureMessageRequest NewExpiredRequest(string kind, ReadOnlyMemory<byte> payload)
    {
        return new FeatureMessageRequest(
            new FeatureName("battle-runtime"),
            kind,
            payload,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            new NodeId("data-1"),
            "corr-1");
    }

    [FeatureCommand(17)]
    private sealed record TestCommand(string RoomId);

    private sealed record TestReply(string Status);

    private sealed class JsonFeatureSerializer : IFeatureMessageSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload.Span)!;
        }
    }

    private sealed class FixedAccessor : IHotfixRuntimeAccessor
    {
        public FixedAccessor(IHotfixFeatureCommandInvoker commands)
        {
            Current = new HotfixRuntimeSnapshot(
                new HotfixServiceInvoker(),
                commands,
                new ServiceCollection().BuildServiceProvider());
        }

        public HotfixRuntimeSnapshot Current { get; }
    }

    private sealed class RecordingCommandInvoker : IHotfixFeatureCommandInvoker
    {
        public object? Request { get; private set; }

        public bool TryResolve(string featureName, FeatureCommandId commandId, out HotfixFeatureCommandDescriptor descriptor)
        {
            descriptor = new HotfixFeatureCommandDescriptor(
                $"{featureName}#{commandId.Value}",
                featureName,
                commandId,
                typeof(TestCommand),
                typeof(TestReply));
            return true;
        }

        public ValueTask<object?> InvokeAsync(
            HotfixFeatureCommandDescriptor descriptor,
            object? request,
            FeatureMessageRequest message,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return new ValueTask<object?>(new TestReply("ok"));
        }
    }

    private sealed class MissingCommandInvoker : IHotfixFeatureCommandInvoker
    {
        public bool TryResolve(string featureName, FeatureCommandId commandId, out HotfixFeatureCommandDescriptor descriptor)
        {
            descriptor = default!;
            return false;
        }

        public ValueTask<object?> InvokeAsync(
            HotfixFeatureCommandDescriptor descriptor,
            object? request,
            FeatureMessageRequest message,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CancelingCommandInvoker : IHotfixFeatureCommandInvoker
    {
        private readonly CancellationTokenSource _source;

        public CancelingCommandInvoker(CancellationTokenSource source)
        {
            _source = source;
        }

        public bool TryResolve(string featureName, FeatureCommandId commandId, out HotfixFeatureCommandDescriptor descriptor)
        {
            descriptor = new HotfixFeatureCommandDescriptor(
                $"{featureName}#{commandId.Value}",
                featureName,
                commandId,
                typeof(TestCommand),
                typeof(TestReply));
            return true;
        }

        public ValueTask<object?> InvokeAsync(
            HotfixFeatureCommandDescriptor descriptor,
            object? request,
            FeatureMessageRequest message,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            _source.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class DetachedCancellationCommandInvoker : IHotfixFeatureCommandInvoker
    {
        public bool TryResolve(string featureName, FeatureCommandId commandId, out HotfixFeatureCommandDescriptor descriptor)
        {
            descriptor = new HotfixFeatureCommandDescriptor(
                $"{featureName}#{commandId.Value}",
                featureName,
                commandId,
                typeof(TestCommand),
                typeof(TestReply));
            return true;
        }

        public ValueTask<object?> InvokeAsync(
            HotfixFeatureCommandDescriptor descriptor,
            object? request,
            FeatureMessageRequest message,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException("Detached cancellation.");
        }
    }
}
```

- [ ] **Step 2: Run handler tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "HotfixFeatureMessageHandlerTests"
```

Expected: fail because handler still fans out to hotfix-side `IFeatureMessageHandler`.

- [ ] **Step 3: Add non-generic serializer reflection helper**

Create `FeatureMessageSerializerInvoker.cs`:

```csharp
using System.Reflection;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Features;

internal static class FeatureMessageSerializerInvoker
{
    private static readonly MethodInfo SerializeMethod = typeof(IFeatureMessageSerializer)
        .GetMethods()
        .Single(method => method.Name == nameof(IFeatureMessageSerializer.Serialize));

    private static readonly MethodInfo DeserializeMethod = typeof(IFeatureMessageSerializer)
        .GetMethods()
        .Single(method => method.Name == nameof(IFeatureMessageSerializer.Deserialize));

    public static object? Deserialize(
        IFeatureMessageSerializer serializer,
        Type payloadType,
        ReadOnlyMemory<byte> payload)
    {
        return DeserializeMethod
            .MakeGenericMethod(payloadType)
            .Invoke(serializer, [payload]);
    }

    public static ReadOnlyMemory<byte> Serialize(
        IFeatureMessageSerializer serializer,
        Type payloadType,
        object? value)
    {
        var result = SerializeMethod
            .MakeGenericMethod(payloadType)
            .Invoke(serializer, [value]);
        return result is ReadOnlyMemory<byte> payload
            ? payload
            : throw new InvalidOperationException("Feature message serializer returned an invalid payload.");
    }
}
```

- [ ] **Step 4: Replace handler fan-out with command dispatch**

Replace `HotfixFeatureMessageHandler.HandleAsync` with:

```csharp
public async ValueTask<FeatureMessageReply> HandleAsync(
    FeatureMessageRequest request,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(request);
    cancellationToken.ThrowIfCancellationRequested();

    if (request.IsExpired(DateTimeOffset.UtcNow))
    {
        return new FeatureMessageReply(ClusterSendStatus.Expired, ReadOnlyMemory<byte>.Empty);
    }

    if (_hotfixRuntime is null)
    {
        return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
    }

    if (!FeatureCommandId.TryParse(request.Kind, out var commandId))
    {
        return new FeatureMessageReply(
            ClusterSendStatus.Rejected,
            ReadOnlyMemory<byte>.Empty,
            $"Feature message kind '{request.Kind}' is not a valid feature command id.");
    }

    var snapshot = _hotfixRuntime.Current;
    if (!snapshot.FeatureCommands.TryResolve(request.Feature.Value, commandId, out var descriptor))
    {
        return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
    }

    if (_serializer is null)
    {
        return new FeatureMessageReply(
            ClusterSendStatus.HandlerUnavailable,
            ReadOnlyMemory<byte>.Empty,
            "Feature message serializer is not available.");
    }

    object? commandRequest;
    try
    {
        commandRequest = FeatureMessageSerializerInvoker.Deserialize(
            _serializer,
            descriptor.RequestType,
            request.Payload);
    }
    catch (Exception ex)
    {
        return new FeatureMessageReply(ClusterSendStatus.DeserializationFailed, ReadOnlyMemory<byte>.Empty, ex.Message);
    }

    object? commandReply;
    try
    {
        commandReply = await snapshot.FeatureCommands.InvokeAsync(
            descriptor,
            commandRequest,
            request,
            snapshot.Services,
            cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (OperationCanceledException ex)
    {
        return new FeatureMessageReply(ClusterSendStatus.Failed, ReadOnlyMemory<byte>.Empty, ex.Message);
    }
    catch (Exception ex)
    {
        return new FeatureMessageReply(ClusterSendStatus.Failed, ReadOnlyMemory<byte>.Empty, ex.Message);
    }

    try
    {
        var payload = FeatureMessageSerializerInvoker.Serialize(
            _serializer,
            descriptor.ReplyType,
            commandReply);
        return new FeatureMessageReply(ClusterSendStatus.Accepted, payload);
    }
    catch (Exception ex)
    {
        return new FeatureMessageReply(ClusterSendStatus.SerializationFailed, ReadOnlyMemory<byte>.Empty, ex.Message);
    }
}
```

Update constructor:

```csharp
private readonly IHotfixRuntimeAccessor? _hotfixRuntime;
private readonly IFeatureMessageSerializer? _serializer;

public HotfixFeatureMessageHandler(
    IHotfixRuntimeAccessor? hotfixRuntime = null,
    IFeatureMessageSerializer? serializer = null)
{
    _hotfixRuntime = hotfixRuntime;
    _serializer = serializer;
}
```

Add `using Lakona.Game.Server.Features;`.

- [ ] **Step 5: Replace old fan-out tests**

In `LakonaGameServerTests`, remove or rewrite `AddLakonaGameServer_routes_feature_messages_to_current_hotfix_handlers`. Replace it with:

```csharp
[Fact]
public async Task AddLakonaGameServer_allows_stable_feature_message_handler_replacement()
{
    var custom = new RecordingFeatureMessageHandler();
    await using var provider = new ServiceCollection()
        .AddSingleton<IFeatureMessageHandler>(custom)
        .AddLakonaGameServer()
        .BuildServiceProvider();

    var handler = provider.GetRequiredService<IFeatureMessageHandler>();
    var reply = await handler.HandleAsync(
        new FeatureMessageRequest(
            new FeatureName("battle-runtime"),
            "17",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new NodeId("data-1"),
            "corr-1"),
        TestContext.Current.CancellationToken);

    Assert.Same(custom, handler);
    Assert.Equal(ClusterSendStatus.Accepted, reply.Status);
}
```

Keep `AddLakonaGameServer_feature_message_handler_is_noop_without_hotfix`.

- [ ] **Step 6: Run handler and server registration tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "HotfixFeatureMessageHandlerTests|AddLakonaGameServer_allows_stable_feature_message_handler_replacement|AddLakonaGameServer_feature_message_handler_is_noop_without_hotfix"
```

Expected: selected tests pass.

- [ ] **Step 7: Commit stable handler changes**

Run:

```powershell
git add src\Lakona.Game.Server\Features\FeatureMessageSerializerInvoker.cs src\Lakona.Game.Server\Hotfix\HotfixFeatureMessageHandler.cs tests\Lakona.Game.Server.Tests\HotfixFeatureMessageHandlerTests.cs tests\Lakona.Game.Server.Tests\LakonaGameServerTests.cs
git commit -m "Dispatch feature messages through hotfix command table"
```

Expected: commit succeeds.

---

## Task 8: Migrate Agar Hotfix Features To Typed Commands

**Files:**
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeFeature.cs`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Features/BattleRuntimeRoomAllocation.cs`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Features/StateStoreFeatures.cs`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Services/StateStoreUserActorPlacement.cs`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Services/LoginService.cs`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Services/PlayerService.cs`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingBehavior.cs`
- Test: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixTests.cs`
- Test: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`
- Test: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`

- [ ] **Step 1: Add source scan tests that reject Agar feature message handlers**

Add or extend a business logic test:

```csharp
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
```

Use the existing repo-root helper if the test project already has one; otherwise add a local helper that walks upward until `CONTRIBUTING.md` exists.

- [ ] **Step 2: Run Agar source scan test and verify it fails**

Run:

```powershell
dotnet test samples\Game.Unity.Agar\tests\BusinessLogic.Tests\BusinessLogic.Tests.csproj --no-restore --filter "AgarHotfixFeatures_DoNotRegisterFeatureMessageHandlers"
```

Expected: fail because Agar still has `BattleRuntimeFeatureMessageHandler`, `StateStoreFeatureMessageHandler`, and manual feature message transport usage.

- [ ] **Step 3: Convert battle runtime command DTO**

Modify `BattleRuntimeRoomAllocation.cs`:

```csharp
using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Lakona.Game.Server.Hotfix.Abstractions;
using MemoryPack;

namespace Server.Hotfix.Features;

internal static class BattleRuntimeRoomAllocation
{
    public const string FeatureName = "battle-runtime";

    public const int AllocateRoomCommandId = 101;
}

[MemoryPackable(GenerateType.VersionTolerant)]
[FeatureCommand(BattleRuntimeRoomAllocation.AllocateRoomCommandId)]
public partial class BattleRuntimeRoomAllocationRequest
{
    [MemoryPackOrder(0)]
    public string RoomId { get; set; } = "";

    [MemoryPackOrder(1)]
    public string MatchId { get; set; } = "";

    [MemoryPackOrder(2)]
    public string CreatedByUserId { get; set; } = "";

    [MemoryPackOrder(3)]
    public DateTime CreatedAtUtc { get; set; }

    [MemoryPackOrder(4)]
    public int MaxPlayers { get; set; } = 10;

    [MemoryPackOrder(5)]
    public List<PlayerRoomAssignment> Players { get; set; } = new();

    [MemoryPackOrder(6)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BattleRuntimeRoomAllocationReply
{
    [MemoryPackOrder(0)]
    public bool Succeeded { get; set; }

    [MemoryPackOrder(1)]
    public string RoomId { get; set; } = "";

    [MemoryPackOrder(2)]
    public string MatchId { get; set; } = "";

    [MemoryPackOrder(3)]
    public string Message { get; set; } = "";

    [MemoryPackOrder(4)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}
```

Delete `BattleRuntimeFeatureMessageHandler` from this file after moving its helper methods into `BattleRuntimeFeature.cs`.

- [ ] **Step 4: Move battle runtime handling into feature class**

Replace `BattleRuntimeFeature.cs` with:

```csharp
using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Rooms;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Logging;
using Server.Hotfix.State.Rooms;

namespace Server.Hotfix.Features;

[HotfixFeature(BattleRuntimeRoomAllocation.FeatureName)]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    private readonly IActorLifecycle _lifecycle;
    private readonly IActorDirectory _directory;
    private readonly IActorDirectoryCache _directoryCache;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly RoomActors _rooms;
    private readonly ILogger<BattleRuntimeFeature> _logger;

    public BattleRuntimeFeature(
        IActorLifecycle lifecycle,
        IActorDirectory directory,
        IActorDirectoryCache directoryCache,
        LocalActorNodeIdentity localNode,
        RoomActors rooms,
        ILogger<BattleRuntimeFeature> logger)
    {
        _lifecycle = lifecycle;
        _directory = directory;
        _directoryCache = directoryCache;
        _localNode = localNode;
        _rooms = rooms;
        _logger = logger;
    }

    public static void Configure(HotfixFeatureContext context)
    {
        context.HandleCommand<BattleRuntimeRoomAllocationRequest, BattleRuntimeRoomAllocationReply>(
            nameof(AllocateRoomAsync));
        context.ScheduleActiveActorTicks<RoomActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending);
    }

    public async ValueTask<BattleRuntimeRoomAllocationReply> AllocateRoomAsync(
        HotfixFeatureCommandCall<BattleRuntimeRoomAllocationRequest> call)
    {
        var payload = call.Request;
        if (string.IsNullOrWhiteSpace(payload.RoomId) || payload.Players.Count == 0)
        {
            return CreateReply(payload, false, "RoomId and Players are required.");
        }

        var actorId = ActorId.From(payload.RoomId);
        var registerStatus = await _directory
            .RegisterAsync(actorId, _localNode.NodeId, call.CancellationToken)
            .ConfigureAwait(false);
        var registeredHere = registerStatus == ActorDirectoryRegisterStatus.Registered;
        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            _directoryCache.Remove(actorId);
            return CreateReply(payload, false, $"Room actor {payload.RoomId} is owned by another node.");
        }

        try
        {
            var createResult = await _lifecycle
                .CreateLocalAsync<RoomActor>(actorId, cancellationToken: call.CancellationToken)
                .ConfigureAwait(false);
            if (!createResult.Succeeded)
            {
                if (registeredHere)
                {
                    await _directory.UnregisterAsync(actorId, _localNode.NodeId, call.CancellationToken).ConfigureAwait(false);
                }

                _directoryCache.Remove(actorId);
                return CreateReply(payload, false, createResult.Diagnostic ?? $"Could not create room actor '{payload.RoomId}'.");
            }

            var roomId = new RoomId(payload.RoomId);
            var create = await _rooms.Local(roomId).CreateAsync(new RoomCreateRequest
            {
                RoomId = payload.RoomId,
                MatchId = payload.MatchId,
                CreatedByUserId = payload.CreatedByUserId,
                CreatedAtUtc = payload.CreatedAtUtc,
                MaxPlayers = payload.MaxPlayers,
                Players = payload.Players.Select(CloneAssignment).ToList(),
                RuntimeGateway = CloneGateway(payload.RuntimeGateway)
            }).ConfigureAwait(false);
            if (!create.Succeeded)
            {
                return CreateReply(payload, false, create.Message);
            }

            var start = await _rooms.Local(roomId).StartAsync(new RoomStartRequest
            {
                RoomId = payload.RoomId,
                StartedByUserId = payload.CreatedByUserId,
                StartedAtUtc = payload.CreatedAtUtc
            }).ConfigureAwait(false);
            if (!start.Succeeded)
            {
                return CreateReply(payload, false, start.Message);
            }

            _directoryCache.Set(actorId, _localNode.NodeId);
            _logger.LogDebug("Allocated battle-runtime room {RoomId} on node {NodeId}.", payload.RoomId, _localNode.NodeId.Value);
            return CreateReply(payload, true, "Room allocated.");
        }
        catch
        {
            if (registeredHere)
            {
                await _directory.UnregisterAsync(actorId, _localNode.NodeId, call.CancellationToken).ConfigureAwait(false);
            }

            _directoryCache.Remove(actorId);
            throw;
        }
    }

    private static BattleRuntimeRoomAllocationReply CreateReply(
        BattleRuntimeRoomAllocationRequest request,
        bool succeeded,
        string message)
    {
        return new BattleRuntimeRoomAllocationReply
        {
            Succeeded = succeeded,
            RoomId = request.RoomId,
            MatchId = request.MatchId,
            Message = message,
            RuntimeGateway = CloneGateway(request.RuntimeGateway)
        };
    }

    private static PlayerRoomAssignment CloneAssignment(PlayerRoomAssignment assignment)
    {
        return new PlayerRoomAssignment
        {
            UserId = assignment.UserId,
            RoomId = assignment.RoomId,
            MatchId = assignment.MatchId,
            SeatIndex = assignment.SeatIndex,
            SessionToken = assignment.SessionToken,
            ConnectionId = assignment.ConnectionId,
            AssignedAtUtc = assignment.AssignedAtUtc,
            RuntimeGateway = CloneGateway(assignment.RuntimeGateway)
        };
    }

    private static GatewayEndpointDescriptor CloneGateway(GatewayEndpointDescriptor? gateway)
    {
        if (gateway is null)
        {
            return new GatewayEndpointDescriptor();
        }

        return new GatewayEndpointDescriptor
        {
            InstanceId = gateway.InstanceId,
            Transport = gateway.Transport,
            Host = gateway.Host,
            Port = gateway.Port,
            Path = gateway.Path
        };
    }
}
```

- [ ] **Step 5: Add MemoryPack generator references to the Agar hotfix project**

Modify `Server.Hotfix.csproj` and add package references next to the existing project references:

```xml
<ItemGroup>
  <PackageReference Include="MemoryPack.UnityShims" Version="1.21.4" />
  <PackageReference Include="MemoryPack.Generator" Version="1.21.4" PrivateAssets="all" />
</ItemGroup>
```

Typed feature command payloads follow the configured cluster serializer. Agar's cluster serializer is memorypack, so hotfix-owned command DTOs must be generated MemoryPack types rather than plain JSON-only classes.

- [ ] **Step 6: Convert state store command DTOs**

Modify `StateStoreUserActorPlacement.cs`:

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;
using MemoryPack;

namespace Server.Hotfix.Services;

internal static class StateStoreUserActorPlacement
{
    public const string FeatureName = "state-store";

    public const int EnsureUserActorCommandId = 201;

    public const int EnsureLeaderboardActorCommandId = 202;
}

[MemoryPackable(GenerateType.VersionTolerant)]
[FeatureCommand(StateStoreUserActorPlacement.EnsureUserActorCommandId)]
internal partial class EnsureUserActorRequest
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = "";
}

[MemoryPackable(GenerateType.VersionTolerant)]
[FeatureCommand(StateStoreUserActorPlacement.EnsureLeaderboardActorCommandId)]
internal partial class EnsureLeaderboardActorRequest
{
    [MemoryPackOrder(0)]
    public string LeaderboardId { get; set; } = "";
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal partial class EnsureActorReply
{
    [MemoryPackOrder(0)]
    public bool Succeeded { get; set; }

    [MemoryPackOrder(1)]
    public string Message { get; set; } = "";
}
```

- [ ] **Step 7: Move state-store handling into feature class**

In `StateStoreFeatures.cs`, remove `StateStoreFeatureMessageHandler`, remove `using System.Text.Json;`, and make `StateStoreFeature` constructor-injected:

```csharp
[HotfixFeature(StateStoreUserActorPlacement.FeatureName)]
public sealed class StateStoreFeature : HotfixGameFeature
{
    private readonly IActorLifecycle _lifecycle;
    private readonly IActorDirectory _directory;
    private readonly IActorDirectoryCache _directoryCache;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ILogger<StateStoreFeature> _logger;

    public StateStoreFeature(
        IActorLifecycle lifecycle,
        IActorDirectory directory,
        IActorDirectoryCache directoryCache,
        LocalActorNodeIdentity localNode,
        ILogger<StateStoreFeature> logger)
    {
        _lifecycle = lifecycle;
        _directory = directory;
        _directoryCache = directoryCache;
        _localNode = localNode;
        _logger = logger;
    }

    public static void Configure(HotfixFeatureContext context)
    {
        context.Services.AddSingleton<MatchmakingNotifier>();
        context.Services.AddSingleton<RoomNotifier>();
        context.HandleCommand<EnsureUserActorRequest, EnsureActorReply>(nameof(EnsureUserActorAsync));
        context.HandleCommand<EnsureLeaderboardActorRequest, EnsureActorReply>(nameof(EnsureLeaderboardActorAsync));
    }

    public async ValueTask<EnsureActorReply> EnsureUserActorAsync(
        HotfixFeatureCommandCall<EnsureUserActorRequest> call)
    {
        if (string.IsNullOrWhiteSpace(call.Request.UserId))
        {
            return new EnsureActorReply { Succeeded = false, Message = "UserId is required." };
        }

        return await EnsureActorAsync<UserActor>(
            ActorId.From(call.Request.UserId),
            $"user actor {call.Request.UserId}",
            call.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<EnsureActorReply> EnsureLeaderboardActorAsync(
        HotfixFeatureCommandCall<EnsureLeaderboardActorRequest> call)
    {
        if (string.IsNullOrWhiteSpace(call.Request.LeaderboardId))
        {
            return new EnsureActorReply { Succeeded = false, Message = "LeaderboardId is required." };
        }

        return await EnsureActorAsync<LeaderboardActor>(
            ActorId.From(call.Request.LeaderboardId),
            $"leaderboard actor {call.Request.LeaderboardId}",
            call.CancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<EnsureActorReply> EnsureActorAsync<TActor>(
        ActorId actorId,
        string description,
        CancellationToken cancellationToken)
    {
        var registerStatus = await _directory
            .RegisterAsync(actorId, _localNode.NodeId, cancellationToken)
            .ConfigureAwait(false);
        var registeredHere = registerStatus == ActorDirectoryRegisterStatus.Registered;
        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            _directoryCache.Remove(actorId);
            return new EnsureActorReply
            {
                Succeeded = false,
                Message = $"{description} is owned by another node."
            };
        }

        try
        {
            var createResult = await _lifecycle
                .CreateLocalAsync<TActor>(actorId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!createResult.Succeeded)
            {
                if (registeredHere)
                {
                    await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);
                }

                _directoryCache.Remove(actorId);
                return new EnsureActorReply
                {
                    Succeeded = false,
                    Message = createResult.Diagnostic ?? $"Could not create {description}. Status={createResult.Status}."
                };
            }

            _directoryCache.Set(actorId, _localNode.NodeId);
            _logger.LogDebug("Created state-store {Description} on node {NodeId}.", description, _localNode.NodeId.Value);
            return new EnsureActorReply { Succeeded = true, Message = "Actor ready." };
        }
        catch
        {
            if (registeredHere)
            {
                await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);
            }

            _directoryCache.Remove(actorId);
            throw;
        }
    }
}
```

- [ ] **Step 8: Replace direct feature message transport in LoginService**

In `LoginService.cs`, add:

```csharp
using Lakona.Game.Server.Features;
```

Replace `SendEnsureUserActorAsync` with:

```csharp
private static async ValueTask SendEnsureUserActorAsync(
    ClusterNodeDescriptor owner,
    string userId,
    IServiceProvider services)
{
    var client = services.GetRequiredService<IFeatureCommandClient>();
    var reply = await client.SendToNodeAsync<EnsureUserActorRequest, EnsureActorReply>(
        owner,
        StateStoreUserActorPlacement.FeatureName,
        new EnsureUserActorRequest { UserId = userId }).ConfigureAwait(false);
    if (!reply.Succeeded)
    {
        throw new InvalidOperationException(
            $"State-store node {owner.Node.Value} rejected user actor creation for '{userId}'. {reply.Message}");
    }
}
```

Remove manual JSON serialization and `FeatureMessageRequest` construction from this method.

- [ ] **Step 9: Replace direct feature message transport in PlayerService**

In `PlayerService.cs`, add:

```csharp
using Lakona.Game.Server.Features;
```

Replace `SendEnsureLeaderboardActorAsync` with:

```csharp
private static async ValueTask SendEnsureLeaderboardActorAsync(
    ClusterNodeDescriptor owner,
    string leaderboardId,
    IServiceProvider services)
{
    var client = services.GetRequiredService<IFeatureCommandClient>();
    var reply = await client.SendToNodeAsync<EnsureLeaderboardActorRequest, EnsureActorReply>(
        owner,
        StateStoreUserActorPlacement.FeatureName,
        new EnsureLeaderboardActorRequest { LeaderboardId = leaderboardId }).ConfigureAwait(false);
    if (!reply.Succeeded)
    {
        throw new InvalidOperationException(
            $"State-store node {owner.Node.Value} rejected leaderboard actor creation for '{leaderboardId}'. {reply.Message}");
    }
}
```

- [ ] **Step 10: Replace battle-runtime direct transport in matchmaking**

In `MatchmakingBehavior.cs`, add:

```csharp
using Lakona.Game.Server.Features;
```

Change the dependency check in `AllocateRemoteRoomAsync`:

```csharp
if (self.Context.Services.GetService<IClusterNodeDiscovery>() is not IClusterNodeDiscovery discovery ||
    self.Context.Services.GetService<IFeatureCommandClient>() is not IFeatureCommandClient commands)
{
    return new RoomSettlementResult
    {
        RoomId = request.RoomId,
        Succeeded = false,
        Message = "Battle runtime feature command client is unavailable."
    };
}
```

Replace manual payload/message/send with:

```csharp
var reply = await commands.SendToNodeAsync<BattleRuntimeRoomAllocationRequest, BattleRuntimeRoomAllocationReply>(
    target,
    BattleRuntimeRoomAllocation.FeatureName,
    new BattleRuntimeRoomAllocationRequest
    {
        RoomId = request.RoomId,
        MatchId = request.MatchId,
        CreatedByUserId = request.CreatedByUserId,
        CreatedAtUtc = request.CreatedAtUtc,
        MaxPlayers = request.MaxPlayers,
        Players = request.Players.Select(CloneAssignment).ToList(),
        RuntimeGateway = CloneGateway(request.RuntimeGateway)
    }).ConfigureAwait(false);
if (!reply.Succeeded)
{
    return new RoomSettlementResult
    {
        RoomId = request.RoomId,
        Succeeded = false,
        Message = string.IsNullOrWhiteSpace(reply.Message)
            ? "Battle runtime allocation failed."
            : reply.Message
    };
}
```

Return success using the typed reply fields.

- [ ] **Step 11: Add MemoryPack roundtrip coverage for typed feature command DTOs**

Add this project reference to `BusinessLogic.Tests.csproj`:

```xml
<ProjectReference Include="..\..\..\..\src\Lakona.Game.Cluster.Rpc.MemoryPack\Lakona.Game.Cluster.Rpc.MemoryPack.csproj" />
```

Add a test in the Agar business logic test project:

```csharp
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Lakona.Game.Cluster.Rpc.MemoryPack;
using Server.Hotfix.Features;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarFeatureCommandSerializationTests
{
    [Fact]
    public void BattleRuntimeFeatureCommandDtosRoundTripWithConfiguredMemoryPackSerializer()
    {
        var serializer = ClusterRpcMemoryPack.CreateSerializer();
        var request = new BattleRuntimeRoomAllocationRequest
        {
            RoomId = "room-1",
            MatchId = "match-1",
            CreatedByUserId = "user-1",
            CreatedAtUtc = new DateTime(2026, 6, 29, 8, 0, 0, DateTimeKind.Utc),
            MaxPlayers = 10,
            Players =
            [
                new PlayerRoomAssignment
                {
                    UserId = "user-1",
                    RoomId = "room-1",
                    MatchId = "match-1",
                    SeatIndex = 0,
                    SessionToken = "session-1",
                    ConnectionId = "connection-1",
                    AssignedAtUtc = new DateTime(2026, 6, 29, 8, 0, 1, DateTimeKind.Utc),
                    RuntimeGateway = new GatewayEndpointDescriptor
                    {
                        InstanceId = "runtime-1",
                        Transport = "kcp",
                        Host = "127.0.0.1",
                        Port = 7001,
                        Path = ""
                    }
                }
            ],
            RuntimeGateway = new GatewayEndpointDescriptor
            {
                InstanceId = "runtime-1",
                Transport = "kcp",
                Host = "127.0.0.1",
                Port = 7001,
                Path = ""
            }
        };

        using var frame = serializer.SerializeFrame(request);
        var decoded = serializer.Deserialize<BattleRuntimeRoomAllocationRequest>(frame.Memory);

        Assert.Equal("room-1", decoded.RoomId);
        Assert.Equal("match-1", decoded.MatchId);
        Assert.Single(decoded.Players);
        Assert.Equal("runtime-1", decoded.RuntimeGateway.InstanceId);
    }

    [Fact]
    public void BattleRuntimeFeatureCommandReplyRoundTripsWithConfiguredMemoryPackSerializer()
    {
        var serializer = ClusterRpcMemoryPack.CreateSerializer();
        var reply = new BattleRuntimeRoomAllocationReply
        {
            Succeeded = true,
            RoomId = "room-1",
            MatchId = "match-1",
            Message = "Room allocated.",
            RuntimeGateway = new GatewayEndpointDescriptor
            {
                InstanceId = "runtime-1",
                Transport = "kcp",
                Host = "127.0.0.1",
                Port = 7001,
                Path = ""
            }
        };

        using var frame = serializer.SerializeFrame(reply);
        var decoded = serializer.Deserialize<BattleRuntimeRoomAllocationReply>(frame.Memory);

        Assert.True(decoded.Succeeded);
        Assert.Equal("room-1", decoded.RoomId);
        Assert.Equal("Room allocated.", decoded.Message);
    }
}
```

- [ ] **Step 12: Run Agar compile/test slice**

Run:

```powershell
dotnet test samples\Game.Unity.Agar\tests\BusinessLogic.Tests\BusinessLogic.Tests.csproj --no-restore --filter "AgarHotfixFeatures_DoNotRegisterFeatureMessageHandlers|BattleRuntimeFeatureCommandDtosRoundTripWithConfiguredMemoryPackSerializer|BattleRuntimeFeatureCommandReplyRoundTripsWithConfiguredMemoryPackSerializer|AgarHotfixTests|DistributedTopologyConfigurationTests"
```

Expected: selected tests pass.

- [ ] **Step 13: Commit Agar migration**

Run:

```powershell
git add samples\Game.Unity.Agar\Server\Hotfix\Features\BattleRuntimeFeature.cs samples\Game.Unity.Agar\Server\Hotfix\Features\BattleRuntimeRoomAllocation.cs samples\Game.Unity.Agar\Server\Hotfix\Features\StateStoreFeatures.cs samples\Game.Unity.Agar\Server\Hotfix\Services\StateStoreUserActorPlacement.cs samples\Game.Unity.Agar\Server\Hotfix\Server.Hotfix.csproj samples\Game.Unity.Agar\Server\Hotfix\Services\LoginService.cs samples\Game.Unity.Agar\Server\Hotfix\Services\PlayerService.cs samples\Game.Unity.Agar\Server\Hotfix\State\Matchmaking\MatchmakingBehavior.cs samples\Game.Unity.Agar\tests\BusinessLogic.Tests
git commit -m "Migrate Agar feature messages to typed hotfix commands"
```

Expected: commit succeeds and does not stage the Unity font asset.

---

## Task 9: Migrate Godot Sample And Generated Starter Template

**Files:**
- Modify: `samples/Game.Godot.Chat/Server/Hotfix/Features/ChatFeature.cs`
- Modify: `samples/Game.Godot.Chat/Server/App/BuildTag.props`
- Modify: `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`
- Modify: `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`
- Test: `tests/Lakona.Tool.Tests/Rendering/HotfixRendererTests.cs`
- Test: `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs`
- Test: `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`

- [ ] **Step 1: Update generated starter tests**

In `HotfixRendererTests`, extend the `ChatFeature` assertions:

```csharp
Assert.Contains("public static void Configure(HotfixFeatureContext context)", feature, StringComparison.Ordinal);
Assert.DoesNotContain("public override void Configure", feature, StringComparison.Ordinal);
Assert.DoesNotContain("IFeatureMessageHandler", feature, StringComparison.Ordinal);
```

In `ServerAppRendererTests`, change the expected generated BuildTag:

```csharp
Assert.Contains("<LakonaHotfixBuildTag>20260629.001</LakonaHotfixBuildTag>", buildTag, StringComparison.Ordinal);
```

- [ ] **Step 2: Run tool rendering tests and verify they fail**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore --filter "HotfixRendererTests|AddFiles_EmitsHotfixBuildTagPropsAndImportsIt"
```

Expected: fail because renderer still emits instance `Configure` and old BuildTag.

- [ ] **Step 3: Update generated ChatFeature**

In `HotfixRenderer.cs`, change `RenderChatFeature` to emit:

```csharp
public static void Configure(HotfixFeatureContext context)
{
    context.EnsureLocalActor<ChatRoomActor>(ChatRoomIds.Global);
}
```

No constructor and no service registration are needed.

- [ ] **Step 4: Update Godot sample ChatFeature**

Modify `ChatFeature.cs`:

```csharp
using Server.App.Chat;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Features
{
    [HotfixFeature("chat")]
    public sealed class ChatFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
            context.EnsureLocalActor<ChatRoomActor>(ChatRoomIds.Global);
        }
    }
}
```

- [ ] **Step 5: Update BuildTag defaults**

In `ServerAppRenderer.cs`, change:

```xml
<LakonaHotfixBuildTag>20260612.001</LakonaHotfixBuildTag>
```

to:

```xml
<LakonaHotfixBuildTag>20260629.001</LakonaHotfixBuildTag>
```

In `samples/Game.Godot.Chat/Server/App/BuildTag.props`, change the same value to `20260629.001`.

Do not change package writer or manifest unit tests that pass literal BuildTag values unrelated to renderer output.

- [ ] **Step 6: Run starter/tool tests**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore --filter "HotfixRendererTests|ServerAppRendererTests|GodotChatSample_UsesZeroTemplateHostAndHotfixOwnedChatFeature"
```

Expected: selected tests pass.

- [ ] **Step 7: Commit template migration**

Run:

```powershell
git add samples\Game.Godot.Chat\Server\Hotfix\Features\ChatFeature.cs samples\Game.Godot.Chat\Server\App\BuildTag.props src\Lakona.Tool\Rendering\Server\HotfixRenderer.cs src\Lakona.Tool\Rendering\Server\ServerAppRenderer.cs tests\Lakona.Tool.Tests\Rendering\HotfixRendererTests.cs tests\Lakona.Tool.Tests\Rendering\ServerAppRendererTests.cs tests\Lakona.Tool.Tests\Integration\ToolArchitectureScanTests.cs
git commit -m "Render hotfix features with static configure"
```

Expected: commit succeeds.

---

## Task 10: Documentation And Package Versions

**Files:**
- Modify: `docs/cluster.md`
- Modify: `docs/hotfix/architecture.md`
- Modify: `docs/hotfix/actor-behavior.md`
- Modify: `docs/configuration.md`
- Modify: `src/Lakona.Game.Cluster/Lakona.Game.Cluster.csproj`
- Modify: `src/Lakona.Game.Cluster/Diagnostics/ClusterDiagnostics.cs`
- Modify: `src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj`
- Modify: `src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj`
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Modify: `src/Lakona.Tool/Lakona.Tool.csproj`

- [ ] **Step 1: Update current docs**

Add these durable rules to `docs/hotfix/architecture.md`:

```markdown
### Feature Commands

Hotfix feature declarations use `public static void Configure(HotfixFeatureContext context)`.
The scanner does not construct feature classes during declaration. Runtime feature
command calls activate a fresh feature instance from the current hotfix service
provider, invoke a method shaped as
`ValueTask<TReply> Method(HotfixFeatureCommandCall<TRequest> call)`, and dispose
the feature instance after the returned `ValueTask` completes.

Feature command request and reply DTOs may be hotfix-owned types. Their wire
compatibility is governed by the hotfix BuildTag and the active hotfix generation,
not by the stable RPC service boundary validator.
```

Add to `docs/cluster.md`:

```markdown
Typed feature commands encode `FeatureCommandId` as an invariant-culture decimal
string in `FeatureMessageRequest.Kind`. Blank values, non-integers, zero,
negative values, and overflow values are rejected before deserialization with
`ClusterSendStatus.Rejected`.

`IFeatureCommandClient.SendAsync` selects any ready node that advertises the
feature. `SendToNodeAsync` sends the same typed command to an already selected
`ClusterNodeDescriptor`, which is the correct path after placement logic has
chosen a specific owner node.
```

Add to `docs/hotfix/actor-behavior.md`:

```markdown
Feature commands are capability-level orchestration points: placement checks,
route registration, local actor creation, and the first calls into actors. Once
a concrete actor exists, business logic should use generated actor refs rather
than treating feature commands as actor mailboxes.
```

Add to `docs/configuration.md`:

```markdown
Generated projects and ordinary hotfix business code should not register
hotfix-side `IFeatureMessageHandler` implementations. The default stable cluster
endpoint owns the low-level handler and dispatches typed commands into the
current hotfix feature command table. Advanced hosts may replace the stable
`IFeatureMessageHandler`, but that replacement owns the whole low-level feature
message surface.
```

- [ ] **Step 2: Bump package versions**

Apply exact version changes:

```xml
<!-- src/Lakona.Game.Cluster/Lakona.Game.Cluster.csproj -->
<Version>0.3.2</Version>

<!-- src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj -->
<Version>0.2.4</Version>

<!-- src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj -->
<Version>0.3.6</Version>

<!-- src/Lakona.Game.Server/Lakona.Game.Server.csproj -->
<Version>0.8.19</Version>

<!-- src/Lakona.Tool/Lakona.Tool.csproj -->
<Version>0.14.8</Version>
```

In `ClusterDiagnostics.cs`, change both version strings to:

```csharp
"0.3.2"
```

- [ ] **Step 3: Search for stale package/template versions**

Run:

```powershell
rg -n "0\.3\.1|0\.3\.5|0\.2\.3|0\.8\.18|0\.14\.7|20260612\.001" src samples tests docs -g '*.cs' -g '*.csproj' -g '*.props' -g '*.md'
```

Expected: remaining `20260612.001` matches are only literal package/manifest test data that do not represent generated starter defaults. Remaining old package versions should not appear in shippable package metadata or generated dependency constants.

- [ ] **Step 4: Run docs and targeted tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Cluster.Tests\Lakona.Game.Cluster.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore
```

Expected: all four projects pass.

- [ ] **Step 5: Commit docs and versions**

Run:

```powershell
git add docs\cluster.md docs\hotfix\architecture.md docs\hotfix\actor-behavior.md docs\configuration.md src\Lakona.Game.Cluster\Lakona.Game.Cluster.csproj src\Lakona.Game.Cluster\Diagnostics\ClusterDiagnostics.cs src\Lakona.Game.Server.Hotfix.Abstractions\Lakona.Game.Server.Hotfix.Abstractions.csproj src\Lakona.Game.Server.Hotfix\Lakona.Game.Server.Hotfix.csproj src\Lakona.Game.Server\Lakona.Game.Server.csproj src\Lakona.Tool\Lakona.Tool.csproj
git commit -m "Document typed hotfix feature commands"
```

Expected: commit succeeds.

---

## Task 11: Full Verification And Cleanup Readiness

**Files:**
- Inspect: all files modified by Tasks 1-10
- Do not stage: `samples/Game.Unity.Agar/Client/Assets/TextMesh Pro/Resources/Fonts & Materials/DotArenaCJK SDF.asset`

- [ ] **Step 1: Run source scans**

Run:

```powershell
rg -n "IFeatureMessageHandler|FeatureMessageRequest|FeatureMessageReply" samples\Game.Unity.Agar\Server\Hotfix src\Lakona.Tool\Rendering\Server samples\Game.Godot.Chat\Server\Hotfix -g '*.cs'
```

Expected: no matches in generated hotfix templates or sample hotfix business code. Matches in stable runtime packages and cluster RPC packages are allowed.

Run:

```powershell
rg -n "override void Configure\(HotfixFeatureContext|public override void Configure" src tests samples -g '*.cs'
```

Expected: no matches for hotfix feature declarations.

- [ ] **Step 2: Run core test projects**

Run:

```powershell
dotnet test tests\Lakona.Game.Cluster.Tests\Lakona.Game.Cluster.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore
dotnet test samples\Game.Unity.Agar\tests\BusinessLogic.Tests\BusinessLogic.Tests.csproj --no-restore
```

Expected: all projects pass.

- [ ] **Step 3: Run solution build**

Run:

```powershell
dotnet build Lakona.slnx --no-restore
```

Expected: build passes.

- [ ] **Step 4: Run Agar three-node smoke test**

Run:

```powershell
pwsh -NoProfile -File scripts\game\ci\test-agar-three-node.ps1
```

Expected: script completes successfully and room allocation works without `BattleRuntimeFeatureMessageHandler`.

- [ ] **Step 5: Inspect git status and diff**

Run:

```powershell
git status --short
git diff --stat
```

Expected: only planned source/docs/test changes plus the pre-existing Unity font asset if still present. Do not stage the Unity font asset.

- [ ] **Step 6: Final commit if verification required new fixes**

If verification required additional fixes, run `git status --short`, identify
which task owns each changed file, then rerun that task's exact `git add` and
`git commit` commands with only files from that task. Do not use `git add -A`,
`git add .`, or path wildcards from the repository root.

Expected: commit succeeds if fixes were needed, and the Unity font asset remains
unstaged.

---

## Self-Review

### Spec Coverage

- Static `Configure` and marker `HotfixGameFeature`: Tasks 3, 4, 9, 10.
- Constructor-DI instance command methods: Tasks 3, 5, 6, 8.
- `HotfixFeatureCommandCall<TRequest>` with cancellation and command context: Tasks 3, 5, 7.
- No hotfix-side `IFeatureMessageHandler` fan-out: Tasks 7, 8, 9, 11.
- Numeric wire `Kind` parsing and invalid value `Rejected`: Tasks 1, 7, 10.
- Node-pinned typed command path for Agar placement: Tasks 1, 2, 8.
- Agar MemoryPack compatibility for typed feature command DTOs: Task 8 adds generator references, MemoryPack attributes/orders, and configured serializer roundtrip tests.
- Expiration and cancellation distinction: Task 7 named handler tests cover expired requests, caller cancellation before dispatch, token-canceled command cancellation propagation, and detached `OperationCanceledException` mapping to `Failed`.
- BuildTag and package version impact: Tasks 9, 10.
- Durable docs in current `docs/**`: Task 10.
- Feature/Actor conceptual separation: Tasks 8 and 10.

### Placeholder Scan

The plan avoids unresolved placeholders. Every task names exact files, commands, and expected outcomes. Code snippets define all new public types introduced by later tasks.

### Type Consistency

- `FeatureCommandId.TryParse` is introduced in Task 1 and consumed by `HotfixFeatureMessageHandler` in Task 7.
- `IFeatureMessageBus.SendToNodeAsync` is introduced in Task 1 and consumed by `FeatureCommandClient.SendToNodeAsync` in Task 2.
- `HotfixFeatureCommandCall<TRequest>` is introduced in Task 3 and used by dispatch validation in Task 5 and sample feature methods in Task 8.
- `IHotfixFeatureCommandInvoker` and `HotfixFeatureCommandDescriptor` are introduced in Task 5, added to `HotfixRuntimeSnapshot` in Task 6, and consumed by the stable handler in Task 7.
- `EnsureActorReply`, `BattleRuntimeRoomAllocationReply`, and command ids are introduced before sample callers use them in Task 8.
