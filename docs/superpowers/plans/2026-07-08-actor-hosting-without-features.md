# Actor Hosting Without Features Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove Feature from the user-facing Lakona.Game model and replace it with actor host configuration, startup actor declarations, actor lifecycle attributes, and code-registered actor placement selectors.

**Architecture:** Configuration declares only which actor kinds a node may host and which startup declarations it activates. Hotfix startup code registers services, startup actor plans, and pure placement selectors; generated actor accessors expose `Place(id)` for actors with registered selectors. Cluster node records advertise actor host descriptors with policy identity, while actor route ownership remains protected by the actor directory.

**Tech Stack:** .NET, C#, Microsoft.Extensions.Configuration, Microsoft.Extensions.Hosting, Roslyn source generators and analyzers, Lakona.Game.Server actors, Lakona.Game.Cluster node directory, Lakona.Game.Server.Hotfix scanner/runtime, Lakona.Tool templates, xUnit/NUnit test projects.

---

## Scope Check

This is one large cross-cutting implementation plan because actor host
descriptors, placement, remote actor creation, hotfix lifecycle, generated
accessors, and sample migration are strongly coupled. Keep one
continuity-preserving implementation owner for Tasks 1-9. Documentation cleanup
and source scans can be delegated after Task 9 compiles.

Breaking changes are allowed. Do not preserve Feature as a public authoring
surface in generated templates, samples, or docs.

## File Structure

### Cluster model and discovery

- Modify `src/Lakona.Game.Cluster/Nodes/NodeFeatureDescriptor.cs` into a new
  actor-host descriptor file or replace it with
  `src/Lakona.Game.Cluster/Nodes/NodeActorHostDescriptor.cs`.
- Modify `src/Lakona.Game.Cluster/Nodes/NodeRecord.cs` to expose `ActorHosts`.
- Modify `src/Lakona.Game.Cluster/Nodes/ClusterNodeDescriptor.cs` to expose
  `ActorHosts`.
- Modify `src/Lakona.Game.Cluster/Nodes/NodeDirectoryQuery.cs` to query by
  actor host name and policy hash.
- Modify `src/Lakona.Game.Cluster/Nodes/InMemoryNodeDirectory.cs`,
  `src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryMessages.cs`,
  `src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryRecordConverter.cs`, and
  `src/Lakona.Game.Cluster.Sql/SqlNodeDirectory*.cs` to carry actor host
  descriptors.
- Create `src/Lakona.Game.Cluster/Nodes/ActorHostName.cs` only if a strongly
  typed name improves call-site clarity.

### Server configuration and registration

- Modify `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs` to
  bind `ActorHosts` and `StartupActors`.
- Create `src/Lakona.Game.Server/Actors/ActorHostOptions.cs` for startup actor
  entries if object-shaped entries are supported in the first implementation.
- Modify `src/Lakona.Game.Server/Features/LakonaGameClusterRegistrationHostedService.cs`
  or move the registration logic into an actor-owned registration service.
- Modify `src/Lakona.Game.Server/Guardrails/*` to validate actor hosts and
  startup actors instead of `Lakona:Feature`.

### Hotfix authoring and scanning

- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartAttribute.cs`.
- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStopAttribute.cs`.
- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartCall.cs`.
- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStopCall.cs`.
- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorHostBuilder.cs`.
- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartupPlan.cs`.
- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorPlacementContext.cs`.
- Create `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorHostCandidate.cs`.
- Modify `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanner.cs` to
  scan `HotfixStartup.ConfigureServices` and `HotfixStartup.ConfigureActors`.
- Modify `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanResult.cs`
  to carry service, startup actor, placement selector, and lifecycle
  declarations without Feature declarations.

### Hotfix dispatch and actor runtime

- Create `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixActorLifecycleDescriptor.cs`.
- Create `src/Lakona.Game.Server.Hotfix/Runtime/HotfixActorLifecycleInvoker.cs`.
- Modify `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchTable.cs` to
  resolve actor lifecycle descriptors.
- Modify `src/Lakona.Game.Server/Actors/ActorHosting.cs` or
  `src/Lakona.Game.Server/Actors/IActorHostingRuntime.cs` to invoke actor start
  and stop hooks.
- Modify `src/Lakona.Game.Server/Hotfix/ActorHostingHotfixRollbackParticipant.cs`
  so candidate hotfix startup actor creation rolls back cleanly.

### Actor placement and remote create / ensure

- Create `src/Lakona.Game.Server/Actors/ActorPlacementService.cs`.
- Create `src/Lakona.Game.Server/Actors/IActorPlacementService.cs`.
- Create `src/Lakona.Game.Server/Actors/ActorHostCreateRequest.cs`.
- Create `src/Lakona.Game.Server/Actors/ActorHostCreateReply.cs`.
- Create `src/Lakona.Game.Server/Actors/IActorHostClient.cs`.
- Create or extend the cluster actor RPC path in
  `src/Lakona.Game.Server/Actors/HotfixActorClusterHandler.cs` for internal
  create / ensure requests.
- Modify `src/Lakona.Game.Server/Actors/RemoteActorInvoker.cs` and generated
  actor refs only after the placement service contract is stable.

### Source generation and analyzers

- Modify `src/Lakona.Game.Server.Generators/TypedActorGenerator.cs` to generate
  `Place(id)` accessors for actors with placement selectors.
- Modify `src/Lakona.Game.Server.Generators/TypedActorGeneratorDiagnostics.cs`
  for placement diagnostics.
- Modify `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs` and
  diagnostics for `[ActorStart]`, `[ActorStop]`, and `HotfixStartup`.
- Replace `src/Lakona.Game.Server.Hotfix.Generators/HotfixFeatureLifecycleAnalyzer.cs`
  with actor lifecycle diagnostics or narrow it to reject old Feature lifecycle
  shapes.

### Templates, samples, and docs

- Modify `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`.
- Modify `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`.
- Modify `tests/Lakona.Tool.Tests/Rendering/HotfixRendererTests.cs`.
- Modify `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs`.
- Migrate `samples/Game.Unity.Agar/Server/Hotfix/Features/*` into actor startup
  and behavior code.
- Migrate `samples/Game.Godot.Chat/Server/Hotfix/Features/ChatFeature.cs`.
- Modify docs under `docs/cluster.md`, `docs/configuration.md`,
  `docs/hotfix/architecture.md`, `docs/hotfix/actor-behavior.md`,
  `docs/actor.md`, `docs/tool/default-experience.md`, and package READMEs.

---

### Task 1: Add Cluster Actor Host Descriptors

**Files:**
- Create: `src/Lakona.Game.Cluster/Nodes/NodeActorHostDescriptor.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeRecord.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/ClusterNodeDescriptor.cs`
- Modify: `src/Lakona.Game.Cluster/Nodes/NodeDirectoryQuery.cs`
- Test: `tests/Lakona.Game.Cluster.Tests/NodeDirectoryModelTests.cs`
- Test: `tests/Lakona.Game.Cluster.Tests/InMemoryNodeDirectoryTests.cs`

- [ ] **Step 1: Write descriptor model tests**

Add these tests to `tests/Lakona.Game.Cluster.Tests/NodeDirectoryModelTests.cs`:

```csharp
[Fact]
public void NodeActorHostDescriptorRejectsBlankActorName()
{
    var exception = Assert.Throws<ArgumentException>(() => new NodeActorHostDescriptor(" "));
    Assert.Contains("Actor host name is required", exception.Message, StringComparison.Ordinal);
}

[Fact]
public void NodeActorHostDescriptorCopiesMetadata()
{
    var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["region"] = "us-east",
        ["tier"] = "battle"
    };

    var descriptor = new NodeActorHostDescriptor("room", "hash-a", "build-a", metadata);
    metadata["region"] = "changed";

    Assert.Equal("room", descriptor.Actor);
    Assert.Equal("hash-a", descriptor.PolicyHash);
    Assert.Equal("build-a", descriptor.BuildTag);
    Assert.Equal("us-east", descriptor.Metadata["region"]);
}
```

- [ ] **Step 2: Run descriptor tests and verify failure**

Run: `dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --no-restore --filter NodeDirectoryModelTests`

Expected: compile failure because `NodeActorHostDescriptor` does not exist.

- [ ] **Step 3: Add `NodeActorHostDescriptor`**

Create `src/Lakona.Game.Cluster/Nodes/NodeActorHostDescriptor.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster;

public sealed class NodeActorHostDescriptor
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    public NodeActorHostDescriptor(
        string actor,
        string policyHash,
        string buildTag,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Actor host name is required.", nameof(actor));
        }

        if (string.IsNullOrWhiteSpace(policyHash))
        {
            throw new ArgumentException("Actor host policy hash is required.", nameof(policyHash));
        }

        if (string.IsNullOrWhiteSpace(buildTag))
        {
            throw new ArgumentException("Actor host build tag is required.", nameof(buildTag));
        }

        Actor = actor;
        PolicyHash = policyHash;
        BuildTag = buildTag;
        Metadata = CopyMetadata(metadata);
    }

    public string Actor { get; }

    public string PolicyHash { get; }

    public string BuildTag { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static IReadOnlyDictionary<string, string> CopyMetadata(
        IReadOnlyDictionary<string, string>? metadata)
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
                throw new ArgumentException("Actor host metadata keys cannot be empty.", nameof(metadata));
            }

            copy[pair.Key] = pair.Value ?? throw new ArgumentException(
                "Actor host metadata values cannot be null.",
                nameof(metadata));
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
```

- [ ] **Step 4: Extend node record and descriptor constructors**

Modify `NodeRecord` and `ClusterNodeDescriptor` to add
`IReadOnlyList<NodeActorHostDescriptor> ActorHosts`. Keep `Features` in this
task only as an internal transitional property so existing tests continue to
compile. Add a `CopyActorHosts` helper that rejects null entries.

- [ ] **Step 5: Add query tests for actor host filtering**

Add this test to `tests/Lakona.Game.Cluster.Tests/InMemoryNodeDirectoryTests.cs`:

```csharp
[Fact]
public async Task QueryFiltersByActorHostAndPolicyHash()
{
    var directory = new InMemoryNodeDirectory();
    var now = DateTimeOffset.UtcNow;

    await directory.RegisterAsync(CreateRegistration(
        "node-a",
        actorHosts: [new NodeActorHostDescriptor("room", "policy-1", "build-1")]), now);
    await directory.RegisterAsync(CreateRegistration(
        "node-b",
        actorHosts: [new NodeActorHostDescriptor("room", "policy-2", "build-1")]), now);

    var records = await directory.QueryAsync(
        new NodeDirectoryQuery(
            "default",
            actorHostName: "room",
            actorHostPolicyHash: "policy-1",
            state: NodeState.Ready),
        now);

    var record = Assert.Single(records);
    Assert.Equal("node-a", record.NodeId.Value);
}
```

Use the existing helper pattern in the file and add an `actorHosts` parameter to
that helper.

- [ ] **Step 6: Implement actor host query fields**

Modify `NodeDirectoryQuery`:

```csharp
public NodeDirectoryQuery(
    string clusterName,
    string? featureName = null,
    string? actorHostName = null,
    string? actorHostPolicyHash = null,
    NodeState? state = null,
    IReadOnlyDictionary<string, string>? labels = null,
    bool includeExpired = false)
{
    ...
    ActorHostName = actorHostName;
    ActorHostPolicyHash = actorHostPolicyHash;
}

public string? ActorHostName { get; }

public string? ActorHostPolicyHash { get; }
```

Update `InMemoryNodeDirectory.QueryAsync` so a record matches only when it has
an actor host descriptor with the requested actor and, when provided, the
requested policy hash.

- [ ] **Step 7: Run cluster model tests**

Run: `dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --no-restore --filter "NodeDirectoryModelTests|InMemoryNodeDirectoryTests"`

Expected: PASS.

- [ ] **Step 8: Commit cluster descriptor model**

Run:

```powershell
git add src/Lakona.Game.Cluster tests/Lakona.Game.Cluster.Tests
git commit -m "Add actor host descriptors to cluster nodes"
```

### Task 2: Carry Actor Hosts Through RPC And SQL Node Directories

**Files:**
- Modify: `src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryMessages.cs`
- Modify: `src/Lakona.Game.Cluster.Rpc/Nodes/NodeDirectoryRecordConverter.cs`
- Modify: `src/Lakona.Game.Cluster.Sql/SqlNodeDirectorySchema.cs`
- Modify: `src/Lakona.Game.Cluster.Sql/SqlNodeDirectory.cs`
- Test: `tests/Lakona.Game.Cluster.Rpc.Tests/NodeDirectoryClientTests.cs`
- Test: `tests/Lakona.Game.Cluster.Sql.Tests/SqlNodeDirectoryTests.cs`

- [ ] **Step 1: Write RPC roundtrip test**

Add a test that registers a node with one actor host and resolves it through the
RPC directory client:

```csharp
[Fact]
public async Task ResolvePreservesActorHosts()
{
    var now = DateTimeOffset.UtcNow;
    using var fixture = await NodeDirectoryClientFixture.StartAsync();

    await fixture.Client.RegisterAsync(new NodeRegistration(
        "default",
        new NodeId("battle-1"),
        1,
        new Dictionary<string, NodeEndpoint> { ["cluster"] = new("tcp://127.0.0.1:21001") },
        features: [],
        actorHosts: [new NodeActorHostDescriptor("room", "policy-1", "build-1")],
        labels: null,
        NodeState.Ready,
        now.AddSeconds(30)), now);

    var resolved = await fixture.Client.ResolveAsync("default", new NodeId("battle-1"), now);

    var host = Assert.Single(resolved!.ActorHosts);
    Assert.Equal("room", host.Actor);
    Assert.Equal("policy-1", host.PolicyHash);
}
```

Use the fixture/helper names already present in `NodeDirectoryClientTests.cs`.

- [ ] **Step 2: Run RPC test and verify failure**

Run: `dotnet test tests/Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj --no-restore --filter ResolvePreservesActorHosts`

Expected: compile failure because RPC DTOs do not expose actor hosts.

- [ ] **Step 3: Extend RPC DTOs and converters**

Add actor host DTO fields beside the existing feature fields:

```csharp
public sealed record NodeActorHostRecord(
    string Actor,
    string PolicyHash,
    string BuildTag,
    IReadOnlyDictionary<string, string> Metadata);
```

Update `NodeDirectoryRecordConverter` to map both directions:

```csharp
private static NodeActorHostDescriptor ToDomain(NodeActorHostRecord record)
{
    return new NodeActorHostDescriptor(
        record.Actor,
        record.PolicyHash,
        record.BuildTag,
        record.Metadata);
}
```

- [ ] **Step 4: Extend SQL schema**

Add an `actor_hosts_json` column to the SQL node directory schema next to the
existing feature storage. The JSON payload stores an array of actor host records
with `Actor`, `PolicyHash`, `BuildTag`, and `Metadata`.

- [ ] **Step 5: Write SQL persistence test**

Add a test in `SqlNodeDirectoryTests.cs` that registers a node with actor hosts,
resolves it, and queries by actor host name and policy hash.

- [ ] **Step 6: Implement SQL read/write**

Update `SqlNodeDirectory` serialization helpers to write actor hosts JSON on
registration and read it on resolve/query.

- [ ] **Step 7: Run directory transport tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj --no-restore --filter NodeDirectoryClientTests
dotnet test tests/Lakona.Game.Cluster.Sql.Tests/Lakona.Game.Cluster.Sql.Tests.csproj --no-restore --filter SqlNodeDirectoryTests
```

Expected: PASS.

- [ ] **Step 8: Commit node directory transport changes**

Run:

```powershell
git add src/Lakona.Game.Cluster.Rpc src/Lakona.Game.Cluster.Sql tests/Lakona.Game.Cluster.Rpc.Tests tests/Lakona.Game.Cluster.Sql.Tests
git commit -m "Carry actor host descriptors through node directories"
```

### Task 3: Bind And Validate Actor Host Configuration

**Files:**
- Modify: `src/Lakona.Game.Server/Configuration/LakonaGameRuntimeOptions.cs`
- Create: `src/Lakona.Game.Server/Configuration/LakonaGameStartupActorOptions.cs`
- Modify: `tests/Lakona.Game.Server.Tests/Configuration/LakonaGameRuntimeOptionsTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`

- [ ] **Step 1: Write runtime option binding tests**

Add tests:

```csharp
[Fact]
public void FromConfigurationBindsActorHosts()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lakona:ActorHosts:0"] = "room",
            ["Lakona:ActorHosts:1"] = "user"
        })
        .Build();

    var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

    Assert.Equal(["room", "user"], options.ActorHosts);
}

[Fact]
public void FromConfigurationTreatsOmittedActorHostsAsEmpty()
{
    var options = LakonaGameRuntimeOptions.FromConfiguration(new ConfigurationBuilder().Build());

    Assert.Empty(options.ActorHosts);
}
```

- [ ] **Step 2: Run configuration tests and verify failure**

Run: `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter LakonaGameRuntimeOptionsTests`

Expected: compile failure because `ActorHosts` is missing.

- [ ] **Step 3: Implement option binding**

Add these properties to `LakonaGameRuntimeOptions`:

```csharp
public IReadOnlyList<string> ActorHosts { get; init; } = [];

public IReadOnlyList<LakonaGameStartupActorOptions> StartupActors { get; init; } = [];
```

Create `LakonaGameStartupActorOptions`:

```csharp
public sealed class LakonaGameStartupActorOptions
{
    public string Name { get; init; } = "";

    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
```

Bind `StartupActors` from either string arrays or object arrays. A string entry
maps to `{ Name = value }`.

- [ ] **Step 4: Add validation tests**

In `LakonaGameRuntimeValidatorTests.cs`, add tests that `Lakona:Feature` emits
an error and duplicate `ActorHosts` / `StartupActors` emit errors.

- [ ] **Step 5: Implement validation**

Update guardrail validation to reject:

```txt
Lakona:Feature
duplicate Lakona:ActorHosts entries
duplicate Lakona:StartupActors names
blank actor host names
blank startup actor names
```

- [ ] **Step 6: Run server configuration tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "LakonaGameRuntimeOptionsTests|LakonaGameRuntimeValidatorTests"
```

Expected: PASS.

- [ ] **Step 7: Commit configuration binding**

Run:

```powershell
git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests
git commit -m "Add actor host runtime configuration"
```

### Task 4: Publish Actor Host Descriptors From Server Registration

**Files:**
- Create: `src/Lakona.Game.Server/Actors/ActorHostDescriptorCatalog.cs`
- Create: `src/Lakona.Game.Server/Actors/ActorHostDescriptor.cs`
- Modify: `src/Lakona.Game.Server/Features/LakonaGameClusterRegistrationHostedService.cs`
- Test: `tests/Lakona.Game.Server.Tests/Features/LakonaGameClusterRegistrationHostedServiceTests.cs`

- [ ] **Step 1: Write registration test**

Add a test that builds a service provider with runtime options containing
`ActorHosts = ["room"]` and a catalog entry for `room`, then verifies node
registration includes one actor host descriptor and no user Feature descriptor.

```csharp
[Fact]
public async Task RegistrationPublishesConfiguredActorHosts()
{
    var catalog = new ActorHostDescriptorCatalog([
        new ActorHostDescriptor("room", "policy-room", "build-test")
    ]);
    var services = CreateServices(new LakonaGameRuntimeOptions
    {
        Node = new LakonaGameNodeOptions { Id = "battle-1" },
        ActorHosts = ["room"],
        Cluster = LakonaGameClusterOptions.Defaults()
    });
    services.AddSingleton(catalog);

    var registration = await StartAndCaptureRegistrationAsync(services);

    var host = Assert.Single(registration.ActorHosts);
    Assert.Equal("room", host.Actor);
    Assert.Equal("policy-room", host.PolicyHash);
}
```

Use the existing fixture style in `LakonaGameClusterRegistrationHostedServiceTests.cs`.

- [ ] **Step 2: Run the focused registration test and verify failure**

Run: `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter RegistrationPublishesConfiguredActorHosts`

Expected: compile failure because the catalog types and registration field do
not exist.

- [ ] **Step 3: Add catalog types**

Create:

```csharp
public sealed record ActorHostDescriptor(
    string Actor,
    string PolicyHash,
    string BuildTag,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed class ActorHostDescriptorCatalog
{
    private readonly Dictionary<string, ActorHostDescriptor> _byActor;

    public ActorHostDescriptorCatalog(IEnumerable<ActorHostDescriptor> descriptors)
    {
        _byActor = descriptors.ToDictionary(static descriptor => descriptor.Actor, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string actor, out ActorHostDescriptor descriptor)
    {
        return _byActor.TryGetValue(actor, out descriptor!);
    }
}
```

- [ ] **Step 4: Update registration service**

Modify cluster registration to:

1. Read `LakonaGameRuntimeOptions.ActorHosts`.
2. Resolve each actor host name from `ActorHostDescriptorCatalog`.
3. Fail startup on an unknown actor host name.
4. Publish `NodeActorHostDescriptor` values on `NodeRegistration`.

- [ ] **Step 5: Run focused registration tests**

Run: `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter LakonaGameClusterRegistrationHostedServiceTests`

Expected: PASS after existing Feature registration assertions are rewritten to
assert actor host registration and absence of user Feature descriptors.

- [ ] **Step 6: Commit actor host registration**

Run:

```powershell
git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests
git commit -m "Publish actor host descriptors during registration"
```

### Task 5: Add Hotfix Startup And Placement Registration Abstractions

**Files:**
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorHostBuilder.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartupPlan.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartupDeclaration.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorPlacementContext.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorPlacementDeclaration.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorHostCandidate.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/ActorHostBuilderTests.cs`

- [ ] **Step 1: Write builder tests**

Create `tests/Lakona.Game.Server.Hotfix.Tests/ActorHostBuilderTests.cs`:

```csharp
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class ActorHostBuilderTests
{
    [Fact]
    public void RegisterStartupRejectsBlankName()
    {
        var builder = new ActorHostBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.RegisterStartup(" ", static _ => ActorStartupPlan.Empty));
    }

    [Fact]
    public void RegisterPlacementRejectsDuplicateActor()
    {
        var builder = new ActorHostBuilder();
        builder.RegisterPlacement<TestActor, ActorId>(static context => context.Candidates[0]);

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterPlacement<TestActor, ActorId>(static context => context.Candidates[0]));
    }

    private sealed class TestActor : IActor
    {
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter ActorHostBuilderTests`

Expected: compile failure because actor host builder abstractions are missing.

- [ ] **Step 3: Implement abstractions**

Create `ActorHostBuilder` with in-memory collections and validation:

```csharp
public sealed class ActorHostBuilder
{
    private readonly List<ActorStartupDeclaration> _startups = [];
    private readonly List<ActorPlacementDeclaration> _placements = [];
    private readonly HashSet<string> _startupNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Type> _placementActors = [];

    public IReadOnlyList<ActorStartupDeclaration> Startups => _startups;

    public IReadOnlyList<ActorPlacementDeclaration> Placements => _placements;

    public void RegisterStartup(string name, Func<ActorStartupContext, ActorStartupPlan> createPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(createPlan);
        if (!_startupNames.Add(name))
        {
            throw new InvalidOperationException($"Actor startup '{name}' is already registered.");
        }

        _startups.Add(new ActorStartupDeclaration(name, createPlan));
    }

    public void RegisterPlacement<TActor, TKey>(
        Func<ActorPlacementContext<TKey>, ActorHostCandidate> selector)
        where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (!_placementActors.Add(typeof(TActor)))
        {
            throw new InvalidOperationException($"Actor placement for '{typeof(TActor).FullName}' is already registered.");
        }

        _placements.Add(ActorPlacementDeclaration.Create<TActor, TKey>(selector));
    }
}
```

Create minimal record types referenced by the builder. Use `ActorStartupPlan`
with `Empty`, `Create<TActor>(ActorId)`, and `CreateMany<TActor>(IEnumerable<ActorId>)`.

- [ ] **Step 4: Run builder tests**

Run: `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter ActorHostBuilderTests`

Expected: PASS.

- [ ] **Step 5: Commit abstractions**

Run:

```powershell
git add src/Lakona.Game.Server.Hotfix.Abstractions tests/Lakona.Game.Server.Hotfix.Tests
git commit -m "Add actor host startup abstractions"
```

### Task 6: Scan HotfixStartup Instead Of Feature Descriptors

**Files:**
- Modify: `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanner.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanResult.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixBehaviorScannerTests.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixFeatureScannerTests.cs`

- [ ] **Step 1: Write scanner test for `HotfixStartup`**

Add a test to `HotfixBehaviorScannerTests.cs`:

```csharp
[Fact]
public void ScannerReadsHotfixStartupActorRegistrations()
{
    var result = HotfixBehaviorScanner.Scan(typeof(StartupScanFixture).Assembly, [
        typeof(StartupScanFixture.HotfixStartup)
    ]);

    Assert.Contains(result.ActorStartups, startup => startup.Name == "matchmaking");
    Assert.Contains(result.ActorPlacements, placement => placement.ActorType == typeof(StartupScanFixture.RoomActor));
}

private static class StartupScanFixture
{
    public sealed class RoomActor : IActor
    {
    }

    public static class HotfixStartup
    {
        public static void ConfigureActors(ActorHostBuilder actors)
        {
            actors.RegisterStartup(
                "matchmaking",
                static _ => ActorStartupPlan.Create<RoomActor>(ActorId.From("default")));
            actors.RegisterPlacement<RoomActor, ActorId>(
                static context => context.Candidates[0]);
        }
    }
}
```

- [ ] **Step 2: Run scanner test and verify failure**

Run: `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter ScannerReadsHotfixStartupActorRegistrations`

Expected: compile failure because scan result does not expose actor startups and
placements.

- [ ] **Step 3: Extend scan result**

Add these properties to `HotfixBehaviorScanResult`:

```csharp
public IReadOnlyList<ActorStartupDeclaration> ActorStartups { get; }

public IReadOnlyList<ActorPlacementDeclaration> ActorPlacements { get; }
```

- [ ] **Step 4: Invoke `ConfigureActors` during scanning**

In `HotfixBehaviorScanner`, find public static classes named `HotfixStartup`.
If a class declares `public static void ConfigureActors(ActorHostBuilder actors)`,
create a builder, invoke the method, and append builder declarations to the scan
result. Reject non-static overloads and wrong signatures with diagnostics.

- [ ] **Step 5: Keep `ConfigureServices` support**

Scan `public static void ConfigureServices(IServiceCollection services)` on
`HotfixStartup` and append the generated services to the existing hotfix
service declaration list. Do not construct feature classes.

- [ ] **Step 6: Convert feature scanner tests**

Change `HotfixFeatureScannerTests` so old feature descriptor shapes are either
deleted or moved to tests that assert old Feature authoring is rejected. The
accepted path must be `HotfixStartup`.

- [ ] **Step 7: Run hotfix scanner tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "HotfixBehaviorScannerTests|HotfixFeatureScannerTests"
```

Expected: PASS.

- [ ] **Step 8: Commit hotfix startup scanner**

Run:

```powershell
git add src/Lakona.Game.Server.Hotfix tests/Lakona.Game.Server.Hotfix.Tests
git commit -m "Scan hotfix actor startup registrations"
```

### Task 7: Add Actor Lifecycle Attributes And Dispatch

**Files:**
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartAttribute.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStopAttribute.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStartCall.cs`
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/Actors/ActorStopCall.cs`
- Create: `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixActorLifecycleDescriptor.cs`
- Create: `src/Lakona.Game.Server.Hotfix/Runtime/HotfixActorLifecycleInvoker.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanner.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixDispatchTable.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixBehaviorScannerTests.cs`
- Test: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs`

- [ ] **Step 1: Write lifecycle scan test**

Add:

```csharp
[Fact]
public void ScannerFindsActorStartAndStopMethods()
{
    var result = HotfixBehaviorScanner.Scan(typeof(LifecycleFixture.RoomBehavior).Assembly, [
        typeof(LifecycleFixture.RoomBehavior)
    ]);

    var lifecycle = Assert.Single(result.ActorLifecycles);
    Assert.Equal(typeof(LifecycleFixture.RoomActor), lifecycle.ActorType);
    Assert.Equal(nameof(LifecycleFixture.RoomBehavior.StartAsync), lifecycle.StartMethodName);
    Assert.Equal(nameof(LifecycleFixture.RoomBehavior.StopAsync), lifecycle.StopMethodName);
}
```

Define a nested `RoomActor` and `[HotfixBehaviorOf(typeof(RoomActor))]`
behavior class with `[ActorStart]` and `[ActorStop]` methods.

- [ ] **Step 2: Run lifecycle scan test and verify failure**

Run: `dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter ScannerFindsActorStartAndStopMethods`

Expected: compile failure because attributes and lifecycle result are missing.

- [ ] **Step 3: Add lifecycle abstractions**

Create simple sealed method attributes:

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ActorStartAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ActorStopAttribute : Attribute
{
}
```

Create call objects carrying `IServiceProvider`, `CancellationToken`, and actor
id. The stop call should expose `CancellationToken CleanupCancellationToken =>
CancellationToken.None` so sample code has an explicit noncancelable cleanup
token.

- [ ] **Step 4: Scan lifecycle methods**

Extend `HotfixBehaviorScanner.ScanBehaviorType` so `[ActorStart]` and
`[ActorStop]` methods:

```txt
must be public static
must return ValueTask
must have first parameter equal to the behavior actor type
must have second parameter ActorStartCall or ActorStopCall
must be unique per actor type
```

Add diagnostics for duplicate or invalid lifecycle methods.

- [ ] **Step 5: Add dispatch table lifecycle lookup**

Add `TryResolveActorLifecycle(Type actorType, out HotfixActorLifecycleDescriptor descriptor)` to `HotfixDispatchTable`.

- [ ] **Step 6: Add lifecycle invoker tests**

Write tests that create a fixture behavior method, resolve the descriptor, and
invoke it through `HotfixActorLifecycleInvoker`.

- [ ] **Step 7: Run hotfix lifecycle tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "HotfixBehaviorScannerTests|HotfixDispatchTests"
```

Expected: PASS.

- [ ] **Step 8: Commit lifecycle dispatch**

Run:

```powershell
git add src/Lakona.Game.Server.Hotfix.Abstractions src/Lakona.Game.Server.Hotfix tests/Lakona.Game.Server.Hotfix.Tests
git commit -m "Add hotfix actor lifecycle dispatch"
```

### Task 8: Invoke Actor Lifecycle From Actor Hosting

**Files:**
- Modify: `src/Lakona.Game.Server/Actors/IActorHostingRuntime.cs`
- Modify: `src/Lakona.Game.Server/Actors/ActorHosting.cs`
- Modify: `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`
- Create: `src/Lakona.Game.Server/Actors/IActorLifecycleDispatcher.cs`
- Test: `tests/Lakona.Game.Server.Tests/Actors/ActorHostingTests.cs`

- [ ] **Step 1: Write actor hosting lifecycle tests**

Add tests:

```csharp
[Fact]
public async Task CreateAsyncRunsActorStartHook()
{
    var dispatcher = new RecordingActorLifecycleDispatcher();
    var host = CreateActorHosting(lifecycleDispatcher: dispatcher);

    await host.CreateAsync<TestActor>(ActorId.From("a"));

    Assert.Equal([("start", "a")], dispatcher.Events);
}

[Fact]
public async Task DestroyAsyncRunsActorStopHook()
{
    var dispatcher = new RecordingActorLifecycleDispatcher();
    var host = CreateActorHosting(lifecycleDispatcher: dispatcher);
    await host.CreateAsync<TestActor>(ActorId.From("a"));

    await host.DestroyAsync<TestActor>(ActorId.From("a"), CancellationToken.None);

    Assert.Contains(("stop", "a"), dispatcher.Events);
}
```

Use helper patterns already present in `ActorHostingTests.cs`.

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter ActorHostingTests`

Expected: compile failure because lifecycle dispatcher does not exist.

- [ ] **Step 3: Add lifecycle dispatcher interface**

Create:

```csharp
public interface IActorLifecycleDispatcher
{
    ValueTask StartAsync(Type actorType, ActorId actorId, object actor, CancellationToken cancellationToken);

    ValueTask StopAsync(Type actorType, ActorId actorId, object actor, CancellationToken cancellationToken);
}
```

Provide a no-op default implementation for tests and hosts without hotfix.

- [ ] **Step 4: Wire dispatcher into actor runtime creation and destroy**

After local actor creation succeeds and before route registration is considered
ready for traffic, call `StartAsync`. Before actor destruction completes, call
`StopAsync`. If start fails, destroy the local actor and unregister the route.
If stop fails, surface an `ActorHostingStopException` through the existing stop
path.

- [ ] **Step 5: Run actor hosting tests**

Run: `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter ActorHostingTests`

Expected: PASS.

- [ ] **Step 6: Commit actor lifecycle hosting**

Run:

```powershell
git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests
git commit -m "Run actor lifecycle hooks from hosting"
```

### Task 9: Add Actor Placement Service And Remote Create / Ensure

**Files:**
- Create: `src/Lakona.Game.Server/Actors/IActorPlacementService.cs`
- Create: `src/Lakona.Game.Server/Actors/ActorPlacementService.cs`
- Create: `src/Lakona.Game.Server/Actors/ActorPlacementResult.cs`
- Create: `src/Lakona.Game.Server/Actors/IActorHostClient.cs`
- Create: `src/Lakona.Game.Server/Actors/ActorHostCreateRequest.cs`
- Create: `src/Lakona.Game.Server/Actors/ActorHostCreateReply.cs`
- Modify: `src/Lakona.Game.Server/Actors/HotfixActorClusterHandler.cs`
- Test: `tests/Lakona.Game.Server.Tests/RemoteActorGatewayTests.cs`
- Test: `tests/Lakona.Game.Server.Tests/ActorDirectoryTests.cs`

- [ ] **Step 1: Write placement service test for existing route**

Create a focused test:

```csharp
[Fact]
public async Task PlaceAsyncUsesExistingRouteBeforeSelectingCandidate()
{
    var selector = new RecordingSelector();
    var directory = new FakeActorDirectory(existingOwner: new NodeId("battle-1"));
    var service = CreatePlacementService(directory, selector);

    var result = await service.PlaceAsync<RoomActor, RoomId>(
        new RoomId("room-1"),
        createMode: ActorPlacementCreateMode.Ensure,
        TestContext.Current.CancellationToken);

    Assert.Equal(new NodeId("battle-1"), result.Owner);
    Assert.False(selector.WasCalled);
}
```

- [ ] **Step 2: Run placement test and verify failure**

Run: `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter PlaceAsyncUsesExistingRouteBeforeSelectingCandidate`

Expected: compile failure because placement service does not exist.

- [ ] **Step 3: Implement placement service skeleton**

Create `IActorPlacementService`:

```csharp
public interface IActorPlacementService
{
    ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;
}
```

Implementation order:

1. Convert key to `ActorId` through generated metadata.
2. Resolve route directory first.
3. Discover actor host candidates.
4. Sort candidates by `NodeId`.
5. Invoke selector.
6. Send create / ensure request through `IActorHostClient`.

- [ ] **Step 4: Add candidate selection tests**

Add tests for no candidates, selector returns outside candidate set, and policy
hash mismatch. Each test should assert a structured exception type such as
`ActorPlacementException`.

- [ ] **Step 5: Add internal host client contract**

Define request/reply DTOs:

```csharp
public sealed record ActorHostCreateRequest(
    string Actor,
    string ActorId,
    string Mode,
    string BuildTag);

public sealed record ActorHostCreateReply(
    bool Succeeded,
    string? OwnerNode,
    string Message);
```

Add a server-side handler that maps requests to `ActorHosting.CreateAsync` or
`ActorHosting.EnsureAsync`.

- [ ] **Step 6: Add conflict handling test**

Test that if target create reports an existing owner from the actor directory,
placement returns the existing owner instead of retrying selection.

- [ ] **Step 7: Run placement tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "RemoteActorGatewayTests|ActorDirectoryTests|ActorPlacement"
```

Expected: PASS after adding or adjusting filters for the new tests.

- [ ] **Step 8: Commit actor placement service**

Run:

```powershell
git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests
git commit -m "Add actor placement service"
```

### Task 10: Generate `Place(id)` Actor Accessors

**Files:**
- Modify: `src/Lakona.Game.Server.Generators/TypedActorGenerator.cs`
- Modify: `src/Lakona.Game.Server.Generators/TypedActorGeneratorDiagnostics.cs`
- Test: `tests/Lakona.Game.Server.Generators.Tests/TypedActorGeneratorTests.cs`

- [ ] **Step 1: Write generator test**

Add a test that a registered placement actor gets a generated `Place` accessor:

```csharp
[Fact]
public void GeneratesPlaceAccessorForPlacedActor()
{
    var result = RunGenerator("""
        using Lakona.Game.Server.Actors;

        [ActorName("room")]
        public sealed class RoomActor : IActor
        {
        }
        """);

    Assert.Contains("public RoomActorPlacementRef Place(RoomId id)", result.GeneratedSource);
    Assert.Contains("IActorPlacementService", result.GeneratedSource);
}
```

Use the existing test harness and replace `RoomId` with the ID type shape used
by current actor collection generation.

- [ ] **Step 2: Run generator test and verify failure**

Run: `dotnet test tests/Lakona.Game.Server.Generators.Tests/Lakona.Game.Server.Generators.Tests.csproj --no-restore --filter GeneratesPlaceAccessorForPlacedActor`

Expected: assertion failure because `Place` is not generated.

- [ ] **Step 3: Generate placement refs**

Add generated types:

```csharp
public readonly struct RoomActorPlacementRef
{
    private readonly IActorPlacementService _placement;
    private readonly RoomId _id;

    public ValueTask CreateAsync(RoomCreateRequest request, CancellationToken cancellationToken = default)
    {
        return _placement.PlaceAsync<RoomActor, RoomId>(
            _id,
            ActorPlacementCreateMode.Create,
            cancellationToken);
    }
}
```

Match the current generator's naming, accessibility, and async conventions.

- [ ] **Step 4: Add diagnostic for missing placement selector**

Generate `Place(id)` only from hotfix scan metadata that proves a placement
selector exists for the actor. When a source call tries to use `Place(id)` for an
actor without a registered selector, the generated actor collection simply lacks
that member and the C# compiler reports the missing member.

- [ ] **Step 5: Run generator tests**

Run: `dotnet test tests/Lakona.Game.Server.Generators.Tests/Lakona.Game.Server.Generators.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit generated placement accessors**

Run:

```powershell
git add src/Lakona.Game.Server.Generators tests/Lakona.Game.Server.Generators.Tests
git commit -m "Generate actor placement accessors"
```

### Task 11: Migrate Tool Templates

**Files:**
- Modify: `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`
- Modify: `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`
- Modify: `tests/Lakona.Tool.Tests/Rendering/HotfixRendererTests.cs`
- Modify: `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs`
- Modify: `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`

- [ ] **Step 1: Write renderer tests rejecting Feature output**

Add assertions:

```csharp
Assert.DoesNotContain("HotfixFeature", renderedHotfix, StringComparison.Ordinal);
Assert.DoesNotContain("HotfixGameFeature", renderedHotfix, StringComparison.Ordinal);
Assert.Contains("public static class HotfixStartup", renderedHotfix, StringComparison.Ordinal);
Assert.Contains("ConfigureActors(ActorHostBuilder actors)", renderedHotfix, StringComparison.Ordinal);
```

- [ ] **Step 2: Run renderer tests and verify failure**

Run: `dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-restore --filter "HotfixRendererTests|ServerAppRendererTests|ToolArchitectureScanTests"`

Expected: tests fail because templates still render Feature.

- [ ] **Step 3: Update hotfix template**

Render `HotfixStartup` with:

```csharp
public static class HotfixStartup
{
    public static void ConfigureServices(IServiceCollection services)
    {
    }

    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup(
            "chat-room",
            static _ => ActorStartupPlan.Create<ChatRoomActor>(ActorId.From("chat-room/global")));
    }
}
```

- [ ] **Step 4: Update generated appsettings**

Emit:

```json
"ActorHosts": [ "chat-room" ],
"StartupActors": [ "chat-room" ]
```

Remove `Lakona:Feature`.

- [ ] **Step 5: Run tool tests**

Run: `dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit template migration**

Run:

```powershell
git add src/Lakona.Tool tests/Lakona.Tool.Tests
git commit -m "Migrate templates to actor startup"
```

### Task 12: Migrate Agar Sample From Feature Commands

**Files:**
- Delete after migration: `samples/Game.Unity.Agar/Server/Hotfix/Features/*.cs`
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/State/**`
- Modify: `samples/Game.Unity.Agar/docker-compose.yml`
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/*.cs`

- [ ] **Step 1: Write source scan test**

In `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/ZeroTemplateServerShapeTests.cs`,
add:

```csharp
[Fact]
public void AgarHotfixDoesNotUseFeatureAuthoring()
{
    var files = Directory.GetFiles(
        Path.Combine(SampleRoot, "Server", "Hotfix"),
        "*.cs",
        SearchOption.AllDirectories);

    var combined = string.Join('\n', files.Select(File.ReadAllText));

    Assert.DoesNotContain("HotfixFeature", combined, StringComparison.Ordinal);
    Assert.DoesNotContain("HotfixGameFeature", combined, StringComparison.Ordinal);
    Assert.DoesNotContain("IFeatureCommandClient", combined, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run Agar business tests and verify failure**

Run: `dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore --filter AgarHotfixDoesNotUseFeatureAuthoring`

Expected: failure because Agar still uses Feature classes and feature command
client.

- [ ] **Step 3: Add Agar `HotfixStartup`**

Create a new hotfix startup file that registers services, startup actors, and
placement selectors:

```csharp
public static class HotfixStartup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<MatchmakingNotifier>();
        services.TryAddSingleton<RoomNotifier>();
    }

    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup("matchmaking", static _ =>
            ActorStartupPlan.Create<MatchmakingActor>(ActorId.From("default")));
        actors.RegisterStartup("leaderboard", static _ =>
            ActorStartupPlan.Create<LeaderboardActor>(ActorId.From("global")));
        actors.RegisterPlacement<UserActor, UserId>(static context =>
            SelectStableHash(context.Candidates, context.Key.Value));
        actors.RegisterPlacement<RoomActor, RoomId>(static context =>
            SelectStableHash(context.Candidates, context.Key.Value));
    }
}
```

- [ ] **Step 4: Move timer startup into actor lifecycle**

Move matchmaking timer start/stop from `MatchmakingFeature` into
`MatchmakingBehavior` methods marked `[ActorStart]` and `[ActorStop]`.

- [ ] **Step 5: Replace state-store feature command**

Replace `IFeatureCommandClient.SendToNodeAsync<CreateUserActorRequest, CreateActorReply>`
with:

```csharp
await _users.Place(new UserId(account))
    .EnsureAsync(cancellationToken)
    .ConfigureAwait(false);
```

Use the generated accessor names from Task 10.

- [ ] **Step 6: Replace battle-runtime room allocation command**

Replace feature command room allocation with:

```csharp
await rooms.Place(new RoomId(request.RoomId))
    .CreateAsync(createRoomRequest, cancellationToken)
    .ConfigureAwait(false);
```

Move room creation idempotency into `RoomBehavior.CreateAsync`.

- [ ] **Step 7: Update Docker Compose configuration**

Replace:

```yaml
Lakona__Feature: '["state-store","matchmaking","leaderboard"]'
```

with:

```yaml
Lakona__ActorHosts: '["user","matchmaking","leaderboard"]'
Lakona__StartupActors: '["matchmaking","leaderboard"]'
```

Replace battle node feature with:

```yaml
Lakona__ActorHosts: '["room"]'
```

Gateway nodes should use:

```yaml
Lakona__ActorHosts: '[]'
Lakona__StartupActors: '[]'
```

- [ ] **Step 8: Run Agar tests**

Run: `dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 9: Commit Agar migration**

Run:

```powershell
git add samples/Game.Unity.Agar
git commit -m "Migrate Agar sample to actor hosting"
```

### Task 13: Migrate Godot Chat Sample

**Files:**
- Modify: `samples/Game.Godot.Chat/Server/Hotfix/Features/ChatFeature.cs`
- Modify: Godot chat server configuration files discovered by `rg -n "Feature|HotfixFeature" samples/Game.Godot.Chat`
- Modify: tests that scan Godot chat sample shape.

- [ ] **Step 1: Write sample source scan**

Add or update the existing integration scan to assert Godot chat hotfix uses
`HotfixStartup` and not Feature authoring.

- [ ] **Step 2: Run focused tool/sample scan**

Run: `dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-restore --filter GodotChatSample`

Expected: failure because Godot chat still uses `ChatFeature`.

- [ ] **Step 3: Replace `ChatFeature` with `HotfixStartup`**

Create startup registration:

```csharp
public static class HotfixStartup
{
    public static void ConfigureActors(ActorHostBuilder actors)
    {
        actors.RegisterStartup(
            "chat-room",
            static _ => ActorStartupPlan.Create<ChatRoomActor>(ActorId.From("chat-room/global")));
    }
}
```

Move existing timer or lifecycle cleanup into `[ActorStart]` and `[ActorStop]`
methods on chat room behavior if the sample currently uses lifecycle hooks.

- [ ] **Step 4: Update sample configuration**

Use:

```json
"ActorHosts": [ "chat-room" ],
"StartupActors": [ "chat-room" ]
```

- [ ] **Step 5: Run focused tests**

Run: `dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-restore --filter GodotChatSample`

Expected: PASS.

- [ ] **Step 6: Commit Godot chat migration**

Run:

```powershell
git add samples/Game.Godot.Chat tests/Lakona.Tool.Tests
git commit -m "Migrate Godot chat sample to actor startup"
```

### Task 14: Remove Public Feature Authoring APIs

**Files:**
- Delete or internalize: `src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixGameFeature.cs`
- Delete or internalize: `src/Lakona.Game.Server.Hotfix.Abstractions/Attributes/HotfixFeatureAttribute.cs`
- Delete or internalize: `src/Lakona.Game.Server.Hotfix.Abstractions/Features/FeatureCommandAttribute.cs`
- Delete or internalize: `src/Lakona.Game.Server/Features/IFeatureCommandClient.cs`
- Delete or internalize: `src/Lakona.Game.Server/Features/FeatureCommandClient.cs`
- Modify: package README files and docs.
- Test: source scan tests across `src`, `samples`, and `docs`.

- [ ] **Step 1: Add source scan test**

Create or update a test that scans user-facing code and docs:

```csharp
[Fact]
public void UserFacingSourcesDoNotExposeFeatureAuthoring()
{
    var roots = new[] { "src", "samples", "docs" };
    var banned = new[]
    {
        "HotfixFeatureAttribute",
        "HotfixGameFeature",
        "IFeatureCommandClient",
        "Lakona:Feature"
    };

    foreach (var file in EnumerateTextFiles(roots))
    {
        var text = File.ReadAllText(file);
        foreach (var term in banned)
        {
            Assert.DoesNotContain(term, text, StringComparison.Ordinal);
        }
    }
}
```

Exclude archived implementation plans and this migration plan from the scan.

- [ ] **Step 2: Run source scan and verify failure**

Run the test project containing the scan.

Expected: failure while old public Feature APIs and docs remain.

- [ ] **Step 3: Remove old public APIs**

Remove or make internal any old Feature API that is no longer referenced by
samples or templates. Keep only internal compatibility types required by the
runtime during the same commit, and place them under an `Internal` namespace
with no README or generated-code examples.

- [ ] **Step 4: Update docs**

Update:

```txt
docs/cluster.md
docs/configuration.md
docs/hotfix/architecture.md
docs/hotfix/actor-behavior.md
docs/actor.md
docs/tool/default-experience.md
docs/tool/generation-architecture.md
README.md
```

The docs should teach `ActorHosts`, `StartupActors`, `[ActorStart]`,
`[ActorStop]`, and placement selectors. They should not describe Feature as an
application topology concept.

- [ ] **Step 5: Run source scan**

Run the source scan test again.

Expected: PASS.

- [ ] **Step 6: Commit feature removal**

Run:

```powershell
git add src samples docs tests README.md
git commit -m "Remove public feature authoring"
```

### Task 15: Version Bumps And Final Validation

**Files:**
- Modify package `.csproj` files under changed `src/**` packages.
- Modify release version files or template constants consumed by generated
  projects.

- [ ] **Step 1: List changed shippable packages**

Run:

```powershell
git diff --name-only main...HEAD -- src | Select-String '\.cs$|\.csproj$'
```

Expected: list includes every package changed by implementation.

- [ ] **Step 2: Bump package versions**

For every modified shippable package under `src/**`, bump its `.csproj`
`<Version>` property by one patch version. If a bumped package is consumed by
generated templates, update the corresponding template package version constant
or release-version file in the same commit.

- [ ] **Step 3: Run formatting and whitespace checks**

Run:

```powershell
git diff --check
```

Expected: no output.

- [ ] **Step 4: Build solution**

Run:

```powershell
dotnet build Lakona.slnx --no-restore
```

Expected: build succeeds. If restore is required, rerun with the repository's
approved restore pattern and record the reason.

- [ ] **Step 5: Run affected tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Cluster.Tests/Lakona.Game.Cluster.Tests.csproj --no-build
dotnet test tests/Lakona.Game.Cluster.Rpc.Tests/Lakona.Game.Cluster.Rpc.Tests.csproj --no-build
dotnet test tests/Lakona.Game.Cluster.Sql.Tests/Lakona.Game.Cluster.Sql.Tests.csproj --no-build
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-build
dotnet test tests/Lakona.Game.Server.Generators.Tests/Lakona.Game.Server.Generators.Tests.csproj --no-build
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-build
dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-build
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-build
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-build
```

Expected: all listed test projects pass.

- [ ] **Step 6: Run repository consistency checks**

Run:

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
```

Expected: both scripts pass.

- [ ] **Step 7: Run Agar E2E smoke**

Run:

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
```

Expected: script passes and verifies the three-node Agar topology with
`ActorHosts` and `StartupActors`.

- [ ] **Step 8: Commit final cleanup**

Run:

```powershell
git add src tests samples docs README.md
git commit -m "Finalize actor hosting feature removal"
```

## Review Gates

- Architecture review after Task 3 if cluster descriptor or configuration shape
  changed from the design spec.
- Runtime review after Task 9 before generator and sample migration.
- Hotfix lifecycle review after Task 8.
- Generator/template review after Task 11.
- Final integration review after Task 15.

## Validation And Hygiene Checklist

- Public APIs no longer teach Feature authoring.
- Generated starter output contains `ActorHosts` and `StartupActors`.
- Agar no longer uses `IFeatureCommandClient`.
- Godot chat no longer uses `HotfixFeature`.
- Actor lifecycle hooks are covered by scanner, dispatch, and hosting tests.
- Actor placement is covered for existing route, no candidates, policy mismatch,
  selector exception, and duplicate route conflict.
- Node directory transports preserve actor host descriptors.
- Package versions are bumped for every modified shippable package.
- `git diff --check` passes.
- Affected tests and Agar smoke pass.
