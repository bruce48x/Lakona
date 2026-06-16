# Distributed Feature Cluster Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the V1 distributed Feature, endpoint, RPC service, message bus, client notification relay, and Agar three-node acceptance model defined in `docs/game/distributed-feature-cluster-model.md`.

**Architecture:** Treat `Lakona` configuration as the runtime source of truth. Feature is the only cluster-discoverable application capability; RPC service exposure is endpoint-local and independent from Feature; remote client notifications route through a gateway-owned `client-session` route instead of passing callback objects across nodes.

**Tech Stack:** .NET, C#, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Configuration`, xUnit, Lakona RPC server/client runtime, Lakona cluster directory and route directory packages, MemoryPack-generated sample contracts.

---

## Source Of Truth

Implement exactly the V1 contract in `docs/game/distributed-feature-cluster-model.md`.

Do not reintroduce or preserve these concepts in new APIs, config, diagnostics, generated templates, or sample code:

- `Lakona:Cluster:Services`
- `ClusterService`
- `NodeServiceDescriptor`
- `NodeRegistration.Services`
- `NodeRecord.Services`
- `ClusterFeature`
- endpoint `Name`
- required fluent `Program.cs` feature declarations for generated projects
- config coupling between `RpcService` and `Feature`
- cross-node callback object transport
- gateway database connection in the Agar acceptance path

The old `Lakona.Game` configuration root may be read only for explicit migration tests. Every new sample and every new diagnostic path must use `Lakona`.

## File Map

Cluster public contract changes:

- Modify: `src/Lakona.Game.Cluster/Nodes/NodeRegistration.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeRecord.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeDirectoryQuery.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/ClusterNodeDescriptor.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/ClusterNodeDiscovery.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/IClusterNodeDiscovery.cs`
- Create: `src/Lakona.Game.Cluster/Nodes/FeatureName.cs`
- Create: `src/Lakona.Game.Cluster/Nodes/NodeFeatureDescriptor.cs`
- Delete: `src/Lakona.Game.Cluster/Nodes/ClusterFeature.cs`
- Delete: `src/Lakona.Game.Cluster/Nodes/NodeServiceDescriptor.cs`
- Modify tests: `tests/Lakona.Game.Cluster.Tests/NodeDirectoryModelTests.cs`
- Modify tests: `tests/Lakona.Game.Cluster.Tests/InMemoryNodeDirectoryTests.cs`
- Modify tests: `tests/Lakona.Game.Cluster.Tests/ClusterNodeDiscoveryTests.cs`
- Modify tests: `tests/Lakona.Game.Cluster.Rpc.Tests/NodeDirectoryClientTests.cs`
- Modify tests: `tests/Lakona.Game.Cluster.Sql.Tests/SqlNodeDirectoryTests.cs`

Runtime configuration and endpoint changes:

- Modify: `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`
- Modify: `src/Lakona.Game.Server/Configuration/LakonaGameEndpointOptions.cs`
- Modify: `src/Lakona.Game.Server/Configuration/ClusterOptions.cs`
- Modify: `src/Lakona.Game.Server/Features/LakonaGameEndpointCatalog.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/Rules/EndpointRule.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/Rules/ClusterEndpointRule.cs`
- Delete: `src/Lakona.Game.Server/Guardrails/Rules/ClusterServiceGraphRule.cs`
- Modify: `src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs`
- Modify tests: `tests/Lakona.Game.Server.Tests/Configuration/LakonaGameRuntimeOptionsTests.cs`
- Modify tests: `tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`

Feature discovery and lifecycle changes:

- Modify: `src/Lakona.Game.Server/Features/LakonaGameFeature.cs`
- Modify: `src/Lakona.Game.Server/Features/LakonaGameFeatureContext.cs`
- Modify: `src/Lakona.Game.Server/Features/LakonaGameFeatureCatalog.cs`
- Modify: `src/Lakona.Game.Server/Features/LakonaGameFeatureCatalogBuilder.cs`
- Modify: `src/Lakona.Game.Server/Features/FeatureServiceCollectionExtensions.cs`
- Create: `src/Lakona.Game.Server/Features/LakonaGameFeatureDiscovery.cs`
- Create: `src/Lakona.Game.Server/Features/LakonaGameFeatureName.cs`
- Create: `src/Lakona.Game.Server/Features/LakonaGameFeatureHostedService.cs`
- Create tests: `tests/Lakona.Game.Server.Tests/Features/LakonaGameFeatureDiscoveryTests.cs`
- Modify tests: `tests/Lakona.Game.Server.Tests/FeatureBuilderTests.cs`

RPC service binder discovery:

- Modify: `src/Lakona.Game.Server/Hosting/RpcServersHostedService.cs`
- Modify: `src/Lakona.Game.Server/Hosting/IRpcServerConfigurator.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaGameServerBuilder.cs`
- Create: `src/Lakona.Game.Server/Hosting/LakonaRpcServiceAttribute.cs`
- Create: `src/Lakona.Game.Server/Hosting/LakonaRpcServiceBinder.cs`
- Create: `src/Lakona.Game.Server/Hosting/LakonaRpcServiceCatalog.cs`
- Create: `src/Lakona.Game.Server/Hosting/LakonaEndpointRpcServerConfigurator.cs`
- Create tests: `tests/Lakona.Game.Server.Tests/Hosting/LakonaRpcServiceCatalogTests.cs`
- Create tests: `tests/Lakona.Game.Server.Tests/Hosting/LakonaEndpointRpcServerConfiguratorTests.cs`

Feature-addressed request/reply message bus:

- Modify: `src/Lakona.Game.Cluster/Messaging/ClusterSendStatus.cs`
- Modify: `src/Lakona.Game.Cluster/Messaging/ClusterMessage.cs`
- Modify: `src/Lakona.Game.Cluster/Messaging/IClusterMessageHandler.cs`
- Modify: `src/Lakona.Game.Cluster/Messaging/ClusterNodeSender.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/ClusterReply.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/ClusterRequest.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/IFeatureMessageBus.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/IFeatureMessageHandler.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/FeatureMessageBus.cs`
- Create tests: `tests/Lakona.Game.Cluster.Tests/FeatureMessageBusTests.cs`

Client notification relay:

