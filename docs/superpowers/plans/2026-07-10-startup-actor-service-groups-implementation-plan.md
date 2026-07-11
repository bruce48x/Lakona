# Keyed Startup Actor Service Groups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Execution status:** Active as of 2026-07-11.

**Goal:** Replace named/configured Startup Actors with replicated, ready-advertised service groups registered as `RegisterStartup<TActor, TKey>(selector)` and called through generated `.Startup(key)` refs with safe failover.

**Architecture:** Hotfix registration defines one Startup group and one fixed, application-owned selector per actor type. Every node whose `Lakona:ActorHosts` includes that actor starts one framework-identified local replica; only successfully started replicas are advertised in node membership. A generated Startup ref delegates selection and invocation to a runtime service that preserves the typed key, uses stable candidate ordering, validates selector output, retries only definitely-not-executed attempts, and never exposes the physical actor id.

**Tech Stack:** .NET 10, C# 13, xUnit v3, Roslyn incremental source generation, Lakona hotfix publication transactions, SQL node directory, cluster RPC + MemoryPack, Lakona.Tool renderers, Docker Compose, Unity 2022 LTS PlayMode smoke tests.

---

## Scope and risk checkpoint

- **Classification:** Large cross-cutting change. It changes public registration and generated call APIs, node membership protocol/storage, hotfix publication lifecycle, runtime selection/retry behavior, configuration, templates, and samples.
- **Prerequisite:** The node-directed actor-reply milestone is already shipped;
  this plan assumes replies no longer depend on route-directory entries.
- **Continuity owner:** One implementation owner must own the descriptor model, publication transaction, internal replica identity, resolver, retry classification, and generated Startup ref until the runtime milestone is green.
- **Safe independent slices after runtime stabilizes:** SQL/RPC adapter checklist review, docs/source scans, and sample text migration. Do not delegate concurrent edits to the same generator, lifecycle, or server-runtime files.
- **Compatibility:** Breaking by design. Remove the named `RegisterStartup(string, createPlan)` API, `ActorStartupPlan` model, `Lakona:StartupActors`, and old sample/template usage without shims.
- **State contract:** Replica state is in-memory and not replicated. Failover may lose matchmaking queues and leaderboard state accumulated only on the failed replica.
- **Primary risks:** advertising before actor start, selecting capability rather than readiness, retrying an ambiguous attempt, hotfix rollback leaving descriptors or actors behind, generator/runtime key-type disagreement, node epoch races, or silently accepting removed configuration.
- **Sequencing correction:** Task 1 introduces typed registrations while the
  old startup-plan path remains isolated as an explicitly temporary legacy
  compile bridge. Task 6 switches lifecycle ownership to typed replicas; Task 9
  migrates the remaining consumers and deletes the bridge. The final public
  surface still has no compatibility shim. This avoids an unbuildable interval
  between the authoring-model and lifecycle milestones.
- **Versioning correction:** The fixed versions originally listed in Task 11
  predate later releases. Final versions are derived from the branch base and
  the package-version graph guard, using minor + 1 and patch zero for every
  affected release-graph package.

## Public contracts fixed by this plan

```csharp
actors.RegisterStartup<MatchmakingActor, MatchmakingQueueId>(
    static context => SelectStableHash(context.Candidates, context.Key.Value));

await matchmaking
    .Startup(new MatchmakingQueueId("default"))
    .CallAsync(MatchmakingBehavior.EnqueueAsync, request, cancellationToken);
```

- `TKey` is selector input only. It does not become the physical actor id and does not create independent state.
- There is one Startup group per actor type and one selector fixed at registration.
- Different keys may select the same replica and share its state.
- Physical id is `<actor-name>/@startup/<node-id>` and is absent from generated business APIs.
- Candidates are ready, compatible, non-expired descriptors sorted by ordinal node id.
- Failover reruns the selector with the same key and the failed candidate removed.

## File map

### Hotfix authoring and snapshots

- Modify `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorHostBuilder.cs`.
- Replace `ActorStartupDeclaration.cs` with the typed actor/key/selector declaration.
- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/StartupActorCandidate.cs`.
- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/StartupActorSelectionContext.cs`.
- Delete `ActorStartupContext.cs`, `ActorStartupInstance.cs`, and `ActorStartupPlan.cs`.
- Modify `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanner.cs` and `HotfixBehaviorScanResult.cs`.
- Modify `src/Lakona.Game.Server.Hotfix/IHotfixRuntimeAccessor.cs` and `HotfixManager.cs`.
- Replace `src/Lakona.Game.Server.Hotfix/Runtime/IHotfixRuntimePublicationParticipant.cs` with a transactional prepare/activate/commit/rollback contract.

### Cluster membership, adapters, and storage

- Create `src/Lakona.Game.Cluster/Nodes/StartupActorDescriptor.cs`.
- Modify `NodeRegistration.cs`, `NodeRecord.cs`, `ClusterNodeDescriptor.cs`, `NodeDirectoryQuery.cs`, and `InMemoryNodeDirectory.cs`.
- Modify `src/Lakona.Game.Cluster/Messaging/IClusterNodeSender.cs` and `ClusterNodeSender.cs` for expected-node-epoch sends.
- Modify `src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryMessages.cs` and `NodeDirectoryRecordConverter.cs`.
- Modify `src/Lakona.Game.Cluster.Rpc.MemoryPack/Generation/cluster-rpc-memorypack.schema.json`.
- Modify `src/Lakona.Game.Cluster.Rpc.MemoryPack.Generator/**` only if the schema generator needs a new supported property type; generated output remains build output and is not committed.
- Modify `src/Lakona.Game.Cluster.Sql/SqlNodeDirectory.cs`, `SqlNodeDirectorySchema.cs`, and all three `schema/*/001-lakona-cluster-nodes.sql` files.

