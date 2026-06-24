# Cluster Serializer And Remote Actor Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Lakona:Cluster:Serializer` the single serializer selector for node-to-node cluster RPC payloads and generated remote actor payloads, while keeping `LakonaInternalCodec` for client-facing framework control messages.

**Architecture:** Add an explicit cluster serializer setting, create one cluster `IRpcSerializer` from that setting, and adapt that serializer into the default `IRemoteActorSerializer`. Keep endpoint serializers endpoint-local. Add MemoryPack metadata only to cluster/server RPC DTOs that are serialized through cluster RPC; do not add serializer metadata or package references to `Lakona.Game.Abstractions`.

**Tech Stack:** .NET 10, `Microsoft.Extensions.DependencyInjection`, `Lakona.Rpc.Core.IRpcSerializer`, `JsonRpcSerializer`, `MemoryPackRpcSerializer`, MemoryPack source generator, xUnit.

---

## Non-Negotiable Boundaries

- Do not delete `src/Lakona.Game.Abstractions/Internal/LakonaInternalCodec.cs`.
- Do not add `MemoryPack`, `MemoryPack.Generator`, `Lakona.Rpc.Serializer.Json`, or `Lakona.Rpc.Serializer.MemoryPack` references to `src/Lakona.Game.Abstractions/Lakona.Game.Abstractions.csproj`.
- Do not add `MemoryPackable` or `MemoryPackOrder` attributes under `src/Lakona.Game.Abstractions`.
- Do not use a JSON-only `IRemoteActorSerializer` default.
- Do not infer the cluster serializer from the first client endpoint. Use only `Lakona:Cluster:Serializer`.
- Do not silently default a configured cluster to JSON. `Lakona:Cluster:Serializer` is required whenever `Lakona:Cluster` is configured.
- Do not allow an earlier bare `IRpcSerializer` DI registration to override `Lakona:Cluster:Serializer` for cluster traffic. The cluster endpoint registration owns the cluster `IRpcSerializer`.
- Do not make direct process-local `AddLakonaGameServerActors()` require or register `IRpcSerializer`; direct actor-runtime usage stays local-only unless the caller explicitly supplies remote actor serialization.
- Do not change client handshake, heartbeat, reliable push ack, or session termination notice to use endpoint or cluster `IRpcSerializer`.
- Preserve `init` properties on existing notification DTOs unless MemoryPack source generation fails. If MemoryPack requires public `set`, changing these internal cluster-dispatch DTOs to `set` is acceptable only with the focused MemoryPack roundtrip test in this plan passing.

## Clarification Decisions

- `Lakona:Cluster:Serializer` is mandatory for every configured cluster. Existing cluster tests, samples, and docs must be updated to set either `"json"` or `"memorypack"` explicitly.
- `Lakona:Cluster:Serializer` is the single built-in node-to-node selector. `AddLakonaGameClusterEndpoint` must replace the service collection's `IRpcSerializer` registration for cluster scope instead of using `TryAddSingleton`.
- The cluster RPC server must use the configured `IRpcSerializer` from DI, not independently create a serializer instance through a second factory call.
- The default `IRemoteActorSerializer` adapter is registered by cluster/full game-server wiring where the cluster `IRpcSerializer` exists. A direct local-only actor runtime does not register it.
- Documentation and sample configuration updates are in scope for this implementation because the new cluster serializer setting is a required runtime contract.

## File Structure