- Modify: `src/Lakona.Game.Server/Sessions/IGameSessionDirectory.cs`
- Modify: `src/Lakona.Game.Server/Sessions/InMemoryGameSessionDirectory.cs`
- Create: `src/Lakona.Game.Server/Sessions/IClientNotificationRelay.cs`
- Create: `src/Lakona.Game.Server/Sessions/ClientNotificationRelay.cs`
- Create: `src/Lakona.Game.Server/Sessions/ClientNotificationRouteKey.cs`
- Create: `src/Lakona.Game.Server/Sessions/ClientNotificationStatus.cs`
- Create tests: `tests/Lakona.Game.Server.Tests/ClientNotificationRelayTests.cs`

Agar acceptance sample:

- Modify: `samples/Game.Unity.Agar/Server/App/Program.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/Features/GatewayCoreFeature.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/Features/GatewayBusinessFeature.cs`
- Create: `samples/Game.Unity.Agar/Server/App/Features/DatabaseFeature.cs`
- Create: `samples/Game.Unity.Agar/Server/App/Features/StateStoreFeature.cs`
- Create: `samples/Game.Unity.Agar/Server/App/Features/MatchmakingFeature.cs`
- Create: `samples/Game.Unity.Agar/Server/App/Features/LeaderboardFeature.cs`
- Create: `samples/Game.Unity.Agar/Server/App/Features/BattleRuntimeFeature.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/Hosting/DefaultControlPlaneRpcServerConfigurator.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/Hosting/DefaultRealtimeRpcServerConfigurator.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/appsettings.json`
- Create: `samples/Game.Unity.Agar/Server/App/appsettings.data-1.json`
- Create: `samples/Game.Unity.Agar/Server/App/appsettings.gateway-1.json`
- Create: `samples/Game.Unity.Agar/Server/App/appsettings.battle-1.json`
- Modify: `samples/Game.Unity.Agar/docker-compose.yml`
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/GatewayConfigurationTests.cs`
- Create tests: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`
- Create tests: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/RemoteNotificationRelayExampleTests.cs`

## Task 1: Replace Cluster Service Public Model With Feature Public Model

**Files:**
- Create: `src/Lakona.Game.Cluster/Nodes/FeatureName.cs`
- Create: `src/Lakona.Game.Cluster/Nodes/NodeFeatureDescriptor.cs`
- Delete: `src/Lakona.Game.Cluster/Nodes/ClusterFeature.cs`
- Delete: `src/Lakona.Game.Cluster/Nodes/NodeServiceDescriptor.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeRegistration.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeRecord.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeDirectoryQuery.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/ClusterNodeDescriptor.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/IClusterNodeDiscovery.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/ClusterNodeDiscovery.cs`
- Modify: `tests/Lakona.Game.Cluster.Tests/NodeDirectoryModelTests.cs`

- [ ] **Step 1: Write failing public model tests**

Add these tests to `tests/Lakona.Game.Cluster.Tests/NodeDirectoryModelTests.cs`:

```csharp
[Fact]
public void FeatureNameRejectsBlankValue()
{
    var ex = Assert.Throws<ArgumentException>(() => new FeatureName(" "));
    Assert.Contains("Feature name is required", ex.Message, StringComparison.Ordinal);
}

[Fact]
public void NodeFeatureDescriptorCopiesMetadata()
{
    var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["region"] = "cn-east",
        ["capacity"] = "small"
    };

    var descriptor = new NodeFeatureDescriptor("battle-runtime", metadata);
    metadata["region"] = "changed";

    Assert.Equal("battle-runtime", descriptor.Name);
    Assert.Equal("cn-east", descriptor.Metadata["region"]);
    Assert.Equal("small", descriptor.Metadata["capacity"]);
}

[Fact]
public void NodeRegistrationAllowsNoApplicationFeatures()
{
    var registration = new NodeRegistration(
        "game",
        new NodeId("gateway-1"),
        new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
        {
            ["cluster"] = new NodeEndpoint("tcp://127.0.0.1:21002"),
            ["websocket"] = new NodeEndpoint("ws://127.0.0.1:20000/ws")
        },
        Array.Empty<NodeFeatureDescriptor>(),
        DateTimeOffset.UtcNow.AddMinutes(1));

    Assert.Empty(registration.Features);
}
```

- [ ] **Step 2: Run the failing cluster model tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --filter "FullyQualifiedName~NodeDirectoryModelTests"
```

Expected: compile failure for missing `FeatureName`, missing `NodeFeatureDescriptor`, or stale `NodeRegistration` constructor.

- [ ] **Step 3: Add `FeatureName`**

Create `src/Lakona.Game.Cluster/Nodes/FeatureName.cs`:

```csharp
using System;

namespace Lakona.Game.Cluster
{
    public readonly struct FeatureName : IEquatable<FeatureName>
    {
        public FeatureName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Feature name is required.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(FeatureName other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is FeatureName other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(FeatureName left, FeatureName right) => left.Equals(right);

        public static bool operator !=(FeatureName left, FeatureName right) => !left.Equals(right);

        public static implicit operator FeatureName(string value) => new FeatureName(value);
    }
}
```

- [ ] **Step 4: Add `NodeFeatureDescriptor`**

Create `src/Lakona.Game.Cluster/Nodes/NodeFeatureDescriptor.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class NodeFeatureDescriptor
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public NodeFeatureDescriptor(string name, IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Feature name is required.", nameof(name));
            }

            Name = name;
            Metadata = CopyMetadata(metadata);
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        private static IReadOnlyDictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata is null)
            {
                return EmptyMetadata;
            }

            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in metadata)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException("Feature metadata keys cannot be empty.", nameof(metadata));
                }

                copy[pair.Key] = pair.Value ?? throw new ArgumentException("Feature metadata values cannot be null.", nameof(metadata));
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }
    }
}
```

- [ ] **Step 5: Rename node registration and record properties**

Update constructors and properties:

```csharp
public IReadOnlyList<NodeFeatureDescriptor> Features { get; }
```

Replace `CopyServices` with `CopyFeatures`. The method must accept empty lists and reject null list items:

```csharp
private static IReadOnlyList<NodeFeatureDescriptor> CopyFeatures(
    IReadOnlyList<NodeFeatureDescriptor> features)
{
    if (features is null)
    {
        throw new ArgumentNullException(nameof(features));
    }

    var copy = new List<NodeFeatureDescriptor>(features.Count);
    for (var i = 0; i < features.Count; i++)
    {
        copy.Add(features[i] ?? throw new ArgumentException("Node feature cannot be null.", nameof(features)));
    }

    return new ReadOnlyCollection<NodeFeatureDescriptor>(copy);
}
```

Rename `NodeRecord.HasService` to:

```csharp
public bool HasFeature(string name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        throw new ArgumentException("Feature name is required.", nameof(name));
    }

    return Features.Any(feature => string.Equals(feature.Name, name, StringComparison.Ordinal));
}
```

- [ ] **Step 6: Rename query and discovery inputs**

In `NodeDirectoryQuery`, replace `serviceKind` and `serviceName` with one nullable `featureName`:

```csharp
public NodeDirectoryQuery(
    string clusterName,
    string? featureName = null,
    NodeState? state = null,
    IReadOnlyDictionary<string, string>? labels = null,
    bool includeExpired = false)
```

Expose:

```csharp
public string? FeatureName { get; }
```

In `IClusterNodeDiscovery`, use `FeatureName` and return descriptors from `AnyAsync`:

```csharp
ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
    FeatureName feature,
    CancellationToken cancellationToken = default);

ValueTask<ClusterNodeDescriptor?> AnyAsync(
    FeatureName feature,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 7: Update node directory implementations and converters**

Update all references in:

```txt
src/Lakona.Game.Cluster/Nodes/InMemoryNodeDirectory.cs
src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryMessages.cs
src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryRecordConverter.cs
src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryClient.cs
src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryBinder.cs
src/Lakona.Game.Cluster.Sql/**
```

Wire format field names must become `Features`. Do not leave serialized `Services` in request or reply DTOs.

- [ ] **Step 8: Run cluster tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj
dotnet test tests/Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj
dotnet test tests/Lakona.Game.Cluster.Sql.Tests/Lakona.Game.Cluster.Sql.Tests.csproj
```

Expected: all three test projects pass.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src/Lakona.Game.Cluster src/Lakona.Game.Cluster.Rpc src/Lakona.Game.Cluster.Sql tests/Lakona.Game.Cluster.Tests tests/Lakona.Game.Cluster.Rpc.Tests tests/Lakona.Game.Cluster.Sql.Tests
git commit -m "Replace cluster service model with features"
```

## Task 2: Move Runtime Configuration To `Lakona` And Add Endpoint `RpcServices`

**Files:**
- Modify: `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`
- Modify: `src/Lakona.Game.Server/Configuration/LakonaGameEndpointOptions.cs`
- Modify: `src/Lakona.Game.Server/Configuration/ClusterOptions.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/Rules/EndpointRule.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/Rules/ClusterEndpointRule.cs`
- Delete: `src/Lakona.Game.Server/Guardrails/Rules/ClusterServiceGraphRule.cs`
- Modify: `src/Lakona.Game.Server/Health/LakonaGameReadinessProbe.cs`
- Modify: `tests/Lakona.Game.Server.Tests/Configuration/LakonaGameRuntimeOptionsTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`

- [ ] **Step 1: Write failing configuration tests**

Add tests to `tests/Lakona.Game.Server.Tests/Configuration/LakonaGameRuntimeOptionsTests.cs`:

```csharp
[Fact]
public void FromConfiguration_prefers_lakona_root()
{
    var configuration = BuildConfiguration(new Dictionary<string, string?>
    {
        ["Lakona:Node:Id"] = "gateway-1",
        ["Lakona.Game:Node:Id"] = "legacy",
        ["Lakona:Endpoints:0:Transport"] = "websocket",
        ["Lakona:Endpoints:0:Host"] = "0.0.0.0",
        ["Lakona:Endpoints:0:Port"] = "20000",
        ["Lakona:Endpoints:0:Path"] = "/ws",
        ["Lakona:Endpoints:0:RpcServices:0"] = "login",
        ["Lakona:Endpoints:0:RpcServices:1"] = "player",
        ["Lakona:Cluster:Endpoint"] = "tcp://10.0.0.2:21002",
        ["Lakona:Cluster:Seeds:0"] = "tcp://10.0.0.1:21001"
    });

    var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

    Assert.Equal("gateway-1", options.Node.Id);
    var endpoint = Assert.Single(options.Endpoints);
    Assert.Equal("websocket", endpoint.Transport);
    Assert.Equal(["login", "player"], endpoint.RpcServices);
    Assert.Equal("tcp://10.0.0.2:21002", options.Cluster!.Endpoint);
}

[Fact]
public void FromConfiguration_preserves_empty_feature_array()
{
    var configuration = BuildConfiguration(new Dictionary<string, string?>
    {
        ["Lakona:Node:Id"] = "gateway-1",
        ["Lakona:Feature"] = ""
    });

    var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

    Assert.NotNull(options.Feature);
    Assert.Empty(options.Feature);
}

[Fact]
public void ToClusterOptions_uses_cluster_endpoint_and_transport_keys()
{
    var options = new LakonaGameRuntimeOptions
    {
        Node = new LakonaGameNodeOptions { Id = "gateway-1" },
        Cluster = new LakonaGameClusterOptions { Endpoint = "tcp://10.0.0.2:21002" },
        Endpoints =
        [
            new LakonaGameEndpointOptions
            {
                Transport = "websocket",
                Host = "0.0.0.0",
                AdvertisedHost = "game.example.com",
                Port = 20000,
                Path = "/ws"
            }
        ]
    };

    var cluster = options.ToClusterOptions();

    Assert.Equal("gateway-1", cluster.NodeId);
    Assert.Equal("tcp://10.0.0.2:21002", cluster.AdvertisedEndpoints["cluster"]);
    Assert.Equal("ws://game.example.com:20000/ws", cluster.AdvertisedEndpoints["websocket"]);
    Assert.False(cluster.AdvertisedEndpoints.ContainsKey("client"));
}
```

- [ ] **Step 2: Run failing server configuration tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~LakonaGameRuntimeOptionsTests"
```

Expected: compile failure for missing `RpcServices`, failing assertions for `Lakona` root, or stale `ToClusterOptions` signature.

- [ ] **Step 3: Bind `Lakona` root first**

In `LakonaGameRuntimeOptions.FromConfiguration`, choose the configuration section with this rule:

```csharp
private static IConfigurationSection GetRuntimeSection(IConfiguration configuration)
{
    var lakona = configuration.GetSection("Lakona");
    if (lakona.GetChildren().Any())
    {
        return lakona;
    }

    return configuration.GetSection("Lakona.Game");
}
```

Every new error path emitted by this class must start with `Lakona:`. Keep `Lakona.Game` only in compatibility test names or compatibility assertions.

- [ ] **Step 4: Add endpoint `RpcServices`**

In `LakonaGameEndpointOptions` add:

```csharp
public IReadOnlyList<string> RpcServices { get; init; } = Array.Empty<string>();
```

In endpoint binding, populate it with non-null string values from `RpcServices`.

Endpoint options must not add a `Name` property.

- [ ] **Step 5: Build cluster endpoint map**

Replace `ToClusterOptions(string transport)` with:

```csharp
public ClusterOptions ToClusterOptions()
```

Implementation rules:

- Add `["cluster"] = Cluster.Endpoint` when `Cluster` exists and `Cluster.Endpoint` is not blank.
- Add one advertised endpoint entry per configured endpoint using the lower-case transport as the key.
- Throw `InvalidOperationException` if two endpoints use the same transport case-insensitively.
- Do not add `client`.
- Do not read `Lakona:Cluster:Services`.

- [ ] **Step 6: Remove cluster service options**

In `ClusterOptions`, remove:

```csharp
public IReadOnlyList<ClusterServiceOptions> Services { get; init; }
public sealed class ClusterServiceOptions
```

Update readiness and resolved runtime models to report features, endpoints, and cluster endpoint. Do not report cluster services.

- [ ] **Step 7: Update guardrail rules**

In endpoint validation:

- WebSocket endpoint must have `Path`.
- KCP endpoint must have empty `Path`.
- Duplicate endpoint transports fail.
- Duplicate endpoint bind addresses fail.
- Duplicate `RpcServices` within one endpoint fail.

Delete `ClusterServiceGraphRule.cs` and remove its service registration from `src/Lakona.Game.Server/Guardrails/LakonaGameGuardrailServiceCollectionExtensions.cs`. Feature duplicate-name and unknown-name validation belongs in `LakonaGameFeatureCatalogBuilder`, not in a guardrail rule. No type name may contain `ClusterService`.

- [ ] **Step 8: Run server configuration and guardrail tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~Configuration|FullyQualifiedName~Guardrails"
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests
git commit -m "Move game runtime configuration to Lakona root"
```

## Task 3: Implement Convention-Based Feature Discovery And Lifecycle

**Files:**
- Modify: `src/Lakona.Game.Server/Features/LakonaGameFeature.cs`
- Modify: `src/Lakona.Game.Server/Features/LakonaGameFeatureContext.cs`
- Modify: `src/Lakona.Game.Server/Features/LakonaGameFeatureCatalog.cs`
- Modify: `src/Lakona.Game.Server/Features/LakonaGameFeatureCatalogBuilder.cs`
- Modify: `src/Lakona.Game.Server/Features/FeatureServiceCollectionExtensions.cs`
- Create: `src/Lakona.Game.Server/Features/LakonaGameFeatureDiscovery.cs`
- Create: `src/Lakona.Game.Server/Features/LakonaGameFeatureName.cs`
- Create: `src/Lakona.Game.Server/Features/LakonaGameFeatureHostedService.cs`
- Create: `tests/Lakona.Game.Server.Tests/Features/LakonaGameFeatureDiscoveryTests.cs`

- [ ] **Step 1: Write failing discovery tests**

Create `tests/Lakona.Game.Server.Tests/Features/LakonaGameFeatureDiscoveryTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server.Features;
using Xunit;

namespace Lakona.Game.Server.Tests.Features;

public sealed class LakonaGameFeatureDiscoveryTests
{
    [Fact]
    public void NameConventionConvertsFeatureTypesToKebabCase()
    {
        Assert.Equal("database", LakonaGameFeatureName.FromType(typeof(DatabaseFeature)));
        Assert.Equal("state-store", LakonaGameFeatureName.FromType(typeof(StateStoreFeature)));
        Assert.Equal("http-gateway", LakonaGameFeatureName.FromType(typeof(HTTPGatewayFeature)));
    }

    [Fact]
    public void DiscoveryRejectsFeatureNameCollisions()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LakonaGameFeatureDiscovery.Discover(typeof(DatabaseFeature).Assembly, [
                typeof(DatabaseFeature),
                typeof(DATABASEFeature)
            ]));

        Assert.Contains("database", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OmittedFeatureConfigEnablesDiscoveredAppFeaturesByName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "data-1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLakonaGame(configuration, [typeof(StateStoreFeature), typeof(DatabaseFeature)]);

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Equal(["database", "state-store"], catalog.ActiveNames);
    }

    [Fact]
    public void EmptyFeatureConfigEnablesNoAppFeatures()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "gateway-1",
                ["Lakona:Feature"] = ""
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLakonaGame(configuration, [typeof(DatabaseFeature)]);

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Empty(catalog.ActiveNames);
    }

    [Fact]
    public async Task HostedServiceStartsAndStopsFeaturesInConfiguredOrder()
    {
        LifecycleLog.Events.Clear();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Feature:0"] = "database",
                ["Lakona:Feature:1"] = "state-store"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGame(configuration, [typeof(DatabaseFeature), typeof(StateStoreFeature)]);

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().OfType<LakonaGameFeatureHostedService>().Single();

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await hosted.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([
            "database:start",
            "state-store:start",
            "state-store:stop",
            "database:stop"
        ], LifecycleLog.Events);
    }

    private sealed class DatabaseFeature : LakonaGameFeature
    {
        public override ValueTask StartAsync(LakonaGameFeatureContext context, CancellationToken cancellationToken = default)
        {
            LifecycleLog.Events.Add("database:start");
            return default;
        }

        public override ValueTask StopAsync(LakonaGameFeatureContext context, CancellationToken cancellationToken = default)
        {
            LifecycleLog.Events.Add("database:stop");
            return default;
        }
    }

    private sealed class StateStoreFeature : LakonaGameFeature
    {
        public override ValueTask StartAsync(LakonaGameFeatureContext context, CancellationToken cancellationToken = default)
        {
            LifecycleLog.Events.Add("state-store:start");
            return default;
        }

        public override ValueTask StopAsync(LakonaGameFeatureContext context, CancellationToken cancellationToken = default)
        {
            LifecycleLog.Events.Add("state-store:stop");
            return default;
        }
    }

    private sealed class HTTPGatewayFeature : LakonaGameFeature { }

    private sealed class DATABASEFeature : LakonaGameFeature { }

    private static class LifecycleLog
    {
        public static readonly List<string> Events = [];
    }
}
```

- [ ] **Step 2: Run the failing discovery tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~LakonaGameFeatureDiscoveryTests"
```

Expected: compile failure for missing discovery APIs and lifecycle hooks.

- [ ] **Step 3: Extend `LakonaGameFeature`**

Change `src/Lakona.Game.Server/Features/LakonaGameFeature.cs` to include:

```csharp
public abstract class LakonaGameFeature
{
    public virtual bool Discoverable => true;

    public virtual IReadOnlyDictionary<string, string> Metadata => new Dictionary<string, string>(StringComparer.Ordinal);

    public virtual void ConfigureServices(LakonaGameFeatureContext context)
    {
    }

    public virtual ValueTask StartAsync(
        LakonaGameFeatureContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    public virtual ValueTask StopAsync(
        LakonaGameFeatureContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }
}
```

- [ ] **Step 4: Add feature name convention**

Create `LakonaGameFeatureName.FromType(Type featureType)`. Rules:

- Type name must end with `Feature`.
- Remove the suffix.
- Convert PascalCase and acronym words to lower-case kebab-case.
- Reject empty names.
- Reject case-insensitive collisions during discovery.

- [ ] **Step 5: Add convention discovery overload**

Add this public extension:

```csharp
public static IServiceCollection AddLakonaGame(
    this IServiceCollection services,
    IConfiguration config,
    IReadOnlyList<Type>? featureTypes = null)
```

When `featureTypes` is null, discover `LakonaGameFeature` subclasses from non-dynamic assemblies already loaded in `AppDomain.CurrentDomain`. The overload with explicit `featureTypes` exists for tests and deterministic samples.

The existing fluent overload may remain for compatibility, but generated projects and Agar must not call it after Task 7.

- [ ] **Step 6: Resolve enabled features**

Resolution rules:

- `Lakona:Feature` missing: enable all discovered app features sorted by feature name.
- `Lakona:Feature` empty array: enable no app features.
- `Lakona:Feature` string array: enable those names in array order.
- Unknown configured feature fails startup with available feature names.
- Duplicate configured feature fails startup.

- [ ] **Step 7: Add hosted lifecycle**

Create `LakonaGameFeatureHostedService` and register it as `IHostedService` in `AddLakonaGame`.

Startup:

1. Run `ConfigureServices` during service registration in active feature order.
2. Run `StartAsync` after host build in active feature order.
3. Mark discoverable features as ready only after each `StartAsync` succeeds.
4. If a feature start fails, call `StopAsync` for already-started features in reverse order and rethrow.

Shutdown:

1. Call `StopAsync` in reverse active feature order.
2. Log stop failures and continue.

- [ ] **Step 8: Run feature tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~Feature"
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src/Lakona.Game.Server/Features tests/Lakona.Game.Server.Tests
git commit -m "Discover game features by convention"
```

## Task 4: Add Endpoint-Scoped RPC Service Binder Discovery

**Files:**
- Create: `src/Lakona.Game.Server/Hosting/LakonaRpcServiceAttribute.cs`
- Create: `src/Lakona.Game.Server/Hosting/LakonaRpcServiceBinder.cs`
- Create: `src/Lakona.Game.Server/Hosting/LakonaRpcServiceCatalog.cs`
- Create: `src/Lakona.Game.Server/Hosting/LakonaEndpointRpcServerConfigurator.cs`
- Modify: `src/Lakona.Game.Server/Hosting/RpcServersHostedService.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaGameServerGatewayExtensions.cs`
- Create: `tests/Lakona.Game.Server.Tests/Hosting/LakonaRpcServiceCatalogTests.cs`

- [ ] **Step 1: Write failing RPC service catalog tests**

Create `tests/Lakona.Game.Server.Tests/Hosting/LakonaRpcServiceCatalogTests.cs`:

```csharp
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaRpcServiceCatalogTests
{
    [Fact]
    public void DiscoversBinderByExplicitAttribute()
    {
        var catalog = LakonaRpcServiceCatalog.FromTypes([typeof(LoginBinder)]);

        Assert.True(catalog.TryGet("login", out var descriptor));
        Assert.Equal(typeof(LoginBinder), descriptor.BinderType);
    }

    [Fact]
    public void RejectsBinderWithoutAttribute()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LakonaRpcServiceCatalog.FromTypes([typeof(MissingAttributeBinder)]));

        Assert.Contains(nameof(MissingAttributeBinder), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateBinderNames()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LakonaRpcServiceCatalog.FromTypes([typeof(LoginBinder), typeof(DuplicateLoginBinder)]));

        Assert.Contains("login", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [LakonaRpcService("login")]
    private sealed class LoginBinder : LakonaRpcServiceBinder
    {
        public override void Bind(RpcServiceRegistry registry, IServiceProvider services)
        {
        }
    }

    [LakonaRpcService("login")]
    private sealed class DuplicateLoginBinder : LakonaRpcServiceBinder
    {
        public override void Bind(RpcServiceRegistry registry, IServiceProvider services)
        {
        }
    }

    private sealed class MissingAttributeBinder : LakonaRpcServiceBinder
    {
        public override void Bind(RpcServiceRegistry registry, IServiceProvider services)
        {
        }
    }
}
```

- [ ] **Step 2: Run failing RPC service catalog tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~LakonaRpcServiceCatalogTests"
```

Expected: compile failure for missing RPC service binder APIs.

- [ ] **Step 3: Implement binder base and attribute**

Add:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class LakonaRpcServiceAttribute : Attribute
{
    public LakonaRpcServiceAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("RPC service name is required.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }
}
```

Add:

```csharp
public abstract class LakonaRpcServiceBinder
{
    public abstract void Bind(RpcServiceRegistry registry, IServiceProvider services);
}
```

- [ ] **Step 4: Implement catalog rules**

`LakonaRpcServiceCatalog` rules:

- Binder type must inherit `LakonaRpcServiceBinder`.
- Binder type must have exactly one `LakonaRpcServiceAttribute`.
- Attribute name must match lower-case kebab-case.
- Duplicate names fail startup.
- Configured names match case-insensitively and normalize to lower-case.
- No C# type-name inference.

- [ ] **Step 5: Bind only endpoint-listed services**

`LakonaEndpointRpcServerConfigurator` must:

1. Receive one `LakonaGameEndpointOptions`.
2. Create the transport acceptor for the endpoint transport.
3. Resolve `LakonaRpcServiceCatalog`.
4. Bind only endpoint `RpcServices`.
5. Fail startup if any configured service is unknown.
6. Fail startup if a service name appears twice in one endpoint.

Do not use endpoint `Name`.

- [ ] **Step 6: Run hosting tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~Hosting"
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Lakona.Game.Server/Hosting tests/Lakona.Game.Server.Tests/Hosting
git commit -m "Bind RPC services by endpoint configuration"
```

## Task 5: Implement Feature-Addressed Request/Reply Message Bus

**Files:**
- Modify: `src/Lakona.Game.Cluster/Messaging/ClusterSendStatus.cs`
- Modify: `src/Lakona.Game.Cluster/Messaging/ClusterMessage.cs`
- Modify: `src/Lakona.Game.Cluster/Messaging/IClusterMessageHandler.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/ClusterRequest.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/ClusterReply.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/IFeatureMessageBus.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/IFeatureMessageHandler.cs`
- Create: `src/Lakona.Game.Cluster/Messaging/FeatureMessageBus.cs`
- Create: `tests/Lakona.Game.Cluster.Tests/FeatureMessageBusTests.cs`

- [ ] **Step 1: Write failing message bus tests**

Create `tests/Lakona.Game.Cluster.Tests/FeatureMessageBusTests.cs`:

```csharp
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class FeatureMessageBusTests
{
    [Fact]
    public async Task SendToFeatureReturnsFeatureNotFoundWhenNoReadyNodeExists()
    {
        var bus = new FeatureMessageBus(
            new EmptyDiscovery(),
            new ThrowingSender(),
            new TestSerializer());

        var reply = await bus.SendToFeatureAsync<string, string>(
            new FeatureName("matchmaking"),
            "join",
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterSendStatus.FeatureNotFound, reply.Status);
    }

    [Fact]
    public async Task SendToFeatureUsesClusterEndpointOfSelectedNode()
    {
        var sender = new CapturingSender();
        var bus = new FeatureMessageBus(
            new SingleNodeDiscovery(new ClusterNodeDescriptor(
                new NodeId("data-1"),
                NodeState.Ready,
                [new NodeFeatureDescriptor("matchmaking")],
                new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
                {
                    ["cluster"] = new NodeEndpoint("tcp://10.0.0.1:21001")
                })),
            sender,
            new TestSerializer());

        await bus.SendToFeatureAsync<string, string>(
            new FeatureName("matchmaking"),
            "join",
            TestContext.Current.CancellationToken);

        Assert.Equal(new NodeId("data-1"), sender.LastNode);
        Assert.Equal("tcp://10.0.0.1:21001", sender.LastEndpoint);
    }
}
```

Complete the test doubles in the same file. The doubles must only implement the methods exercised by these two tests and throw `NotSupportedException` from every other method.

- [ ] **Step 2: Run failing message bus tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --filter "FullyQualifiedName~FeatureMessageBusTests"
```

Expected: compile failure for missing message bus APIs.

- [ ] **Step 3: Extend status model**

Add these statuses to `ClusterSendStatus` without changing existing numeric values:

```csharp
FeatureNotFound = 9,
NodeUnavailable = 10,
SerializationFailed = 11,
DeserializationFailed = 12,
Rejected = 13
```

Keep existing statuses:

```txt
RouteNotFound
Timeout
Backpressure
HandlerUnavailable
Expired
```

- [ ] **Step 4: Add request/reply payload contracts**

`ClusterRequest` must include:

```csharp
public sealed class ClusterRequest
{
    public ClusterRequest(
        FeatureName feature,
        string kind,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset expiresAt,
        NodeId sourceNode,
        string correlationId)
}
```

`ClusterReply` must include:

```csharp
public sealed class ClusterReply
{
    public ClusterSendStatus Status { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public string? ErrorMessage { get; }
}
```

- [ ] **Step 5: Add feature message bus**

`FeatureMessageBus.SendToFeatureAsync` flow:

1. Resolve one node using `IClusterNodeDiscovery.AnyAsync(feature)`.
2. Return `FeatureNotFound` when no node is found.
3. Require the selected node to expose endpoint key `cluster`.
4. Serialize the request.
5. Send to the selected node.
6. Return reply payload or structured failure.

Generated business-facing APIs can throw typed exceptions later; this task only implements low-level status-returning calls.

- [ ] **Step 6: Run cluster message tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --filter "FullyQualifiedName~FeatureMessageBusTests|FullyQualifiedName~ClusterRouterTests|FullyQualifiedName~ClusterNodeSenderTests"
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Lakona.Game.Cluster/Messaging tests/Lakona.Game.Cluster.Tests
git commit -m "Add feature addressed cluster message bus"
```

## Task 6: Implement Gateway-Owned Client Notification Relay

**Files:**
- Create: `src/Lakona.Game.Server/Sessions/ClientNotificationRouteKey.cs`
- Create: `src/Lakona.Game.Server/Sessions/ClientNotificationStatus.cs`
- Create: `src/Lakona.Game.Server/Sessions/IClientNotificationRelay.cs`
- Create: `src/Lakona.Game.Server/Sessions/ClientNotificationRelay.cs`
- Modify: `src/Lakona.Game.Server/Sessions/IGameSessionDirectory.cs`
- Modify: `src/Lakona.Game.Server/Sessions/InMemoryGameSessionDirectory.cs`
- Create: `tests/Lakona.Game.Server.Tests/ClientNotificationRelayTests.cs`

- [ ] **Step 1: Write failing relay tests**

Create `tests/Lakona.Game.Server.Tests/ClientNotificationRelayTests.cs`:

```csharp
using Lakona.Game.Server.Sessions;
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
        var directory = new InMemoryGameSessionDirectory();
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
    public async Task RelayReturnsRouteNotFoundForStaleGeneration()
    {
        var directory = new InMemoryGameSessionDirectory();
        var current = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var stale = new GameSessionKey(current.OwnerKey, current.SessionId, current.Generation + 1);
        var relay = new ClientNotificationRelay(directory);

        var status = await relay.NotifyAsync<TestPlayerCallback>(
            stale,
            cb => cb.Notify("stale"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.RouteNotFound, status);
    }

    private sealed class TestPlayerCallback
    {
        public string LastMessage { get; private set; } = "";

        public void Notify(string message)
        {
            LastMessage = message;
        }
    }
}
```

- [ ] **Step 2: Run failing relay tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~ClientNotificationRelayTests"
```

Expected: compile failure for missing relay APIs.

- [ ] **Step 3: Add route key**

`ClientNotificationRouteKey.FromSession(GameSessionKey session)` must return:

```txt
client-session:<OwnerKey>/<SessionId>/<Generation>
```

Generation is required. Do not add a player-only route.

- [ ] **Step 4: Add relay**

`IClientNotificationRelay`:

```csharp
public interface IClientNotificationRelay
{
    ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        GameSessionKey session,
        Action<TCallback> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class;
}
```

`ClientNotificationRelay` must use `IGameSessionDirectory.GetCallbackAsync<TCallback>`. It must not serialize or store callback objects.

- [ ] **Step 5: Register route on login and remove on disconnect**

After Task 7 wires Agar login to this framework API, gateway login must register a route in the cluster route directory:

```txt
client-session:<playerId>/<sessionId>/<generation> -> gateway-1 cluster endpoint
```

Disconnect cleanup must remove or expire that route through the existing route lease model.

- [ ] **Step 6: Run session tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~Session|FullyQualifiedName~ClientNotificationRelay"
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Lakona.Game.Server/Sessions tests/Lakona.Game.Server.Tests
git commit -m "Add gateway client notification relay"
```

## Task 7: Convert Agar To Three-Node Distributed Acceptance Sample

**Files:**
- Modify: `samples/Game.Unity.Agar/Server/App/Program.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/appsettings.json`
- Create: `samples/Game.Unity.Agar/Server/App/appsettings.data-1.json`
- Create: `samples/Game.Unity.Agar/Server/App/appsettings.gateway-1.json`
- Create: `samples/Game.Unity.Agar/Server/App/appsettings.battle-1.json`
- Modify: `samples/Game.Unity.Agar/docker-compose.yml`
- Create or split feature files under `samples/Game.Unity.Agar/Server/App/Features/`
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/GatewayConfigurationTests.cs`
- Create: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`
- Create: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/RemoteNotificationRelayExampleTests.cs`

- [ ] **Step 1: Write failing topology configuration tests**

Create `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`:

```csharp
using System.Text.Json;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class DistributedTopologyConfigurationTests
{
    [Fact]
    public void DataNodeOwnsDatabaseAndBusinessFeatures()
    {
        using var document = Open("appsettings.data-1.json");
        var lakona = document.RootElement.GetProperty("Lakona");

        Assert.Equal("data-1", lakona.GetProperty("Node").GetProperty("Id").GetString());
        Assert.Equal([
            "database",
            "state-store",
            "matchmaking",
            "leaderboard"
        ], lakona.GetProperty("Feature").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.False(lakona.TryGetProperty("Endpoints", out _));
        Assert.Equal("tcp://10.0.0.1:21001", lakona.GetProperty("Cluster").GetProperty("Endpoint").GetString());
    }

    [Fact]
    public void GatewayNodeHasNoFeaturesAndOnlyWebSocketRpcServices()
    {
        using var document = Open("appsettings.gateway-1.json");
        var lakona = document.RootElement.GetProperty("Lakona");

        Assert.Equal("gateway-1", lakona.GetProperty("Node").GetProperty("Id").GetString());
        Assert.Empty(lakona.GetProperty("Feature").EnumerateArray());
        var endpoint = Assert.Single(lakona.GetProperty("Endpoints").EnumerateArray());
        Assert.Equal("websocket", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("/ws", endpoint.GetProperty("Path").GetString());
        Assert.Equal(["login", "player"], endpoint.GetProperty("RpcServices").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public void BattleNodeHasBattleRuntimeAndOnlyKcpRpcService()
    {
        using var document = Open("appsettings.battle-1.json");
        var lakona = document.RootElement.GetProperty("Lakona");

        Assert.Equal("battle-1", lakona.GetProperty("Node").GetProperty("Id").GetString());
        Assert.Equal(["battle-runtime"], lakona.GetProperty("Feature").EnumerateArray().Select(x => x.GetString()).ToArray());
        var endpoint = Assert.Single(lakona.GetProperty("Endpoints").EnumerateArray());
        Assert.Equal("kcp", endpoint.GetProperty("Transport").GetString());
        Assert.False(endpoint.TryGetProperty("Path", out _));
        Assert.Equal(["battle"], endpoint.GetProperty("RpcServices").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    private static JsonDocument Open(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "samples", "Game.Unity.Agar", "Server", "App", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
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

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
```

- [ ] **Step 2: Run failing Agar topology tests**

Run:

```powershell
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --filter "FullyQualifiedName~DistributedTopologyConfigurationTests"
```

Expected: failure because the three config files do not exist.

- [ ] **Step 3: Create three node config files**

Use the exact shape from `docs/game/distributed-feature-cluster-model.md`:

- `appsettings.data-1.json`: root `Lakona`, feature array `database`, `state-store`, `matchmaking`, `leaderboard`, no `Endpoints`.
- `appsettings.gateway-1.json`: root `Lakona`, `Feature: []`, WebSocket endpoint with `RpcServices: ["login", "player"]`.
- `appsettings.battle-1.json`: root `Lakona`, feature array `["battle-runtime"]`, KCP endpoint with `RpcServices: ["battle"]`, no `Path`.

Keep `appsettings.json` as local developer default by making it match `appsettings.gateway-1.json`.

- [ ] **Step 4: Remove fluent feature declarations from Agar Program**

Replace:

```csharp
builder.Services.AddLakonaGame(builder.Configuration, features =>
{
    features.Feature<GatewayCoreFeature>("gateway-core");
    features
        .Feature<GatewayBusinessFeature>("gateway-business")
        .After("gateway-core")
        .RequiresFeature("gateway-core")
        .RequiresTransport("websocket")
        .RequiresTransport("kcp");
});
```

With:

```csharp
builder.Services.AddLakonaGame(builder.Configuration, [
    typeof(DatabaseFeature),
    typeof(StateStoreFeature),
    typeof(MatchmakingFeature),
    typeof(LeaderboardFeature),
    typeof(BattleRuntimeFeature)
]);
```

This explicit type list is allowed in the sample only to keep tests deterministic. Generated projects must call the overload without a feature list.

- [ ] **Step 5: Split sample features**

Feature ownership:

- `DatabaseFeature`: registers database connection factories and repositories; `Discoverable => false`.
- `StateStoreFeature`: registers state stores; discoverable.
- `MatchmakingFeature`: registers matchmaking actor/coordinator; discoverable.
- `LeaderboardFeature`: registers leaderboard actor/store; discoverable.
- `BattleRuntimeFeature`: registers room runtime and KCP realtime flow; discoverable.

Gateway node has no feature and only endpoint RPC service binders.

- [ ] **Step 6: Add remote notification example test**

Create `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/RemoteNotificationRelayExampleTests.cs`:

```csharp
using Lakona.Game.Server.Sessions;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class RemoteNotificationRelayExampleTests
{
    [Fact]
    public async Task DataNodeNotificationCanReachGatewayOwnedCallback()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-1", TestContext.Current.CancellationToken);
        var callback = new TestPlayerCallback();
        await directory.BindSessionAsync(session, "gateway-conn-1", callback, TestContext.Current.CancellationToken);
        var relay = new ClientNotificationRelay(directory);

        var status = await relay.NotifyAsync<IPlayerCallback>(
            session,
            cb => cb.OnMatchmakingStatus(new MatchmakingStatusUpdate
            {
                State = MatchmakingState.Matched,
                Message = "matched from data node"
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Delivered, status);
        Assert.Equal(MatchmakingState.Matched, callback.LastStatus.State);
        Assert.Equal("matched from data node", callback.LastStatus.Message);
    }

    private sealed class TestPlayerCallback : IPlayerCallback
    {
        public MatchmakingStatusUpdate LastStatus { get; private set; } = new();

        public void OnWorldState(WorldState worldState) { }

        public void OnPlayerDead(PlayerDead deadEvent) { }

        public void OnMatchEnd(MatchEnd matchEnd) { }

        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            LastStatus = matchmakingStatus;
        }
    }
}
```

- [ ] **Step 7: Run Agar business tests**

Run:

```powershell
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
```

Expected: all Agar business tests pass.

- [ ] **Step 8: Commit**

Run:

```powershell
git add samples/Game.Unity.Agar
git commit -m "Update Agar sample for distributed feature topology"
```

## Task 8: Final Contract Scans And Solution Validation

**Files:**
- Modify only files required by previous tasks.
- No new generated RPC glue.
- No Unity editor cache or asset churn.

- [ ] **Step 1: Scan for forbidden old concepts**

Run:

```powershell
rg -n "ClusterService|NodeServiceDescriptor|NodeRegistration\\.Services|NodeRecord\\.Services|ClusterFeature|Lakona:Cluster:Services|Lakona\\.Game:|\"Lakona\\.Game\"" src tests samples/Game.Unity.Agar docs/game
```

Expected:

- No `ClusterService`, `NodeServiceDescriptor`, `NodeRegistration.Services`, `NodeRecord.Services`, `ClusterFeature`, or `Lakona:Cluster:Services` in `src/**`, `tests/**`, or `samples/Game.Unity.Agar/**`.
- `Lakona.Game` may remain only in docs that explicitly describe migration compatibility.

- [ ] **Step 2: Scan Agar gateway for database ownership violations**

Run:

```powershell
rg -n "ConnectionString|DbConnection|Npgsql|Dapper|AddAgarSampleState|IUserStateStore|IRoomStateStore|ILeaderboardStateStore" samples/Game.Unity.Agar/Server/App
```

Expected:

- Gateway RPC binder files do not directly register or resolve database/state-store services.
- Data-node features own state-store and database registrations.

- [ ] **Step 3: Run targeted test projects**

Run:

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj
dotnet test tests/Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj
dotnet test tests/Lakona.Game.Cluster.Sql.Tests/Lakona.Game.Cluster.Sql.Tests.csproj
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
```

Expected: all listed projects pass.

- [ ] **Step 4: Run solution build**

Run:

```powershell
dotnet build Lakona.slnx
```

Expected: build exits with code 0.

- [ ] **Step 5: Commit final cleanup**

Run:

```powershell
git status --short
git add src tests samples/Game.Unity.Agar docs/game
git commit -m "Complete distributed feature cluster model"
```

Only create this commit if there are final cleanup changes after Task 7. If there are no changes, skip this commit.

## Completion Checklist

- [ ] `docs/game/distributed-feature-cluster-model.md` matches implemented config, APIs, and sample behavior.
- [ ] `Lakona` is the public runtime configuration root.
- [ ] `Lakona.Game` compatibility is isolated to explicit migration tests.
- [ ] Feature discovery works without fluent `Program.cs` declarations.
- [ ] `Feature: []` starts no application features.
- [ ] `database` can be first and non-discoverable.
- [ ] RPC service exposure is endpoint-local through `RpcServices`.
- [ ] WebSocket endpoint has `Path`.
- [ ] KCP endpoint has no `Path`.
- [ ] Cluster node registration uses feature descriptors.
- [ ] Feature discovery returns ready, non-expired nodes.
- [ ] Feature message bus returns `FeatureNotFound`, `NodeUnavailable`, `Timeout`, `Backpressure`, `HandlerUnavailable`, `Expired`, `SerializationFailed`, `DeserializationFailed`, and `Rejected` where those statuses apply.
- [ ] Client notification relay uses `client-session:<owner>/<session>/<generation>`.
- [ ] Agar data node owns database and business features.
- [ ] Agar gateway owns WebSocket callbacks and no database connection.
- [ ] Agar battle node owns KCP battle runtime.
- [ ] Remote notification example demonstrates data-node intent reaching gateway-owned callback.
