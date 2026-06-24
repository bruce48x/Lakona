# Cluster Serializer And Remote Actor Wiring Clarifications

Source plan: `docs/superpowers/plans/2026-06-24-cluster-serializer-remote-actor-wiring.md`

Status: resolved and incorporated into the source plan.

## Decisions

1. `Lakona:Cluster:Serializer` is mandatory whenever `Lakona:Cluster` is configured. Do not silently default to JSON. Update existing cluster tests, samples, and docs to set the value explicitly.
2. Built-in cluster wiring must make `Lakona:Cluster:Serializer` win over earlier bare `IRpcSerializer` registrations by replacing the service collection's cluster `IRpcSerializer`. The cluster RPC server must read that serializer from DI.
3. Direct `AddLakonaGameServerActors()` remains local-only and does not register the default `IRemoteActorSerializer`. The default `RpcRemoteActorSerializer` adapter is registered by active cluster endpoint wiring, where the cluster `IRpcSerializer` exists.
4. Preserve existing `init` properties on client-notification dispatch DTOs first. If MemoryPack source generation rejects `init`, changing those internal cluster-dispatch DTOs to public `set` is acceptable with focused roundtrip tests.
5. Documentation and sample configuration updates are part of the implementation plan because `Lakona:Cluster:Serializer` is a required runtime contract.

## Confirmation Items

### 1. Is `Lakona:Cluster:Serializer` now mandatory for every configured cluster?

The plan makes `Lakona:Cluster:Serializer` required whenever `Lakona:Cluster` is configured, with `ULINK044` for missing values. That is a behavioral compatibility change.

Current repository content still has cluster configurations and tests that only set `Lakona:Cluster:Endpoint`, including:

- `tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs`
- `tests/Lakona.Game.Server.Tests/LakonaGameServerHostingOptionsTests.cs`
- `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`
- `tests/Lakona.Game.Server.Tests/Configuration/LakonaGameRuntimeOptionsTests.cs`
- `samples/Game.Unity.Agar/Server/App/appsettings.gateway-1.json`
- `samples/Game.Unity.Agar/Server/App/appsettings.data-1.json`
- `samples/Game.Unity.Agar/Server/App/appsettings.battle-1.json`
- `docs/configuration.md`
- `docs/cluster.md`
- `src/Lakona.Game.Cluster/README.md`
- `src/Lakona.Game.Cluster.Rpc/README.md`

Resolution: update all current cluster examples/tests to include `"Serializer": "json"` or `"memorypack"` explicitly. Runtime must not keep an implicit JSON default for configured clusters.

### 2. Should the configured cluster serializer override existing `IRpcSerializer` registrations?

Task 3 registers the cluster serializer with:

```csharp
services.TryAddSingleton<IRpcSerializer>(_ =>
    LakonaEndpointRuntimeDefaults.CreateClusterSerializer(runtimeOptions.Cluster));
```

Because this uses `TryAddSingleton`, any earlier `IRpcSerializer` registration wins, even if `Lakona:Cluster:Serializer` says something else. That can make `IClusterClientFactory` and the default remote actor serializer use a different serializer from the configured cluster RPC server.

Resolution: cluster wiring must remove/replace the existing bare `IRpcSerializer` registration so `Lakona:Cluster:Serializer` is the single built-in node-to-node selector. Actor-specific `IRemoteActorSerializer` overrides may still use `TryAddSingleton`.

### 3. What should happen when `IRemoteActorSerializer` is resolved in a local-only actor setup?

Task 6 adds a default `IRemoteActorSerializer` adapter that requires `IRpcSerializer`. `AddLakonaGameServerActors()` is also used by process-local actor tests and local hosts where no cluster endpoint has registered an `IRpcSerializer`.

The plan says this registration may require `IRpcSerializer` only when a non-local remote actor accessor is resolved, but after registration a direct `provider.GetRequiredService<IRemoteActorSerializer>()` in a local-only setup would fail unless some serializer was registered.

Resolution: move the default remote actor serializer registration to active cluster endpoint wiring where the cluster `IRpcSerializer` exists. Local-only actor setups should not register `IRemoteActorSerializer`; direct resolution returns missing service unless the caller explicitly registered one.

### 4. Are `init`-only notification DTO properties allowed to change to `set` if MemoryPack requires it?

Task 5 says to preserve notification DTOs while adding MemoryPack metadata. Current notification DTOs use `init` properties:

- `ClientNotificationCommand`
- `ClientNotificationArgument`
- `ClientNotificationDispatchRequest`
- `ClientNotificationDispatchReply`

The cluster RPC DTO examples use public `set`. If MemoryPack source generation rejects one or more `init` properties, implementation may need to change them from `init` to `set`.

Resolution: preserve `init` first. Changing these internal cluster-dispatch DTO properties from `init` to public `set` is acceptable only if MemoryPack source generation requires it and the focused MemoryPack roundtrip tests pass.

### 5. Should documentation and samples be part of this plan?

Task 8 explicitly protects the current single-node starter from gaining cluster config, but the plan does not say whether existing cluster docs and distributed sample appsettings should be updated for the new serializer setting.

Resolution: include docs and sample config updates. Existing cluster examples must not omit a required `Lakona:Cluster:Serializer` value.