### Server lifecycle, selection, and invocation

- Replace `src/Lakona.Game.Server/Actors/ActorStartupHostedService.cs` with `StartupActorHostedService.cs`.
- Create `StartupActorDescriptorCatalog.cs`, `StartupActorIdentity.cs`, `StartupActorTarget.cs`, `StartupActorUnavailableException.cs`, `StartupActorSelectionException.cs`, `IStartupActorInvoker.cs`, and `StartupActorInvoker.cs` under `src/Lakona.Game.Server/Actors/`.
- Create `src/Lakona.Game.Server/Hotfix/StartupActorPublicationParticipant.cs`.
- Create `src/Lakona.Game.Server/Hosting/IClusterNodeRegistrationRefresher.cs`.
- Modify `LakonaGameClusterRegistrationHostedService.cs`, `ActorServiceCollectionExtensions.cs`, `LakonaGameServerServiceCollectionExtensions.cs`, `RemoteActorInvocation.cs`, `RemoteActorInvocationResult.cs`, and `RemoteActorInvoker.cs`.

### Generator

- Modify `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`, its actor info model in the same project, and `HotfixGeneratorDiagnostics.cs` if a diagnostic is required.
- Modify `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs` and `GeneratorTestHost.cs`.

### Configuration, tooling, samples, and docs

- Modify `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`, readiness/guardrail files, and their tests to reject then remove `Lakona:StartupActors`.
- Modify `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`, `ServerAppRenderer.cs`, Tool tests, and Tool README.
- Migrate `samples/Game.Unity.Agar` Hotfix calls, appsettings, Compose, tests, and README.
- Migrate `samples/Game.Godot.Chat` Hotfix calls, appsettings, and E2E expectations.
- Modify `docs/actor.md`, `docs/cluster.md`, and `docs/configuration.md` plus package READMEs containing the old model.

## Task 1: Introduce the typed Startup authoring model

**Files:**

- Modify: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorHostBuilder.cs`
- Modify: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartupDeclaration.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/StartupActorCandidate.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/StartupActorSelectionContext.cs`
- Delete: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartupContext.cs`
- Delete: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartupInstance.cs`
- Delete: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartupPlan.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/ActorHostBuilderTests.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixBehaviorScannerTests.cs`

- [x] **Step 1: Write registration red tests**

Add tests proving:

```csharp
builder.RegisterStartup<TestActor, TenantKey>(
    static context => context.Candidates[0]);
```

produces one declaration with `ActorType == typeof(TestActor)`, `KeyType == typeof(TenantKey)`, and an invokable typed selector. Add duplicate-actor tests even when the second registration uses a different key type. Add scanner tests for duplicates across two `[HotfixStartup]` classes.

- [x] **Step 2: Run the red tests**

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "FullyQualifiedName~ActorHostBuilderTests|FullyQualifiedName~HotfixBehaviorScannerTests"
```

Expected: FAIL because only the named startup API exists.

- [x] **Step 3: Implement immutable selector inputs**

Use these public shapes:

```csharp
public sealed record ActorStartupDeclaration(
    Type ActorType,
    Type KeyType,
    Delegate Selector)
{
    public static ActorStartupDeclaration Create<TActor, TKey>(
        Func<StartupActorSelectionContext<TKey>, StartupActorCandidate> selector)
        => new(typeof(TActor), typeof(TKey), selector);
}

public sealed record StartupActorSelectionContext<TKey>(
    IReadOnlyList<StartupActorCandidate> Candidates,
    TKey Key);
```

`StartupActorCandidate` copies metadata into an ordinal read-only dictionary and exposes `string NodeId`, `long NodeEpoch`, and metadata. Its constructor rejects blank node ids and negative epochs.

- [x] **Step 4: Replace builder state and API**

```csharp
private readonly HashSet<Type> _startupActors = [];

public void RegisterStartup<TActor, TKey>(
    Func<StartupActorSelectionContext<TKey>, StartupActorCandidate> selector)
{
    ArgumentNullException.ThrowIfNull(selector);
    if (!_startupActors.Add(typeof(TActor)))
    {
        throw new InvalidOperationException(
            $"Actor startup for '{typeof(TActor).FullName}' is already registered.");
    }

    _startups.Add(ActorStartupDeclaration.Create<TActor, TKey>(selector));
}
```

Delete the named overload and plan/instance types.

- [x] **Step 5: Update scanner duplicate identity**

Replace startup-name sets with actor-type sets. Diagnostics must name the actor type and both declarations must not enter the snapshot.

