# Seeded Actor Directory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make actor ownership registered on one cluster node resolvable from every other node through the configured cluster seed.

**Architecture:** The seed retains `InMemoryActorDirectory`; remote nodes replace it with a seeded client that sends opaque Actor Directory commands through the existing ClusterMessage RPC endpoint. A seed-only handler executes commands against the local directory and returns replies through the existing node-directed reply gateway, so no cluster protocol package or MemoryPack schema changes are required.

**Tech Stack:** .NET 10, C#, xUnit, Lakona cluster RPC, `System.Text.Json`, Microsoft dependency injection, PowerShell 7, Docker Compose, Unity Test Framework.

---

## File Structure

- Create `src/Lakona.Game.Server/Actors/ActorDirectoryClusterMessages.cs`: internal request/reply DTOs and stable message kinds/routes.
- Create `src/Lakona.Game.Server/Actors/SeededActorDirectory.cs`: remote `IActorDirectory` implementation targeting the configured seed endpoint.
- Create `src/Lakona.Game.Server/Actors/ActorDirectoryClusterHandler.cs`: seed-local command handler backed by `IActorDirectory`.
- Create `tests/Lakona.Game.Server.Tests/Actors/SeededActorDirectoryTests.cs`: client transport, reply, cancellation, and failure contracts.
- Create `tests/Lakona.Game.Server.Tests/Actors/ActorDirectoryClusterHandlerTests.cs`: handler command/status/reply contracts.
- Modify `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`: choose seed-local versus seeded remote directory and register the seed-only handler.
- Modify `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`: protect DI selection and handler registration.
- Modify `src/Lakona.Game.Server/Hosting/LakonaGameClusterRegistrationHostedService.cs`: stop advertising the removed directory-host discovery label.
- Modify `tests/Lakona.Game.Server.Tests/Hosting/LakonaGameClusterRegistrationHostedServiceTests.cs`: expect no obsolete label.
- Delete `src/Lakona.Game.Server/Actors/ActorDirectoryClient.cs`, `IActorDirectoryHostClient.cs`, and `ActorDirectoryLabels.cs` plus their obsolete tests.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`: prove data, gateway, and battle nodes select the correct Actor Directory implementations.
- Modify `docs/actor.md` and `docs/cluster.md`: document seeded ephemeral ownership and failure behavior.

### Task 1: Seeded client transport contract

**Files:**
- Create: `tests/Lakona.Game.Server.Tests/Actors/SeededActorDirectoryTests.cs`
- Create: `src/Lakona.Game.Server/Actors/ActorDirectoryClusterMessages.cs`
- Create: `src/Lakona.Game.Server/Actors/SeededActorDirectory.cs`

- [ ] **Step 1: Write the failing resolve round-trip test**

Create a recording `INodeMessenger` that captures the target and command, then completes the matching `RemoteActorGateway` pending request through `CreateReplyHandler()`:

```csharp
[Fact]
public async Task ResolveAsync_sends_to_seed_endpoint_and_returns_owner()
{
    var gateway = new RemoteActorGateway();
    var messenger = new ReplyingNodeMessenger(gateway, new ActorDirectoryReply(
        ActorDirectoryOperationStatus.Succeeded,
        new ActorDirectoryRecordDto("user/player-1", "data-1", 7, DateTimeOffset.UtcNow)));
    var directory = new SeededActorDirectory(
        gateway,
        messenger,
        new LocalActorNodeIdentity(new NodeId("gateway-1")),
        "tcp://10.0.0.1:21001");

    var record = await directory.ResolveAsync(
        ActorId.From("user/player-1"),
        TestContext.Current.CancellationToken);

    Assert.NotNull(record);
    Assert.Equal(new NodeId("data-1"), record.Node);
    Assert.Equal("tcp://10.0.0.1:21001", messenger.Target.Endpoint.Address);
    Assert.Equal(ActorDirectoryClusterProtocol.ResolveKind, messenger.Message.Kind);
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter SeededActorDirectoryTests
```

Expected: compile failure because `SeededActorDirectory` and the internal protocol types do not exist.

- [ ] **Step 3: Add the minimal protocol and resolve implementation**

Define internal JSON-owned records and constants:

```csharp
internal static class ActorDirectoryClusterProtocol
{
    public static readonly RouteKey Route = new("actor-directory:command");
    public const string ResolveKind = "_actor_directory_resolve";
    public const string RegisterKind = "_actor_directory_register";
    public const string UnregisterKind = "_actor_directory_unregister";
}

internal sealed record ActorDirectoryRequest(string ActorId, string? OwnerNode);
internal sealed record ActorDirectoryRecordDto(
    string ActorId,
    string Node,
    long Version,
    DateTimeOffset UpdatedAt);
internal enum ActorDirectoryOperationStatus
{
    Succeeded,
    Registered,
    AlreadyRegistered,
    Conflict,
    Unregistered,
    NotFound,
    OwnershipMismatch,
    InvalidRequest,
    Failed
}
internal sealed record ActorDirectoryReply(
    ActorDirectoryOperationStatus Status,
    ActorDirectoryRecordDto? Record = null,
    string? Error = null);
```

Implement `SeededActorDirectory` with one private `ExecuteAsync` method. It must create a pending gateway correlation, send a `ClusterMessage` to a synthetic `RouteLocation` whose endpoint is the configured seed, cancel pending state on send failure, preserve caller cancellation, translate non-accepted sends and malformed/error replies to `ActorDirectoryUnavailableException`, and map a successful record back to `ActorDirectoryRecord`.

The synthetic target is constructed without route lookup:

```csharp
private static RouteLocation CreateSeedTarget(string endpoint) => new(
    ActorDirectoryClusterProtocol.Route,
    new NodeId("actor-directory-seed"),
    new NodeEndpoint(endpoint),
    DateTimeOffset.MaxValue);
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 1 command. Expected: the resolve test passes.

- [ ] **Step 5: Add RED tests for register, unregister, malformed reply, send failure, and cancellation**

Add separate tests asserting:

```csharp
Assert.Equal(ActorDirectoryRegisterStatus.Conflict, await directory.RegisterAsync(actorId, owner, ct));
Assert.Equal(ActorDirectoryUnregisterStatus.OwnershipMismatch, await directory.UnregisterAsync(actorId, owner, ct));
await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() => directory.ResolveAsync(actorId, ct).AsTask());
await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.ResolveAsync(actorId, canceled.Token).AsTask());
Assert.Equal(0, gateway.PendingCount);
```

Run the focused command after each behavior and confirm it fails for the missing mapping or cleanup.

- [ ] **Step 6: Implement the minimal mappings and cleanup**

Map the exact existing enums without weakening unknown-value handling:

```csharp
private static ActorDirectoryRegisterStatus ToRegisterStatus(ActorDirectoryOperationStatus status) => status switch
{
    ActorDirectoryOperationStatus.Registered => ActorDirectoryRegisterStatus.Registered,
    ActorDirectoryOperationStatus.AlreadyRegistered => ActorDirectoryRegisterStatus.AlreadyRegistered,
    ActorDirectoryOperationStatus.Conflict => ActorDirectoryRegisterStatus.Conflict,
    _ => throw new ActorDirectoryUnavailableException("Actor directory returned an invalid register status.")
};
```

Implement the equivalent unregister mapping and ensure every path either completes or removes the gateway pending registration.

- [ ] **Step 7: Run focused tests and commit**

Run the Task 1 command. Expected: all `SeededActorDirectoryTests` pass and `gateway.PendingCount` is zero after every terminal path.

```powershell
git add src/Lakona.Game.Server/Actors/ActorDirectoryClusterMessages.cs src/Lakona.Game.Server/Actors/SeededActorDirectory.cs tests/Lakona.Game.Server.Tests/Actors/SeededActorDirectoryTests.cs
git commit -m "Add seeded actor directory client"
```

### Task 2: Seed-local directory command handler

**Files:**
- Create: `tests/Lakona.Game.Server.Tests/Actors/ActorDirectoryClusterHandlerTests.cs`
- Create: `src/Lakona.Game.Server/Actors/ActorDirectoryClusterHandler.cs`

- [ ] **Step 1: Write failing handler tests for all three commands**

Construct the handler with `InMemoryActorDirectory`, a recording `IClusterNodeSender`, and local node `data-1`. For register, resolve, and unregister, assert the handler returns `Accepted` and sends exactly one reply to the request `SourceNode` with the same correlation id:

```csharp
var status = await handler.HandleAsync(
    CreateMessage(
        ActorDirectoryClusterProtocol.RegisterKind,
        new ActorDirectoryRequest("user/player-1", "battle-1")),
    TestContext.Current.CancellationToken);

Assert.Equal(ClusterSendStatus.Accepted, status);
Assert.Equal(new NodeId("gateway-1"), sender.DestinationNode);
Assert.Equal("correlation-1", sender.Message.CorrelationId);
```

Also assert unrelated message kinds return `RouteNotFound` without sending a reply.

- [ ] **Step 2: Run the tests and verify RED**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter ActorDirectoryClusterHandlerTests
```

Expected: compile failure because `ActorDirectoryClusterHandler` does not exist.

- [ ] **Step 3: Implement the handler**

`HandleAsync` must recognize only the three protocol kinds, deserialize `ActorDirectoryRequest`, call the matching `IActorDirectory` operation, serialize an `ActorDirectoryReply`, and send it with `RemoteActorGateway.SendReplyAsync`. Invalid JSON or missing owner for register/unregister must return a typed error reply, not crash the cluster listener. Return the reply send status so node unavailability remains observable.

- [ ] **Step 4: Add RED tests for invalid payload and reply-send failure**

Assert invalid JSON produces an error reply and that `ClusterSendStatus.Failed` from the recording sender is returned unchanged. Run the focused command and observe the expected failures before extending the implementation.

- [ ] **Step 5: Finish minimal error handling, verify, and commit**

Run the Task 2 command. Expected: all handler tests pass.

```powershell
git add src/Lakona.Game.Server/Actors/ActorDirectoryClusterHandler.cs tests/Lakona.Game.Server.Tests/Actors/ActorDirectoryClusterHandlerTests.cs
git commit -m "Handle actor directory commands on cluster seed"
```

### Task 3: Dependency injection and obsolete discovery cleanup

**Files:**
- Modify: `tests/Lakona.Game.Server.Tests/Hosting/LakonaClusterEndpointServiceCollectionExtensionsTests.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs`
- Modify: `tests/Lakona.Game.Server.Tests/Hosting/LakonaGameClusterRegistrationHostedServiceTests.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaGameClusterRegistrationHostedService.cs`
- Delete: `tests/Lakona.Game.Server.Tests/ActorDirectoryClientTests.cs`
- Delete: `src/Lakona.Game.Server/Actors/ActorDirectoryClient.cs`
- Delete: `src/Lakona.Game.Server/Actors/IActorDirectoryHostClient.cs`
- Delete: `src/Lakona.Game.Server/Actors/ActorDirectoryLabels.cs`

- [ ] **Step 1: Write failing DI-selection tests**

Add tests that call `AddLakonaGameServerActors()` before `AddLakonaGameClusterEndpoint()`:

```csharp
[Fact]
public void Cluster_seed_keeps_local_actor_directory_and_registers_handler()
{
    var provider = BuildProvider(endpoint: Seed, seeds: [Seed]);
    Assert.IsType<InMemoryActorDirectory>(provider.GetRequiredService<IActorDirectory>());
    Assert.Contains(provider.GetServices<IClusterMessageHandler>(), handler => handler is ActorDirectoryClusterHandler);
}

[Fact]
public void Remote_node_uses_seeded_actor_directory_without_local_handler()
{
    var provider = BuildProvider(endpoint: Gateway, seeds: [Seed]);
    Assert.IsType<SeededActorDirectory>(provider.GetRequiredService<IActorDirectory>());
    Assert.DoesNotContain(provider.GetServices<IClusterMessageHandler>(), handler => handler is ActorDirectoryClusterHandler);
}
```

- [ ] **Step 2: Run and verify RED**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "LakonaClusterEndpointServiceCollectionExtensionsTests|LakonaGameClusterRegistrationHostedServiceTests"
```

Expected: remote node still resolves `InMemoryActorDirectory`, no directory handler is registered, and registration still contains the obsolete label.

- [ ] **Step 3: Implement seed-local versus remote DI**

In `AddLakonaGameClusterEndpoint`, reuse `SelectRemoteDirectorySeed`:

```csharp
if (directorySeed is null)
{
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IClusterMessageHandler, ActorDirectoryClusterHandler>());
}
else
{
    services.RemoveAll<IActorDirectory>();
    services.AddSingleton<IActorDirectory>(provider => new SeededActorDirectory(
        provider.GetRequiredService<RemoteActorGateway>(),
        provider.GetRequiredService<INodeMessenger>(),
        provider.GetRequiredService<LocalActorNodeIdentity>(),
        directorySeed));
}
```

Keep `AddLakonaGameServerActors()` process-local defaults unchanged. This preserves direct actor-only test hosts while cluster-enabled remote nodes obtain the shared directory.

- [ ] **Step 4: Remove obsolete discovery surface and label**

Delete the three unused production types and their test file. Change cluster registration labels to an empty ordinal dictionary:

```csharp
private static IReadOnlyDictionary<string, string> CreateLabels() =>
    new Dictionary<string, string>(StringComparer.Ordinal);
```

Update the registration test to `Assert.Empty(registration.Labels)`.

- [ ] **Step 5: Run focused tests and public-name scan**

Run the Task 3 test command, then:

```powershell
rg -n "ActorDirectoryClient|IActorDirectoryHostClient|ActorDirectoryLabels|actor-directory.*label" src tests docs
```

Expected: focused tests pass; scan finds no obsolete discovery surface.

- [ ] **Step 6: Commit**

```powershell
git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests
git commit -m "Wire actor directory through cluster seed"
```

### Task 4: Agar topology contract

**Files:**
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs`

- [ ] **Step 1: Add failing topology assertions**

Extend the existing data/gateway/battle service-provider tests:

```csharp
Assert.IsType<InMemoryActorDirectory>(dataProvider.GetRequiredService<IActorDirectory>());
Assert.IsType<SeededActorDirectory>(gatewayProvider.GetRequiredService<IActorDirectory>());
Assert.IsType<SeededActorDirectory>(battleProvider.GetRequiredService<IActorDirectory>());
```

The cross-client shared-state test belongs in
`ActorDirectoryClusterHandlerTests`: create one `InMemoryActorDirectory` and
one handler, then create two gateways plus two `SeededActorDirectory` clients.
Use an `InProcessSeedMessenger` per client that forwards the command to the
same handler and routes the handler's node-directed reply into the matching
gateway reply handler. Register `user/player-1` through the battle client and
resolve it through the gateway client:

```csharp
var register = await battleDirectory.RegisterAsync(actorId, new NodeId("battle-1"), ct);
var resolved = await gatewayDirectory.ResolveAsync(actorId, ct);
Assert.Equal(ActorDirectoryRegisterStatus.Registered, register);
Assert.NotNull(resolved);
Assert.Equal(new NodeId("battle-1"), resolved.Node);
```

- [ ] **Step 2: Run and verify RED**

Run:

```powershell
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore --filter DistributedTopologyConfigurationTests
```

Expected: the new Actor Directory topology assertion or cross-node round trip fails before the final wiring is complete.

- [ ] **Step 3: Make only test-fixture adjustments required by production DI**

Build each provider through the existing `CreateProviderForEnvironment` helper,
which already calls `AddLakonaGameServer`. Do not register an Agar-owned
`IActorDirectory`; if a fixture currently replaces it, remove that replacement
so the production seed-selection registration is exercised.

- [ ] **Step 4: Verify and commit**

Run the Task 4 command. Expected: all distributed topology tests pass.

```powershell
git add samples/Game.Unity.Agar/tests/BusinessLogic.Tests/DistributedTopologyConfigurationTests.cs
git commit -m "Cover shared actor directory in Agar topology"
```

### Task 5: Durable documentation and package closure

**Files:**
- Modify: `docs/actor.md`
- Modify: `docs/cluster.md`
- Modify: `src/Lakona.Game.Server/README.md`

- [ ] **Step 1: Update current documentation**

Document these exact contracts:

- process-local actor-only hosts default to `InMemoryActorDirectory`;
- cluster seed owns the ephemeral directory;
- remote nodes use the configured seed without extra actor-directory config;
- seed restart may clear ownership records;
- transport failure surfaces as `ActorDirectoryUnavailableException`;
- no node advertises an actor-directory discovery label.

- [ ] **Step 2: Run documentation and version guards**

Run:

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1 -BaseRef 13026ba4
```

Expected: both pass. `Lakona.Game.Server` remains `0.12.0` because this work belongs to the same unreleased integrated source change; no second bump is added.

- [ ] **Step 3: Commit**

```powershell
git add docs/actor.md docs/cluster.md src/Lakona.Game.Server/README.md docs/superpowers/specs/2026-07-10-seeded-actor-directory-design.md
git commit -m "Document seeded actor directory"
```

### Task 6: Integrated verification and review gate

**Files:**
- Test only; no planned production edits.

- [ ] **Step 1: Run focused and full .NET validation**

Run sequentially:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore
git diff --check
```

Expected: all tests pass and no whitespace errors are reported.

- [ ] **Step 2: Run audited three-node Unity E2E**

Run only the repository smoke entry point:

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1 -TimeoutSeconds 900
```

Expected: Docker readiness succeeds, Unity exits successfully, and `.tmp/agar-three-node` contains an audited test-result XML with exactly one passing target test. Guest login must pass the actor-host creation and subsequent generated UserActor lookup.

- [ ] **Step 3: Evaluate the five-second acceptance separately**

If the audited Unity test does not yet assert and pass entry into battle within the agreed five-second SLA, do not weaken the timeout. Record the measured timestamps, return to systematic debugging, and create the smallest RED matchmaking timing test before changing matchmaking code.

- [ ] **Step 4: Independent strong review**

Dispatch a fresh reviewer with the design, base commit `b5ff16c7`, final head, full test output, E2E result path, and these risks: pending-reply cleanup, caller cancellation, seed/local DI selection, accidental handler recursion, serializer package boundaries, and removal of obsolete public types.

- [ ] **Step 5: Address findings with RED tests and re-run verification**

For every accepted finding, first add the smallest failing regression test, then implement the fix and repeat Steps 1-2. Finish only when the independent review has no unresolved correctness findings.