- Modify `src/Lakona.Game.Server/Configuration/LakonaGameClusterOptions.cs`: add `Serializer`.
- Modify `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`: bind `Lakona:Cluster:Serializer`.
- Modify `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedClusterEndpoint.cs`: expose resolved cluster serializer.
- Modify `src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs`: populate the resolved cluster serializer path.
- Modify `src/Lakona.Game.Server/Guardrails/Rules/ClusterEndpointRule.cs`: validate cluster serializer with `ULINK044`.
- Modify `src/Lakona.Game.Server/Hosting/LakonaEndpointRuntimeDefaults.cs`: add `CreateClusterSerializer`.
- Modify `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`: register cluster `IRpcSerializer` from `Lakona:Cluster:Serializer`.
- Modify `src/Lakona.Game.Server/Hosting/LakonaClusterRpcServerConfigurator.cs`: use the configured cluster serializer from DI instead of `new JsonRpcSerializer()`.
- Create `src/Lakona.Game.Server/Actors/RpcRemoteActorSerializer.cs`: default adapter from `IRpcSerializer` to `IRemoteActorSerializer`.
- Modify `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`: register default `IRemoteActorSerializer` only when cluster endpoint wiring is active.
- Modify `src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs`: register `LocalActorNodeIdentity` from `Lakona:Node:Id` before actor defaults.
- Modify `src/Lakona.Game.Cluster.Rpc/*.csproj` and DTO files under `src/Lakona.Game.Cluster.Rpc`: add MemoryPack source-generation support for cluster RPC DTOs.
- Modify `src/Lakona.Game.Server/*.csproj` and session DTO files under `src/Lakona.Game.Server/Sessions`: add MemoryPack support for cluster client-notification dispatch DTOs.
- Modify `samples/Game.Unity.Agar/Server/App/appsettings.*.json`: add explicit cluster serializer values to every distributed node config that has `Lakona:Cluster`.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`: assert the distributed sample cluster serializer values.
- Modify `src/Lakona.Game.Cluster/README.md` and `src/Lakona.Game.Cluster.Rpc/README.md`: update cluster JSON examples to include `Serializer`.
- Do not modify `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs` in the current single-node starter path. Add tests that keep the generated starter cluster-free.
- Bump package versions for every changed shippable package under `src/**`: `Lakona.Game.Server`, `Lakona.Game.Cluster.Rpc`, `Lakona.Game.Cluster` if its package README changes, and `Lakona.Tool` if tool rendering changes.

## Task 1: Bind And Validate `Lakona:Cluster:Serializer`

**Files:**
- Modify: `src/Lakona.Game.Server/Configuration/LakonaGameClusterOptions.cs`
- Modify: `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedClusterEndpoint.cs`
- Modify: `src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/Rules/ClusterEndpointRule.cs`
- Test: `tests/Lakona.Game.Server.Tests/Configuration/LakonaGameRuntimeOptionsTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`

- [ ] **Step 1: Write config binding tests**

Add these assertions to the existing cluster-binding tests in `LakonaGameRuntimeOptionsTests`:

```csharp
["Lakona:Cluster:Serializer"] = "memorypack",
```

and assert:

```csharp
Assert.Equal("memorypack", options.Cluster!.Serializer);
```

Also add a separate test:

```csharp
[Fact]
public void FromConfiguration_preserves_cluster_serializer()
{
    var configuration = BuildConfiguration(new Dictionary<string, string?>
    {
        ["Lakona:Node:Id"] = "gateway-1",
        ["Lakona:Cluster:Endpoint"] = "tcp://127.0.0.1:21002",
        ["Lakona:Cluster:Serializer"] = "json"
    });

    var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

    Assert.Equal("json", options.Cluster!.Serializer);
}
```

- [ ] **Step 2: Implement cluster serializer binding**

Add this property:

```csharp
public string Serializer { get; init; } = "";
```

Bind it in `BindCluster`:

```csharp
Serializer = section["Serializer"] ?? "",
```

- [ ] **Step 3: Extend resolved cluster endpoint**

Change `LakonaGameResolvedClusterEndpoint` so it carries serializer:

```csharp
public sealed record LakonaGameResolvedClusterEndpoint(
    LakonaGameResolvedValue<string> Endpoint,
    LakonaGameResolvedValue<string> Serializer,
    IReadOnlyList<string> Seeds);
```

Update `LakonaGameReadinessProbe.ToResolvedRuntime` to pass:

```csharp
new LakonaGameResolvedValue<string>(
    runtime.Cluster?.Serializer ?? "",
    LakonaGameValueSource.Configuration,
    "Lakona:Cluster:Serializer")
```

- [ ] **Step 4: Add validation**

In `ClusterEndpointRule`, add a known serializer set:

```csharp
private static readonly HashSet<string> KnownSerializers = new(StringComparer.OrdinalIgnoreCase)
{
    "json",
    "memorypack"
};
```

After endpoint URI validation, validate:

```csharp
var serializer = runtime.ClusterEndpoint.Serializer.Value;
if (string.IsNullOrWhiteSpace(serializer))
{
    yield return new LakonaGameDiagnostic(
        "ULINK044",
        LakonaGameDiagnosticSeverity.Error,
        "Lakona:Cluster:Serializer is required when Cluster is configured.",
        "Set Lakona:Cluster:Serializer to json or memorypack.");
}
else if (!KnownSerializers.Contains(serializer))
{
    yield return new LakonaGameDiagnostic(
        "ULINK044",
        LakonaGameDiagnosticSeverity.Error,
        $"Lakona:Cluster:Serializer '{serializer}' is unknown.",
        "Use json or memorypack.");
}
```

- [ ] **Step 5: Add guardrail tests**

In `LakonaGameRuntimeValidatorTests`, add tests that a configured cluster without serializer reports `ULINK044`, and a configured cluster with `Serializer = "protobuf"` reports `ULINK044` and mentions `protobuf`.

Also update existing helper-created valid cluster endpoints so they include a valid serializer. For example:

```csharp
private static LakonaGameResolvedClusterEndpoint TestClusterEndpoint(
    string endpoint,
    string serializer = "memorypack")
{
    return new LakonaGameResolvedClusterEndpoint(
        Endpoint: new LakonaGameResolvedValue<string>(endpoint, LakonaGameValueSource.Configuration, "Lakona:Cluster:Endpoint"),
        Serializer: new LakonaGameResolvedValue<string>(serializer, LakonaGameValueSource.Configuration, "Lakona:Cluster:Serializer"),
        Seeds: []);
}
```

- [ ] **Step 6: Update existing cluster test fixtures**

Add an explicit cluster serializer to every existing valid test fixture that configures `Lakona:Cluster:Endpoint`. Use `"memorypack"` when the endpoint serializer is already MemoryPack, and `"json"` only for tests that intentionally model JSON cluster traffic.

At minimum, update:

- `tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs`
- `tests/Lakona.Game.Server.Tests/LakonaGameServerHostingOptionsTests.cs`
- `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`
- `tests/Lakona.Game.Server.Tests/Configuration/LakonaGameRuntimeOptionsTests.cs`

Do not weaken the missing-serializer validation test added in Step 5.

- [ ] **Step 7: Run focused tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~LakonaGameRuntimeOptionsTests|FullyQualifiedName~LakonaGameRuntimeValidatorTests"
```

Expected: all selected tests pass after implementation.

## Task 2: Create Cluster Serializer Runtime Factory

**Files:**
- Modify: `src/Lakona.Game.Server/Hosting/LakonaEndpointRuntimeDefaults.cs`
- Modify: `tests/Lakona.Game.Server.Tests/Hosting/LakonaEndpointRuntimeDefaultsTests.cs`

- [ ] **Step 1: Add failing tests**

Add:

```csharp
[Theory]
[InlineData("json", typeof(JsonRpcSerializer))]
[InlineData("memorypack", typeof(MemoryPackRpcSerializer))]
public void CreateClusterSerializer_uses_cluster_serializer(string serializer, Type expectedType)
{
    var cluster = new LakonaGameClusterOptions
    {
        Endpoint = "tcp://127.0.0.1:21001",
        Serializer = serializer
    };

    var result = LakonaEndpointRuntimeDefaults.CreateClusterSerializer(cluster);

    Assert.IsType(expectedType, result);
}

[Fact]
public void CreateClusterSerializer_rejects_unknown_serializer()
{
    var cluster = new LakonaGameClusterOptions
    {
        Endpoint = "tcp://127.0.0.1:21001",
        Serializer = "protobuf"
    };

    var ex = Assert.Throws<InvalidOperationException>(() =>
        LakonaEndpointRuntimeDefaults.CreateClusterSerializer(cluster));

    Assert.Contains("protobuf", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Implement factory**

Add:

```csharp
public static IRpcSerializer CreateClusterSerializer(LakonaGameClusterOptions cluster)
{
    ArgumentNullException.ThrowIfNull(cluster);

    return Normalize(cluster.Serializer) switch
    {
        "json" => new JsonRpcSerializer(),
        "memorypack" => new MemoryPackRpcSerializer(),
        var serializer => throw new InvalidOperationException(
            $"Cluster serializer '{serializer}' is unknown. Use json or memorypack.")
    };
}
```

- [ ] **Step 3: Run focused tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter LakonaEndpointRuntimeDefaultsTests
```

Expected: all selected tests pass.

## Task 3: Wire Cluster DI And Cluster RPC Server To The Cluster Serializer

**Files:**
- Modify: `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaClusterRpcServerConfigurator.cs`
- Test: `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs`

- [ ] **Step 1: Add DI test**

Add a test that builds services with:

```csharp
Cluster = new LakonaGameClusterOptions
{
    Endpoint = "tcp://127.0.0.1:21001",
    Serializer = "memorypack"
}
```

Then call `AddLakonaGameClusterEndpoint()` and assert:

```csharp
Assert.IsType<MemoryPackRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
```

Add a second test that pre-registers a JSON serializer, then verifies cluster configuration still wins:

```csharp
[Fact]
public void AddLakonaGameClusterEndpoint_replaces_existing_rpc_serializer_with_configured_cluster_serializer()
{
    var services = new ServiceCollection();
    services.AddSingleton<IRpcSerializer, JsonRpcSerializer>();
    services.AddSingleton(new LakonaGameRuntimeOptions
    {
        Node = new LakonaGameNodeOptions { Id = "data-1" },
        Cluster = new LakonaGameClusterOptions
        {
            Endpoint = "tcp://127.0.0.1:21001",
            Serializer = "memorypack"
        }
    });

    services.AddLakonaGameClusterEndpoint();
    using var provider = services.BuildServiceProvider();

    Assert.IsType<MemoryPackRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
}
```

- [ ] **Step 2: Register configured cluster serializer as the cluster-owned service**

Replace:

```csharp
services.TryAddSingleton<IRpcSerializer, JsonRpcSerializer>();
```

with:

```csharp
services.RemoveAll<IRpcSerializer>();
services.AddSingleton<IRpcSerializer>(_ =>
    LakonaEndpointRuntimeDefaults.CreateClusterSerializer(runtimeOptions.Cluster));
```

This replacement is intentional. For built-in cluster wiring, `Lakona:Cluster:Serializer` wins over an earlier bare `IRpcSerializer` service registration. Do not use `TryAddSingleton` here.

- [ ] **Step 3: Use configured serializer in `LakonaClusterRpcServerConfigurator`**

Replace:

```csharp
context.Builder.UseSerializer(new JsonRpcSerializer());
```

with:

```csharp
context.Builder.UseSerializer(context.Services.GetRequiredService<IRpcSerializer>());
```

Add `using Microsoft.Extensions.DependencyInjection;` if needed. Keep the existing cluster endpoint null/empty guard before this call. Tests that instantiate `LakonaClusterRpcServerConfigurator` directly must register the same cluster `IRpcSerializer` in the `IServiceProvider` passed to `LakonaGameServerRpcContext`.

- [ ] **Step 4: Add an integration test for MemoryPack feature messages**

In `LakonaGameServerTests`, duplicate the shape of `ClusterEndpointRpcServerAcceptsFeatureMessageTransport`, but set:

```csharp
Serializer = "memorypack"
```

Register the server-side provider serializer before calling the configurator:

```csharp
services.AddSingleton<IRpcSerializer>(
    LakonaEndpointRuntimeDefaults.CreateClusterSerializer(runtime.Cluster));
```

and create the client factory with:

```csharp
await using var clientFactory = new ClusterClientFactory(
    new TcpClusterTransportFactory(),
    new MemoryPackRpcSerializer());
```

Expected request and reply assertions stay the same.

- [ ] **Step 5: Run focused tests**

Run only the DI test added in Step 1 now:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter AddLakonaGameClusterEndpoint_uses_configured_cluster_serializer
```

Expected: pass.

Run the broader command after Task 4 is complete:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~LakonaClusterEndpointServiceCollectionExtensionsTests|FullyQualifiedName~ClusterEndpointRpcServerAccepts"
```

Expected after Task 4: all selected tests pass.

## Task 4: Add MemoryPack Metadata To Cluster RPC DTOs

**Files:**
- Modify: `src/Lakona.Game.Cluster.Rpc/Lakona.Game.Cluster.Rpc.csproj`
- Modify: `src/Lakona.Game.Cluster.Rpc/Messaging/ClusterSendRequest.cs`
- Modify: `src/Lakona.Game.Cluster.Rpc/Messaging/ClusterSendReply.cs`
- Modify: `src/Lakona.Game.Cluster.Rpc/Messaging/FeatureSendRequest.cs`
- Modify: `src/Lakona.Game.Cluster.Rpc/Messaging/FeatureSendReply.cs`
- Modify: `src/Lakona.Game.Cluster.Rpc/Routes/RouteDirectoryMessages.cs`
- Modify: `src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryMessages.cs`
- Modify: `tests/Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj`
- Create: `tests/Lakona.Game.Cluster.Rpc.Tests/ClusterRpcMemoryPackDtoTests.cs`

- [ ] **Step 1: Add direct MemoryPack references**

Add to `Lakona.Game.Cluster.Rpc.csproj`:

```xml
<PackageReference Include="MemoryPack" Version="1.21.4" />
<PackageReference Include="MemoryPack.Generator" Version="1.21.4">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

Add a project reference to tests:

```xml
<ProjectReference Include="..\..\src\Lakona.Rpc.Serializer.MemoryPack\Lakona.Rpc.Serializer.MemoryPack.csproj" />
```

- [ ] **Step 2: Add MemoryPack attributes**

In each DTO file, add:

```csharp
using MemoryPack;
```

Annotate exactly these classes:

- `ClusterSendRequest`
- `ClusterSendReply`
- `FeatureSendRequest`
- `FeatureSendReply`
- `RouteLocationDto`
- `RouteRegisterRequest`
- `RouteRegisterReply`
- `RouteResolveRequest`
- `RouteResolveReply`
- `RouteUnregisterRequest`
- `RouteUnregisterReply`
- `RouteRefreshLeaseRequest`
- `RouteRefreshLeaseReply`
- `RouteExpireRequest`
- `RouteExpireReply`
- `RouteClearByNodeRequest`
- `RouteClearByNodeEpochRequest`
- `RouteClearReply`
- `NodeEndpointDto`
- `NodeFeatureDto`
- `NodeRegistrationDto`
- `NodeRecordDto`
- `NodeDirectoryClientQueryDto`
- `NodeRegisterRequest`
- `NodeRegisterReply`
- `NodeHeartbeatRequest`
- `NodeHeartbeatReply`
- `NodeUpdateStateRequest`
- `NodeUpdateStateReply`
- `NodeResolveRequest`
- `NodeResolveReply`
- `NodeQueryRequest`
- `NodeQueryReply`
- `NodeExpireRequest`
- `NodeExpireReply`

For each class, add:

```csharp
[MemoryPackable(GenerateType.VersionTolerant)]
```

For every serialized property, add `[MemoryPackOrder(n)]` starting at `0` in the current declaration order. Preserve existing property names, nullability, defaults, and public setters.

Example for `ClusterSendRequest`:

```csharp
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ClusterSendRequest
{
    [MemoryPackOrder(0)] public string Route { get; set; } = string.Empty;
    [MemoryPackOrder(1)] public string Kind { get; set; } = string.Empty;
    [MemoryPackOrder(2)] public byte[] Payload { get; set; } = Array.Empty<byte>();
    [MemoryPackOrder(3)] public DateTimeOffset ExpiresAt { get; set; }
    [MemoryPackOrder(4)] public string SourceNode { get; set; } = string.Empty;
    [MemoryPackOrder(5)] public string? CorrelationId { get; set; }
    [MemoryPackOrder(6)] public string? TraceId { get; set; }
    [MemoryPackOrder(7)] public string? OrderedBy { get; set; }
}
```

Every annotated class must be `partial`.

- [ ] **Step 3: Add roundtrip tests**

Create `ClusterRpcMemoryPackDtoTests.cs` with tests that serialize and deserialize:

```csharp
var serializer = new MemoryPackRpcSerializer();
using var frame = serializer.SerializeFrame(new ClusterSendRequest
{
    Route = "actor:room/1",
    Kind = "join",
    Payload = [1, 2, 3],
    ExpiresAt = new DateTimeOffset(2026, 6, 24, 1, 2, 3, TimeSpan.Zero),
    SourceNode = "gateway-1",
    CorrelationId = "corr-1",
    TraceId = "trace-1",
    OrderedBy = "room/1"
});
var decoded = serializer.Deserialize<ClusterSendRequest>(frame.Memory);
Assert.Equal("actor:room/1", decoded.Route);
Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Payload);
Assert.Equal("gateway-1", decoded.SourceNode);
```

Add separate roundtrips for `FeatureSendRequest`, `RouteRegisterRequest` with a populated `RouteLocationDto`, and `NodeRegisterRequest` with a populated `NodeRegistrationDto`.

- [ ] **Step 4: Run cluster RPC tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Cluster.Rpc.Tests\Lakona.Game.Cluster.Rpc.Tests.csproj --filter ClusterRpcMemoryPackDtoTests
```

Expected: all selected tests pass.

## Task 5: Add MemoryPack Support For Cluster Client Notification Dispatch DTOs

**Files:**
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Modify: `src/Lakona.Game.Server/Sessions/ClientNotificationCommand.cs`
- Modify: `src/Lakona.Game.Server/Sessions/ClusterClientNotificationProtocol.cs`
- Create: `tests/Lakona.Game.Server.Tests/ClientNotificationMemoryPackDtoTests.cs`

- [ ] **Step 1: Add direct MemoryPack references to `Lakona.Game.Server`**

Add:

```xml
<PackageReference Include="MemoryPack" Version="1.21.4" />
<PackageReference Include="MemoryPack.Generator" Version="1.21.4">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

- [ ] **Step 2: Annotate notification dispatch DTOs**

Add `using MemoryPack;`, make these classes `partial`, and annotate with `MemoryPackable(GenerateType.VersionTolerant)` plus stable `MemoryPackOrder` values:

- `ClientNotificationCommand`
- `ClientNotificationArgument`
- `ClientNotificationDispatchRequest`
- `ClientNotificationDispatchReply`

Keep `ClientNotificationCommand.ToSessionKey()` unannotated as a method.

Preserve the current `init` accessors first:

```csharp
[MemoryPackOrder(0)] public string OwnerKey { get; init; } = "";
```

Only if the MemoryPack source generator rejects `init` during build may these four internal cluster-dispatch DTOs change from `init` to public `set`:

```csharp
[MemoryPackOrder(0)] public string OwnerKey { get; set; } = "";
```

Do not change DTO names, nullability, default values, or collection types while making that fallback. If the fallback is used, mention it in the implementation summary and keep the MemoryPack roundtrip test below as proof.

- [ ] **Step 3: Add roundtrip test**

Create:

```csharp
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Serializer.MemoryPack;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClientNotificationMemoryPackDtoTests
{
    [Fact]
    public void MemoryPack_roundtrips_client_notification_dispatch_request()
    {
        var serializer = new MemoryPackRpcSerializer();
        using var frame = serializer.SerializeFrame(new ClientNotificationDispatchRequest
        {
            Command = new ClientNotificationCommand
            {
                OwnerKey = "player-1",
                SessionId = "session-1",
                Generation = 2,
                CallbackContractType = "Game.ILoginCallback",
                MethodName = "OnMatchedAsync",
                Arguments =
                [
                    new ClientNotificationArgument
                    {
                        TypeName = "System.String",
                        Payload = [7, 8, 9]
                    }
                ]
            }
        });

        var decoded = serializer.Deserialize<ClientNotificationDispatchRequest>(frame.Memory);

        Assert.NotNull(decoded.Command);
        Assert.Equal("player-1", decoded.Command.OwnerKey);
        Assert.Equal(2, decoded.Command.Generation);
        var argument = Assert.Single(decoded.Command.Arguments);
        Assert.Equal("System.String", argument.TypeName);
        Assert.Equal(new byte[] { 7, 8, 9 }, argument.Payload);
    }
}
```

- [ ] **Step 4: Run focused test**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter ClientNotificationMemoryPackDtoTests
```

Expected: pass.

## Task 6: Add Default Remote Actor Serializer Adapter

**Files:**
- Create: `src/Lakona.Game.Server/Actors/RpcRemoteActorSerializer.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`
- Test: `tests/Lakona.Game.Server.Tests/ActorRuntimeTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Protect local-only actor runtime behavior**

In `ActorRuntimeTests`, add:

```csharp
[Fact]
public void AddLakonaGameServerActors_does_not_register_remote_actor_serializer_without_cluster_serializer()
{
    using var provider = new ServiceCollection()
        .AddLakonaGameServerActors()
        .BuildServiceProvider();

    Assert.Null(provider.GetService<IRemoteActorSerializer>());
}
```

Direct actor-runtime usage remains process-local. Do not register the default remote actor serializer in `ActorServiceCollectionExtensions`.

- [ ] **Step 2: Add cluster adapter test**

In `LakonaClusterEndpointServiceCollectionExtensionsTests`, add:

```csharp
[Fact]
public void AddLakonaGameClusterEndpoint_registers_remote_actor_serializer_adapter()
{
    var services = new ServiceCollection();
    services.AddSingleton(new LakonaGameRuntimeOptions
    {
        Node = new LakonaGameNodeOptions { Id = "data-1" },
        Cluster = new LakonaGameClusterOptions
        {
            Endpoint = "tcp://127.0.0.1:21001",
            Serializer = "memorypack"
        }
    });

    services.AddLakonaGameClusterEndpoint();
    using var provider = services.BuildServiceProvider();

    var serializer = provider.GetRequiredService<IRemoteActorSerializer>();
    var payload = serializer.Serialize(new ClientNotificationDispatchReply { Status = 7 });
    var decoded = serializer.Deserialize<ClientNotificationDispatchReply>(payload);

    Assert.IsType<MemoryPackRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
    Assert.Equal(7, decoded.Status);
}
```

Add the necessary `using Lakona.Game.Server.Actors;`, `using Lakona.Game.Server.Sessions;`, `using Lakona.Rpc.Core;`, and `using Lakona.Rpc.Serializer.MemoryPack;`.

- [ ] **Step 3: Implement adapter**

Create:

```csharp
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Actors;

public sealed class RpcRemoteActorSerializer : IRemoteActorSerializer
{
    private readonly IRpcSerializer _serializer;

    public RpcRemoteActorSerializer(IRpcSerializer serializer)
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

- [ ] **Step 4: Register adapter only with active cluster endpoint wiring**

In `AddLakonaGameClusterEndpoint`, after the configured `IRpcSerializer` registration, add:

```csharp
services.TryAddSingleton<IRemoteActorSerializer, RpcRemoteActorSerializer>();
```

Keep this as `TryAddSingleton` so a caller can intentionally provide a custom actor-facing serializer, but do not use a JSON-only fallback. The built-in default adapts the configured cluster `IRpcSerializer`.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter "AddLakonaGameServerActors_does_not_register_remote_actor_serializer_without_cluster_serializer|AddLakonaGameClusterEndpoint_registers_remote_actor_serializer_adapter"
```

Expected: both selected tests pass.

## Task 7: Preserve Node Identity For Actor Route Registration

**Files:**
- Modify: `src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs`
- Test: `tests/Lakona.Game.Server.Tests/ActorRuntimeTests.cs`

- [ ] **Step 1: Add test**

Add:

```csharp
[Fact]
public void AddLakonaGameServer_uses_configured_node_id_for_local_actor_identity()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "battle-1"
        })
        .Build();

    using var provider = new ServiceCollection()
        .AddLakonaGameServer(configuration)
        .BuildServiceProvider();

    Assert.Equal(new NodeId("battle-1"), provider.GetRequiredService<LocalActorNodeIdentity>().NodeId);
}
```

- [ ] **Step 2: Register node identity before actor defaults**

In `LakonaGameServerServiceCollectionExtensions`, add `using Lakona.Game.Cluster;`.

Add this private helper to the class:

```csharp
private static LakonaGameRuntimeOptions FindRuntimeOptions(IServiceCollection services)
{
    for (var i = services.Count - 1; i >= 0; i--)
    {
        var descriptor = services[i];
        if (descriptor.ServiceType == typeof(LakonaGameRuntimeOptions) &&
            descriptor.ImplementationInstance is LakonaGameRuntimeOptions options)
        {
            return options;
        }
    }

    return new LakonaGameRuntimeOptions();
}
```

Inside `AddLakonaGameServer(LakonaGameHostingOptions options, IConfiguration? configuration)`, immediately after:

```csharp
services.TryAddSingleton(new LakonaGameRuntimeOptions());
```

add:

```csharp
var runtimeOptions = FindRuntimeOptions(services);
services.TryAddSingleton(new LocalActorNodeIdentity(new NodeId(runtimeOptions.Node.Id)));
```

This must happen before:

```csharp
services.AddLakonaGameServerActors(actorOptions => options.Actors.ApplyTo(actorOptions));
```

Keep `AddLakonaGameServerActors()` defaulting to `"local"` for direct actor-runtime unit tests and process-local hosts that do not use full game-server configuration.

- [ ] **Step 3: Run focused test**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter AddLakonaGameServer_uses_configured_node_id_for_local_actor_identity
```

Expected: pass.

## Task 8: Update Existing Docs, Package READMEs, And Distributed Samples

**Files:**
- Modify: `samples/Game.Unity.Agar/Server/App/appsettings.gateway-1.json`
- Modify: `samples/Game.Unity.Agar/Server/App/appsettings.data-1.json`
- Modify: `samples/Game.Unity.Agar/Server/App/appsettings.battle-1.json`
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`
- Modify: `src/Lakona.Game.Cluster/README.md`
- Modify: `src/Lakona.Game.Cluster.Rpc/README.md`
- Verify: `docs/configuration.md`
- Verify: `docs/cluster.md`

- [ ] **Step 1: Add explicit cluster serializer to Agar distributed configs**

Every `samples/Game.Unity.Agar/Server/App/appsettings.*.json` file that contains `Lakona:Cluster` must include:

```json
"Serializer": "memorypack"
```

Place it next to `Endpoint`:

```json
"Cluster": {
  "Endpoint": "tcp://10.0.0.2:21002",
  "Serializer": "memorypack",
  "Seeds": [ "tcp://10.0.0.1:21001" ]
}
```

Use `memorypack` for `gateway-1`, `data-1`, and `battle-1` so all distributed Agar nodes use the same cluster serializer as the sample's client-facing business RPC endpoints.

- [ ] **Step 2: Assert sample cluster serializer values**

In `DistributedTopologyConfigurationTests`, add assertions to the existing distributed config tests:

```csharp
Assert.Equal("memorypack", lakona.GetProperty("Cluster").GetProperty("Serializer").GetString());
```

Add this assertion in:

- `DataNodeOwnsStateAndClusterEndpointWithoutClientEndpoints`
- `GatewayNodeOwnsOnlyWebSocketClientEndpoint`
- `BattleNodeOwnsRuntimeAndKcpEndpoint`

- [ ] **Step 3: Update package README cluster examples**

In `src/Lakona.Game.Cluster/README.md` and `src/Lakona.Game.Cluster.Rpc/README.md`, add `"Serializer": "memorypack"` or `"Serializer": "json"` to every JSON example that includes `Lakona:Cluster`.

Use one serializer consistently inside each multi-node example. For Agar-shaped examples, prefer `memorypack` because the sample endpoints already use MemoryPack. For generic explanatory examples, `json` is acceptable if all nodes in that example use `json`.

If an example includes `Lakona:Endpoints`, keep endpoint `Serializer` separate from cluster `Serializer`; do not imply endpoint serializer controls cluster traffic.

- [ ] **Step 4: Verify canonical docs are consistent**

Check `docs/configuration.md` and `docs/cluster.md` after the implementation. They must state all of the following:

- `Lakona:Cluster:Serializer` is required whenever `Lakona:Cluster` is configured.
- Supported values are `json` and `memorypack`.
- All communicating cluster nodes must use the same cluster serializer.
- Remote actor payloads follow `Lakona:Cluster:Serializer`.
- `LakonaInternalCodec` remains the fixed client-facing framework-control codec.

- [ ] **Step 5: Run sample config tests**

Run:

```powershell
dotnet test samples\Game.Unity.Agar\tests\BusinessLogic.Tests\BusinessLogic.Tests.csproj --filter DistributedTopologyConfigurationTests
```

Expected: all selected tests pass.

## Task 9: Tooling And Generated Configuration Guardrail

**Files:**
- Test: `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs`
- Test: `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`

- [ ] **Step 1: Keep single-node starter cluster-free**

The current single-node zero-template starter must still omit `Lakona:Cluster`. Add this assertion to `ServerAppRendererTests.AddFiles_UsesCompactLakonaAppsettingsShape`:

```csharp
Assert.False(lakona.TryGetProperty("Cluster", out _));
```

- [ ] **Step 2: Do not change current generated appsettings output**

Do not add `Lakona:Cluster` to the current single-node starter output in this implementation. The tracked docs already state the future cluster-template requirement; current code does not expose a cluster template path to update.

- [ ] **Step 3: Keep architecture scan**

Do not weaken `FrameworkInternalDtos_DoNotUseEndpointSerializerMetadata`. It must continue scanning `src/Lakona.Game.Abstractions` for absence of:

```txt
MemoryPackable
MemoryPackOrder
Lakona.Rpc.Serializer.Json
Lakona.Rpc.Serializer.MemoryPack
```

- [ ] **Step 4: Run tool tests**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --filter "FullyQualifiedName~ServerAppRendererTests|FullyQualifiedName~ToolArchitectureScanTests"
```

Expected: pass.

## Task 10: Version Bumps

**Files:**
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Modify: `src/Lakona.Game.Cluster.Rpc/Lakona.Game.Cluster.Rpc.csproj`
- Modify: `src/Lakona.Game.Cluster/Lakona.Game.Cluster.csproj` if `src/Lakona.Game.Cluster/README.md` changed.
- Modify: `src/Lakona.Tool/Lakona.Tool.csproj` only if `src/Lakona.Tool/**` changed.

- [ ] **Step 1: Bump changed package versions**

Apply these minimum bumps:

```xml
<!-- src/Lakona.Game.Server/Lakona.Game.Server.csproj -->
<Version>0.8.3</Version>

<!-- src/Lakona.Game.Cluster.Rpc/Lakona.Game.Cluster.Rpc.csproj -->
<Version>0.2.1</Version>

<!-- src/Lakona.Game.Cluster/Lakona.Game.Cluster.csproj, only if src/Lakona.Game.Cluster/README.md changed -->
<Version>0.3.1</Version>

<!-- src/Lakona.Tool/Lakona.Tool.csproj, only if Tool source changed -->
<Version>0.14.1</Version>
```

- [ ] **Step 2: Search for pinned package versions**

Run:

```powershell
rg -n "Lakona.Game.Server|Lakona.Game.Cluster.Rpc|Lakona.Game.Cluster|Lakona.Tool|0\.8\.2|0\.2\.0|0\.3\.0|0\.14\.0" src tests samples docs
```

Update only references that are package version pins or generated package version constants affected by this release. Do not edit historical notes under `docs/superpowers/**`.

## Task 11: Full Verification

**Files:**
- No production files.

- [ ] **Step 1: Build**

Run:

```powershell
dotnet build Lakona.slnx
```

Expected: build succeeds with no new warnings from MemoryPack source generation.

- [ ] **Step 2: Run focused test projects**

Run:

```powershell
dotnet test tests\Lakona.Game.Cluster.Rpc.Tests\Lakona.Game.Cluster.Rpc.Tests.csproj --no-build
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-build
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-build
```

Expected: all pass.

- [ ] **Step 3: Final source scans**

Run:

```powershell
rg -n "new JsonRpcSerializer\\(\\)|JsonRemoteActorSerializer|LakonaInternalCodec" src\Lakona.Game.Server src\Lakona.Game.Cluster.Rpc src\Lakona.Game.Abstractions
rg -n "MemoryPackable|MemoryPackOrder|Lakona.Rpc.Serializer" src\Lakona.Game.Abstractions
```

Expected:

- `new JsonRpcSerializer()` may remain for endpoint JSON factories, but not in cluster RPC server configuration.
- `JsonRemoteActorSerializer` must not exist in production source.
- `LakonaInternalCodec` remains in `Lakona.Game.Abstractions` and client/server framework-control call sites.
- The second scan returns no matches.

## Acceptance Criteria

- `Lakona:Cluster:Serializer` is required and validated when `Lakona:Cluster` is configured.
- Cluster RPC client and server use the DI-registered cluster `IRpcSerializer` created from `Lakona:Cluster:Serializer` for `json` and `memorypack`.
- Earlier bare `IRpcSerializer` registrations do not override the built-in cluster serializer selection.
- Generated remote actor request/reply payloads use an `IRemoteActorSerializer` default that adapts the configured cluster `IRpcSerializer`.
- Direct local-only `AddLakonaGameServerActors()` usage does not require or register `IRpcSerializer`.
- MemoryPack cluster DTO support is explicit and source-generated for AOT safety.
- `Lakona.Game.Abstractions` remains serializer-package-free and metadata-free.
- Existing distributed sample configs, package README cluster examples, and tests include explicit cluster serializer values.
- Single-node starter generated `appsettings.json` remains compact and cluster-free.
- Shippable package versions are bumped for every modified package under `src/**`.