- [x] **Step 6: Run tests and commit**

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "FullyQualifiedName~ActorHostBuilderTests|FullyQualifiedName~HotfixBehaviorScannerTests"
git add src/Lakona.Game.Server.Hotfix.Abstractions src/Lakona.Game.Server.Hotfix/Scanning tests/Lakona.Game.Server.Hotfix.Tests
git commit -m "Define keyed startup actor registrations"
```

Expected: PASS; old startup classes and named registration no longer compile anywhere outside still-to-migrate consumers.

## Task 2: Add ready Startup descriptors to the node model

**Files:**

- Create: `src/Lakona.Game.Cluster/Nodes/StartupActorDescriptor.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeRegistration.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeRecord.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/ClusterNodeDescriptor.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeDirectoryQuery.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/InMemoryNodeDirectory.cs`
- Test: `tests/Lakona.Game.Cluster.Tests/NodeDirectoryModelTests.cs`
- Test: `tests/Lakona.Game.Cluster.Tests/InMemoryNodeDirectoryTests.cs`

- [x] **Step 1: Write descriptor and query red tests**

Test constructor copying/validation, `NodeRecord.HasStartupActor`, registration round-trip, and a query that returns only Ready, non-expired nodes advertising the requested startup actor and policy hash. Also prove `ActorHosts` capability alone does not satisfy a Startup query.

- [x] **Step 2: Run the red tests**

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --no-restore --filter "FullyQualifiedName~NodeDirectoryModelTests|FullyQualifiedName~InMemoryNodeDirectoryTests"
```

Expected: FAIL because the model has no startup descriptor list.

- [x] **Step 3: Implement the descriptor**

```csharp
public sealed class StartupActorDescriptor
{
    public StartupActorDescriptor(
        string actor,
        string policyHash,
        string buildTag,
        IReadOnlyDictionary<string, string>? metadata = null);

    public string Actor { get; }
    public string PolicyHash { get; }
    public string BuildTag { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}
```

Match `NodeActorHostDescriptor` validation and defensive-copy behavior, but keep it a distinct type.

- [x] **Step 4: Extend node records without conflating capability and readiness**

Add `IReadOnlyList<StartupActorDescriptor> StartupActors` to `NodeRegistration`, `NodeRecord`, and `ClusterNodeDescriptor`. Add it as a new constructor argument after `actorHosts`; update every construction site explicitly. Provide only constructor delegation needed to keep internal test setup readable; do not preserve a public semantic path that silently drops descriptors.

- [x] **Step 5: Extend queries**

Add `StartupActorName` and `StartupActorPolicyHash` to `NodeDirectoryQuery`, require name when hash is set, and update in-memory matching. Query results remain stably ordered by callers, not by the directory.

- [x] **Step 6: Run tests and commit**

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --no-restore --filter "FullyQualifiedName~NodeDirectoryModelTests|FullyQualifiedName~InMemoryNodeDirectoryTests"
git add src/Lakona.Game.Cluster/Nodes tests/Lakona.Game.Cluster.Tests
git commit -m "Advertise ready startup actor replicas"
```

Expected: PASS.

## Task 3: Preserve descriptors through SQL, cluster RPC, and MemoryPack

**Files:**

- Modify: `src/Lakona.Game.Cluster.Sql/SqlNodeDirectory.cs`
- Modify: `src/Lakona.Game.Cluster.Sql/SqlNodeDirectorySchema.cs`
- Modify: `src/Lakona.Game.Cluster.Sql/schema/sqlite/001-lakona-cluster-nodes.sql`
- Modify: `src/Lakona.Game.Cluster.Sql/schema/postgres/001-lakona-cluster-nodes.sql`
- Modify: `src/Lakona.Game.Cluster.Sql/schema/mysql/001-lakona-cluster-nodes.sql`
- Modify: `src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryMessages.cs`
- Modify: `src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryRecordConverter.cs`
- Modify: `src/Lakona.Game.Cluster.Rpc.MemoryPack/Generation/cluster-rpc-memorypack.schema.json`
- Test: `tests/Lakona.Game.Cluster.Sql.Tests/SqlNodeDirectoryTests.cs`
- Test: `tests/Lakona.Game.Cluster.Rpc.Tests/NodeDirectoryClientTests.cs`
- Test: `tests/Lakona.Game.Cluster.Rpc.Tests/ClusterRpcMemoryPackDtoTests.cs`
- Test: `tests/Lakona.Game.Cluster.Rpc.Tests/ClusterRpcMemoryPackSchemaTests.cs`

- [x] **Step 1: Write adapter red tests**

Round-trip two startup descriptors with different build tags/metadata through SQL registration/query, RPC DTO conversion, and MemoryPack. Assert a startup query does not match a capability-only record.

- [x] **Step 2: Run the red tests**

```powershell
dotnet test tests/Lakona.Game.Cluster.Sql.Tests/Lakona.Game.Cluster.Sql.Tests.csproj --no-restore --filter "FullyQualifiedName~Startup"
dotnet test tests/Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj --no-restore --filter "FullyQualifiedName~Startup|FullyQualifiedName~ClusterRpcMemoryPack"
```

Expected: FAIL because adapters omit the new property.

- [x] **Step 3: Add SQL storage with upgrade support**

New tables contain:

```sql
startup_actors_json TEXT NOT NULL
```

`EnsureCreatedAsync` must also support existing tables: probe `SELECT startup_actors_json ... WHERE 1 = 0`; if the column is absent, execute dialect-safe `ALTER TABLE ... ADD COLUMN startup_actors_json TEXT NULL`, tolerate a concurrent duplicate-column race by re-probing, then backfill null rows to `'[]'`. Reads use `COALESCE(startup_actors_json, '[]')` until the migration is complete. Inserts and updates always write the serialized descriptor list.

- [x] **Step 4: Append RPC/MemoryPack fields compatibly**

Add `StartupActorDto`, append `StartupActors` to `NodeRegistrationDto` and `NodeRecordDto`, and append startup query fields after existing fields. In `cluster-rpc-memorypack.schema.json`, append properties rather than reordering existing properties so MemoryPack member indexes remain stable.

- [x] **Step 5: Run adapter suites and commit**

```powershell
dotnet test tests/Lakona.Game.Cluster.Sql.Tests/Lakona.Game.Cluster.Sql.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj --no-restore
git add src/Lakona.Game.Cluster.Sql src/Lakona.Game.Cluster.Rpc src/Lakona.Game.Cluster.Rpc.MemoryPack tests/Lakona.Game.Cluster.Sql.Tests tests/Lakona.Game.Cluster.Rpc.Tests
git commit -m "Persist startup actor descriptors"
```

Expected: PASS, including schema order checks.

## Task 4: Make node epoch and retry safety explicit

**Files:**

- Modify: `src/Lakona.Game.Cluster/Messaging/IClusterNodeSender.cs`
- Modify: `src/Lakona.Game.Cluster/Messaging/ClusterNodeSender.cs`
- Modify: `src/Lakona.Game.Server/Actors/ActorHostClient.cs`
- Modify: `src/Lakona.Game.Server/Actors/RemoteActorGateway.cs`
- Modify: `src/Lakona.Game.Server/Actors/RemoteActorInvocation.cs`
- Create: `src/Lakona.Game.Server/Actors/RemoteActorRetrySafety.cs`
- Modify: `src/Lakona.Game.Server/Actors/RemoteActorInvocationResult.cs`
- Modify: `src/Lakona.Game.Server/Actors/RemoteActorInvoker.cs`
- Test: `tests/Lakona.Game.Cluster.Tests/ClusterNodeSenderTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/RemoteActorGatewayTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/RemoteActorInvokerTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/HotfixActorClusterHandlerTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/TypedActorDispatcherTests.cs`

- [x] **Step 1: Write epoch and ambiguity red tests**

Test that a sender given expected epoch 7 returns `NodeEpochMismatch` without calling `INodeMessenger` when the directory record is epoch 8. Missing/expired node returns `StaleRoute` without dispatch. A transport/handler `Failed` result remains indeterminate. `RouteNotFound`, `HandlerUnavailable`, `StaleRoute`, and `NodeEpochMismatch` are the only statuses classified `DefinitelyNotExecuted`.

- [x] **Step 2: Run the red tests**

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --no-restore --filter "FullyQualifiedName~ClusterNodeSenderTests"
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteActorInvokerTests"
```

Expected: FAIL because sends do not carry an expected epoch and invocation results do not classify retry safety.

- [x] **Step 3: Change the node sender contract**

```csharp
ValueTask<ClusterSendStatus> SendAsync(
    NodeId nodeId,
    long? expectedNodeEpoch,
    RouteKey route,
    ClusterMessage message,
    CancellationToken cancellationToken = default);
```

Update `ActorHostClient`, `RemoteActorGateway`, `RemoteActorInvoker`, their test
fakes, and the node-directed generated-handler fixtures. Actor-host and reply
sends pass `expectedNodeEpoch: null`; Startup invocations pass the descriptor
epoch. `ClusterNodeSender` returns `StaleRoute` for missing/expired records,
`NodeEpochMismatch` before messenger dispatch for a mismatch, and
`HandlerUnavailable` for a missing cluster endpoint. It passes the actual
record epoch into `RouteLocation`.

- [x] **Step 4: Carry safety through remote invocation**

Add nullable `ExpectedNodeEpoch` to `RemoteActorInvocation`. Add:

```csharp
public enum RemoteActorRetrySafety
{
    Indeterminate = 0,
    DefinitelyNotExecuted = 1
}
```

`RemoteActorInvocationResult` exposes `RetrySafety`. Only the four statuses listed above map to `DefinitelyNotExecuted`; timeout, cancellation, backpressure, serialization, deserialization, `Failed`, and reply-delivery failures remain `Indeterminate`.

- [x] **Step 5: Run suites and commit**

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteActorInvokerTests|FullyQualifiedName~ActorHostClient"
git add src/Lakona.Game.Cluster/Messaging src/Lakona.Game.Server/Actors tests/Lakona.Game.Cluster.Tests tests/Lakona.Game.Server.Tests
git commit -m "Classify safe startup actor failover attempts"
```

Expected: PASS.

## Task 5: Implement keyed selection and invocation

**Files:**

- Create: `src/Lakona.Game.Server/Actors/StartupActorIdentity.cs`
- Create: `src/Lakona.Game.Server/Actors/StartupActorTarget.cs`
- Create: `src/Lakona.Game.Server/Actors/StartupActorUnavailableException.cs`
- Create: `src/Lakona.Game.Server/Actors/StartupActorSelectionException.cs`
- Create: `src/Lakona.Game.Server/Actors/IStartupActorInvoker.cs`
- Create: `src/Lakona.Game.Server/Actors/StartupActorInvoker.cs`
- Modify: `src/Lakona.Game.Server/Actors/ActorServiceCollectionExtensions.cs`
- Create: `tests/Lakona.Game.Server.Tests/Actors/StartupActorInvokerTests.cs`

- [x] **Step 1: Write resolver/invoker red tests**

Cover all of these independently:

1. candidates are ordinal node-id sorted and contain descriptor metadata;
2. the same typed key and candidate set produce the same stable-hash selection;
3. wrong key type, selector throw, and outsider result produce `StartupActorSelectionException`;
4. no compatible ready descriptors produces `StartupActorUnavailableException`;
5. local selected target invokes `matchmaking/@startup/node-a`;
6. a definitely-not-executed remote attempt excludes that exact node/epoch and reruns the same selector with the original key;
7. timeout, `Failed`, reply-delivery failure, cancellation, serialization failure, and backpressure do not reselect;
8. after all candidates are excluded, return unavailable without looping.

- [x] **Step 2: Run the red tests**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~StartupActorInvokerTests"
```

Expected: FAIL because the service does not exist.

- [x] **Step 3: Implement identity and target**

```csharp
internal static ActorId CreateReplicaId(string actorName, NodeId node)
    => ActorId.From($"{actorName}/@startup/{node.Value}");

public sealed record StartupActorTarget(
    ActorId ActorId,
    NodeId Node,
    long NodeEpoch);
```

Actor names use the same `[ActorName]`/type-name normalization as placement.

- [x] **Step 4: Define the runtime invocation surface**

`IStartupActorInvoker` provides three generic operations: acknowledged `CallAsync` with and without result, and fire-and-forget `PostAsync`. Each receives `TActor`, `TKey`, request, actor/method names and remote method id, plus a generated local delegate accepting the resolved physical `ActorId`. This keeps local invocation reflection-free and keeps selection/retry in one runtime implementation.

Representative result call:

```csharp
ValueTask<TResult> CallAsync<TActor, TKey, TRequest, TResult>(
    TKey key,
    string actorName,
    string methodName,
    ulong remoteMethodId,
    TRequest request,
    Func<ActorId, TRequest, CancellationToken, ValueTask<TResult>> invokeLocal,
    CancellationToken cancellationToken = default)
    where TActor : class, IActor;
```

- [x] **Step 5: Implement selection validation and bounded failover**

For each attempt, acquire the current hotfix snapshot, find the single declaration by actor type, verify `KeyType == typeof(TKey)`, query ready descriptors matching actor/policy/build tag, remove excluded `(NodeId, NodeEpoch)` pairs, sort, invoke the typed selector, and verify the selected candidate is one of the exact offered candidates.

Both publication and resolution compute compatibility as
`"startup:v1:" + ActorType.FullName + ":" + KeyType.FullName`; the descriptor
build tag is the hotfix snapshot source version. This gives every node the same
deterministic policy identity without hashing delegate instances or key values.

The loop excludes a target only when the local runtime reports actor-not-found before dispatch or the remote result says `DefinitelyNotExecuted`. Never catch and retry business exceptions.

- [x] **Step 6: Register and test**

Register `IStartupActorInvoker` as singleton with all existing actor dependencies. Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~StartupActorInvokerTests"
```

Expected: PASS.

- [x] **Step 7: Commit**

```powershell
git add src/Lakona.Game.Server/Actors tests/Lakona.Game.Server.Tests/Actors/StartupActorInvokerTests.cs
git commit -m "Invoke keyed startup actor replicas"
```

## Task 6: Make replica lifecycle and advertisement transactional with hotfix publication

**Files:**

- Replace: `src/Lakona.Game.Server/Actors/ActorStartupHostedService.cs`
- Create: `src/Lakona.Game.Server/Actors/StartupActorHostedService.cs`
- Create: `src/Lakona.Game.Server/Actors/StartupActorDescriptorCatalog.cs`
- Create: `src/Lakona.Game.Server/Hosting/IClusterNodeRegistrationRefresher.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaGameClusterRegistrationHostedService.cs`
- Replace: `src/Lakona.Game.Server.Hotfix/Runtime/IHotfixRuntimePublicationParticipant.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/HotfixManager.cs`
- Create: `src/Lakona.Game.Server/Hotfix/StartupActorPublicationParticipant.cs`
- Modify: `src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs`
- Create: `tests/Lakona.Game.Server.Tests/Actors/StartupActorHostedServiceTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/Hosting/LakonaGameClusterRegistrationHostedServiceTests.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixManagerTests.cs`

- [x] **Step 1: Write initial lifecycle red tests**

Test that startup creates exactly one replica only when local `ActorHosts` contains the actor name, uses `matchmaking/@startup/node-a`, runs `[ActorStart]` before the descriptor appears, and publishes nothing on start failure. A gateway with empty `ActorHosts` starts none.

- [x] **Step 2: Write reload transaction red tests**

Test add, selector-only change, removal, activation failure, and rollback. Record events and assert exact order:

```text
withdraw removed descriptor
publish candidate runtime
start added replica
publish ready descriptor set
commit publication
stop removed replica
```

On activation failure assert current runtime is restored, newly created replicas are destroyed, and the previous descriptor set is republished.

- [x] **Step 3: Run the red tests**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~StartupActorHostedServiceTests|FullyQualifiedName~LakonaGameClusterRegistrationHostedServiceTests"
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "FullyQualifiedName~HotfixManagerTests"
```

Expected: FAIL because the old hosted service is configuration-driven and hotfix publication cannot roll back post-swap activation.

- [x] **Step 4: Replace publication callbacks with transactions**

Use this contract:

```csharp
public interface IHotfixRuntimePublicationParticipant
{
    ValueTask<IHotfixRuntimePublicationTransaction> PrepareAsync(
        HotfixRuntimeSnapshot previous,
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken = default);
}

public interface IHotfixRuntimePublicationTransaction : IAsyncDisposable
{
    ValueTask ActivateAsync(CancellationToken cancellationToken = default);
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}
```

`HotfixManager` prepares all participants, swaps to the candidate, activates all, and only then commits. If prepare/activate fails, restore the previous publication, roll back prepared transactions in reverse order, retire the candidate, and report reload failure. `CommitAsync` is cleanup-only; participant implementations catch/log cleanup failures so they cannot create a half-rolled-back publication.

- [x] **Step 5: Serialize node registration refreshes**

`LakonaGameClusterRegistrationHostedService` implements `IClusterNodeRegistrationRefresher`, protects register/heartbeat/refresh with one `SemaphoreSlim`, and builds `NodeRegistration` from both capability `ActorHosts` and the immutable ready-descriptor catalog. Remove fire-and-forget `Reloaded` event refresh; publication transactions explicitly refresh and can observe failure.

- [x] **Step 6: Implement startup lifecycle coordinator**

At initial host start, intersect snapshot registrations with `LakonaGameRuntimeOptions.ActorHosts`, create/ensure eligible replicas, then replace the descriptor catalog. The cluster registration hosted service starts afterward and publishes the ready set.

For reload, `PrepareAsync` computes the diff and withdraws descriptors that will disappear while leaving old actors alive. `ActivateAsync` starts additions against the now-current candidate runtime, then publishes the complete candidate descriptor set. `RollbackAsync` destroys additions and restores previous descriptors. `CommitAsync` stops and destroys removed replicas after the new descriptor set is visible.

Before `StartupActorHostedService.StartAsync` marks the coordinator started,
publication participation returns a no-op transaction. This is required because
the initial hotfix snapshot can publish before hosted services run. The hosted
service owns initial replica start/catalog population; later reloads use the
transaction path exactly once.

- [x] **Step 7: Run lifecycle suites and commit**

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~StartupActor|FullyQualifiedName~LakonaGameClusterRegistrationHostedServiceTests"
git add src/Lakona.Game.Server.Hotfix src/Lakona.Game.Server/Actors src/Lakona.Game.Server/Hosting src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs tests/Lakona.Game.Server.Hotfix.Tests tests/Lakona.Game.Server.Tests
git commit -m "Manage startup replicas through hotfix publication"
```

Expected: PASS.

## Task 7: Generate `.Startup(TKey key)` only for registered Startup actors

**Files:**

- Modify: `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`
- Modify: actor info records under `src/Lakona.Game.Server.Hotfix.Generators/`
- Modify: `src/Lakona.Game.Server.Hotfix.Generators/HotfixGeneratorDiagnostics.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Generators.Tests/GeneratorTestHost.cs`

- [x] **Step 1: Write generated-source red tests**

Compile a source containing a behavior and:

```csharp
actors.RegisterStartup<RoomActor, TenantAffinityKey>(
    static context => context.Candidates[0]);
```

Assert `RoomActors` exposes `RoomStartupRef Startup(TenantAffinityKey key)`, the ref holds `IStartupActorInvoker`, and its `CallAsync`/`PostAsync` methods pass the same key, actor name, method name/id, request, and local delegate. Assert no actor id or per-call selector appears. A non-startup actor collection must not expose `Startup`.

- [x] **Step 2: Run the red test**

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-restore --filter "FullyQualifiedName~Startup"
```

Expected: FAIL because registration invocations are not analyzed and no Startup ref is emitted.

- [x] **Step 3: Discover registrations semantically**

Scan invocation syntax, resolve the method symbol, and accept only the original definition `ActorHostBuilder.RegisterStartup<TActor,TKey>`. Build a map from actor symbol to key-type symbol. Report a deterministic error diagnostic for duplicate actor registrations or a registration whose actor has no generated hotfix behavior contract.

- [x] **Step 4: Emit the typed ref**

Add `StartupKeyType` to the generation info. Generate:

```csharp
public MatchmakingStartupRef Startup(MatchmakingQueueId key)
    => new MatchmakingStartupRef(_startupActors, _runtime, key);
```

The ref uses the same behavior-method resolution already emitted for Local/Route refs and delegates actual selection/failover to `IStartupActorInvoker`. It never derives the physical id.

- [x] **Step 5: Run generator tests and commit**

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-restore
git add src/Lakona.Game.Server.Hotfix.Generators tests/Lakona.Game.Server.Hotfix.Generators.Tests
git commit -m "Generate keyed startup actor refs"
```

Expected: PASS and generated sources compile under the test host.

## Task 8: Remove and reject `Lakona:StartupActors`

**Files:**

- Modify: `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`
- Modify: `src/Lakona.Game.Server/Health/LakonaGameReadinessRuntime.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedRuntime.cs`
- Delete: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedStartupActor.cs`
- Modify: `src/Lakona.Game.Server/Guardrails/Rules/ActorHostConfigurationRule.cs`
- Modify: corresponding configuration/readiness/guardrail tests.

- [x] **Step 1: Write removed-key red tests**

Add one configuration test for array syntax and one for environment JSON syntax. Both must throw with this actionable content:

```text
Lakona:StartupActors was removed. Register Startup Actors in
HotfixStartup.ConfigureActors with RegisterStartup<TActor, TKey>(selector),
and use Lakona:ActorHosts to choose capable nodes.
```

- [x] **Step 2: Run red tests**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~LakonaGameRuntimeOptionsTests|FullyQualifiedName~LakonaGameRuntimeValidatorTests"
```

Expected: FAIL because the old key still binds.

- [x] **Step 3: Reject then remove old model**

At the beginning of `FromConfiguration`, detect `section.GetSection("StartupActors").Exists()` and throw the message above. Remove `StartupActors`, `LakonaGameStartupActorOptions`, all binding/parsing helpers, readiness projection, resolved record, and uniqueness rule.

- [x] **Step 4: Run tests and commit**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Configuration|FullyQualifiedName~Guardrail|FullyQualifiedName~Readiness"
git add src/Lakona.Game.Server/Configuration src/Lakona.Game.Server/Health src/Lakona.Game.Server/Guardrails tests/Lakona.Game.Server.Tests
git commit -m "Remove startup actor configuration"
```

Expected: PASS.

## Task 9: Migrate Lakona.Tool, Godot Chat, and Agar

**Files:**

- Modify: `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`
- Modify: `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`
- Modify: `src/Lakona.Tool/README.md`
- Modify: Tool rendering/integration tests.
- Modify: `samples/Game.Godot.Chat/Server/Hotfix/HotfixStartup.cs`, all ChatRoom `.Route` call sites, appsettings, and tests.
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/HotfixStartup.cs`, Matchmaking/Leaderboard call sites, timer args/callback, appsettings, `docker-compose.yml`, README, and business/source-scan tests.

- [x] **Step 1: Change Tool expectations first**

Generated Chat projects must contain:

```csharp
actors.RegisterStartup<ChatRoomActor, string>(
    static context => context.Candidates[0]);
```

and calls use `.Startup(ChatRoomIds.Global)`. Generated appsettings contains `ActorHosts` but no `StartupActors`. Run Tool tests and confirm they fail before renderer edits.

- [x] **Step 2: Migrate Tool renderers**

Update renderer strings and architecture scans. Do not emit generated source folders or new configuration sections.

- [x] **Step 3: Migrate Godot Chat**

Use `RegisterStartup<ChatRoomActor, string>` and replace the four global room `.Route(ChatRoomIds.Global)` calls with `.Startup(ChatRoomIds.Global)`. Remove `StartupActors` from appsettings.

- [x] **Step 4: Migrate Agar registration and calls**

Use:

```csharp
actors.RegisterStartup<MatchmakingActor, MatchmakingQueueId>(
    static context => SelectStableHash(context.Candidates, context.Key.Value));
actors.RegisterStartup<LeaderboardActor, LeaderboardId>(
    static context => SelectStableHash(context.Candidates, context.Key.Value));
```

Replace global Matchmaking/Leaderboard `.Route(...)` calls with `.Startup(...)`. Keep User/Room placement unchanged.

- [x] **Step 5: Keep each matchmaking timer bound to its physical replica**

At timer creation, place `self.Context.Id.Value` in `MatchmakingTimerArgs.OwnerActorId`. In the callback use `IActorRuntime` with that exact `ActorId` to call `RunTickAsync`; do not call `.Startup(default)` because two replica timers would both select and tick the same primary. This internal lifecycle plumbing is not added to generated business APIs.

- [x] **Step 6: Remove config while preserving the user's Compose edit**

Remove every `Lakona__StartupActors` entry from Agar Compose and appsettings. Preserve the existing uncommitted `Lakona__Hotfix__DebugWatcher: "On"` lines exactly. Review the file-specific diff before staging.

- [x] **Step 7: Run sample and Tool tests**

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-restore
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore
pwsh -NoProfile -File samples/Game.Godot.Chat/test-game-godot-chat-e2e.ps1
```

Expected: PASS; generated and checked-in samples contain no old registration/config/call surface.

- [x] **Step 8: Commit migration without staging unrelated Compose hunks**

Stage the intended removal hunks and all sample/tool changes. Inspect `git diff --cached -- samples/Game.Unity.Agar/docker-compose.yml` and confirm the DebugWatcher additions are not claimed as implementation changes unless the user explicitly asks to include them.

```powershell
git commit -m "Migrate samples to startup actor groups"
```

## Task 10: Update durable docs and package READMEs

**Files:**

- Modify: `docs/actor.md`
- Modify: `docs/cluster.md`
- Modify: `docs/configuration.md`
- Modify: package READMEs under affected `src/Lakona.Game.*` directories.
- Modify: `samples/Game.Unity.Agar/README.md`

- [x] **Step 1: Document semantics, not migration history**

Document Startup service groups versus keyed actors, `TKey` as routing affinity only, fixed application-owned selector, descriptor readiness, internal identity, same-key failover, non-replicated state, and ActorHosts intersection. Configuration docs must say `Lakona:StartupActors` is invalid, not merely deprecated.

- [x] **Step 2: Run repository scans**

```powershell
rg -n "Lakona:StartupActors|Lakona__StartupActors|ActorStartupPlan|RegisterStartup\(\s*\"|\.Route\(ChatRoomIds\.Global\)" src tests samples docs --glob "!docs/superpowers/**"
```

Expected: only the intentional removed-key validation text and tests remain; no active example uses the old model.

- [x] **Step 3: Run docs checks and commit**

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
git add docs src/Lakona.Game.Cluster/README.md src/Lakona.Game.Cluster.Rpc/README.md src/Lakona.Game.Server/README.md src/Lakona.Game.Server.Hotfix.Abstractions/README.md src/Lakona.Game.Server.Hotfix/README.md samples/Game.Unity.Agar/README.md
git commit -m "Document startup actor service groups"
```

Expected: PASS.

## Task 11: Apply package versions and run final integration validation

**Files:**

- Modify package `.csproj` files identified below and any additional package required by the graph guard.

- [x] **Step 1: Apply minor+1 versions**

Set the packages first modified by this plan to:

```text
Lakona.Game.Cluster                         0.5.0
Lakona.Game.Cluster.Rpc                     0.4.0
Lakona.Game.Cluster.Rpc.MemoryPack          0.3.0
Lakona.Game.Cluster.Sql                     0.4.0
Lakona.Game.Server.Hotfix.Abstractions      0.4.0
Lakona.Game.Server.Hotfix                   0.6.0
Lakona.Game.Server.Hotfix.Generators        0.4.0
```

`Lakona.Game.Server` remains `0.12.0` and `Lakona.Tool` remains `0.18.0` from the prerequisite plan; they are already bumped once relative to the shared base and must not be bumped a second time for the same integrated release. If executing this plan independently, set them to those versions here. Non-packable `Lakona.Game.Cluster.Rpc.MemoryPack.Generator` receives no version.

- [x] **Step 2: Build once**

```powershell
dotnet build Lakona.slnx --no-restore
```

Expected: PASS.

- [x] **Step 3: Run affected suites sequentially**

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --no-build --no-restore
dotnet test tests/Lakona.Game.Cluster.Sql.Tests/Lakona.Game.Cluster.Sql.Tests.csproj --no-build --no-restore
dotnet test tests/Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj --no-build --no-restore
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-build --no-restore
dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-build --no-restore
dotnet test tests/Lakona.Game.Server.Generators.Tests/Lakona.Game.Server.Generators.Tests.csproj --no-build --no-restore
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-build --no-restore
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-build --no-restore
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-build --no-restore
```

Expected: all PASS.

- [x] **Step 4: Run package graph and old-surface guard**

```powershell
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
rg -n "Lakona:StartupActors|Lakona__StartupActors|ActorStartupPlan|ActorStartupInstance|RegisterStartup\(\s*\"" src tests samples docs --glob "!docs/superpowers/**"
```

Expected: version graph PASS; scan output contains only deliberate rejection tests/messages.

- [x] **Step 5: Run Godot and Agar acceptance**

```powershell
pwsh -NoProfile -File samples/Game.Godot.Chat/test-game-godot-chat-e2e.ps1
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
```

Expected: both PASS. The checked-in Agar topology publishes its capable data
node's ready Matchmaking and Leaderboard replicas, guest login succeeds, and
matchmaking reaches battle in five seconds. The two-replica selection/failover
contract is proven by `StartupActorInvokerTests` and lifecycle integration
tests because the checked-in three-node topology currently has one data node.

- [x] **Step 6: Run hygiene and inspect ownership**

```powershell
git diff --check
git status --short
git diff --stat
```

Expected: no generated output/build artifacts; no stale old API; the user's DebugWatcher Compose change remains correctly attributed and unstaged unless separately authorized.

- [x] **Step 7: Commit release metadata**

```powershell
git add src/Lakona.Game.Cluster/*.csproj src/Lakona.Game.Cluster.Rpc/*.csproj src/Lakona.Game.Cluster.Rpc.MemoryPack/*.csproj src/Lakona.Game.Cluster.Sql/*.csproj src/Lakona.Game.Server.Hotfix.Abstractions/*.csproj src/Lakona.Game.Server.Hotfix/*.csproj src/Lakona.Game.Server.Hotfix.Generators/*.csproj
git commit -m "Release startup actor service groups"
```

## Review gates

1. **Model/storage gate after Task 3:** verify capability and readiness remain distinct and all adapters preserve descriptors.
2. **Retry-safety gate after Task 5:** strongest available reviewer checks every status classification and confirms no ambiguous attempt can reselect.
3. **Lifecycle gate after Task 6:** strongest available reviewer checks publish/withdraw/start/stop/rollback ordering and concurrency serialization.
4. **Generator gate after Task 7:** review semantic registration discovery, key-type consistency, deterministic output, and absence of physical ids.
5. **Migration gate after Task 10:** checklist review of Tool, Godot, Agar, config scans, and user-owned Compose hunks.
6. **Final integration gate after Task 11:** complete-diff review with base/head commits, validation logs, skipped checks, version graph, and residual risks.

## Completion criteria

- `RegisterStartup<TActor,TKey>(selector)` is the only Startup registration API.
- `.Startup(key)` is generated only for matching registrations and never exposes an actor id or strategy argument.
- Every capable node starts one local replica; only ready replicas are advertised.
- SQL, in-memory, RPC, and MemoryPack directories preserve descriptors and queries.
- Same-key failover occurs only for definitely-not-executed attempts; ambiguous outcomes never retry.
- Hotfix add/remove/rollback leaves actor instances and advertisements consistent.
- `Lakona:StartupActors` is rejected and absent from active configs/templates/samples/docs.
- Package graph, affected suites, Godot E2E, and Agar three-node Unity E2E pass.
