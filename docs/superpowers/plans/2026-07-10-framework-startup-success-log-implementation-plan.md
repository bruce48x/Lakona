# Framework Startup Success Log Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Emit `Lakona server started successfully. NodeId={NodeId}.` exactly once, and only after startup actors, lifecycle callbacks, cluster registration, and every enabled framework listener have started successfully.

**Architecture:** Preserve Generic Host lifecycle semantics by making every listener-backed `BackgroundService.StartAsync` wait for a real bind-ready signal. Add an internal RPC acceptor-ready callback so `RpcServersHostedService` can aggregate all client and cluster listeners without expanding public API. An `IHostedLifecycleService` logs the final message from `StartedAsync`, which the host invokes only after every hosted service has started.

**Tech Stack:** C# 14, .NET 10 Generic Host, `Microsoft.Extensions.Logging`, xUnit v3, Lakona RPC transport abstractions.

---

## Scope and File Map

This is a large cross-cutting lifecycle change spanning `Lakona.Rpc.Server` and
`Lakona.Game.Server`. RPC acceptor readiness, listener hosted services, startup
ordering, and final logging remain under one implementation owner.

- `src/Lakona.Rpc.Server/Hosting/RpcServerHost.cs` — notify an internal caller immediately after the acceptor is acquired and owns the listening endpoint.
- `src/Lakona.Rpc.Server/Hosting/RpcServerHostBuilder.cs` — forward the internal acceptor-ready callback without changing public `RunAsync`.
- `src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj` — friend the game-server assembly and bump the patch version.
- `src/Lakona.Game.Server/Hosting/RpcServersHostedService.cs` — aggregate readiness across every configured client/cluster RPC server and make `StartAsync` wait for it.
- `src/Lakona.Game.Server/Health/LakonaHealthHttpHostedService.cs` — make host startup wait until the health socket binds or fails.
- `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminHostedService.cs` — make host startup wait until the local-admin listener binds or fails.
- `src/Lakona.Game.Server/Hosting/LakonaServerStartupHostedService.cs` — write the single final success log from `StartedAsync`.
- `src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs` — register the final lifecycle logger once.
- `src/Lakona.Game.Server/Lakona.Game.Server.csproj` — bump the patch version.
- `tests/Lakona.Rpc.Tests/RpcServerTests.cs` — protect exact acceptor-ready notification timing.
- `tests/Lakona.Game.Server.Tests/Hosting/RpcServersHostedServiceTests.cs` — protect aggregate listener readiness, failure, cancellation, and zero-listener behavior.
- `tests/Lakona.Game.Server.Tests/Health/LakonaHealthHttpHostedServiceTests.cs` — protect health bind-ready and bind-failure behavior.
- `tests/Lakona.Game.Server.Tests/LocalAdmin/LakonaLocalAdminHostedServiceTests.cs` — protect local-admin disabled, bind-ready, and bind-failure behavior.
- `tests/Lakona.Game.Server.Tests/Hosting/LakonaServerStartupHostedServiceTests.cs` — protect final message, structured NodeId, ordering, failure suppression, and registration.
- `docs/cluster.md` and `docs/configuration.md` — document the runtime-ready success boundary.

Do not modify or stage the existing user-owned Agar editor or Docker Compose
changes.

### Task 1: Add an internal RPC acceptor-ready notification

**Files:**
- Modify: `tests/Lakona.Rpc.Tests/RpcServerTests.cs`
- Modify: `src/Lakona.Rpc.Server/Hosting/RpcServerHost.cs`
- Modify: `src/Lakona.Rpc.Server/Hosting/RpcServerHostBuilder.cs`
- Modify: `src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj`

- [ ] **Step 1: Write the failing RPC host timing test**

Add a test named `RunAsync_notifies_listening_only_after_acceptor_is_created`.
Use a delayed acceptor factory and a blocking acceptor so the assertion observes
the running server rather than a server that has already stopped:

```csharp
[Fact]
public async Task RunAsync_notifies_listening_only_after_acceptor_is_created()
{
    var acceptorReady = new TaskCompletionSource<IRpcConnectionAcceptor>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var listening = new TaskCompletionSource<string>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var builder = RpcServerHostBuilder.Create()
        .UseSerializer(new JsonRpcSerializer())
        .ConfigureServices(_ => { })
        .UseAcceptor(ct => new ValueTask<IRpcConnectionAcceptor>(
            acceptorReady.Task.WaitAsync(ct)));

    var run = builder.RunAsync(
        cts.Token,
        address => listening.TrySetResult(address)).AsTask();

    Assert.False(listening.Task.IsCompleted);
    acceptorReady.SetResult(new BlockingConnectionAcceptor("test://ready"));
    Assert.Equal("test://ready", await listening.Task.WaitAsync(cts.Token));

    await cts.CancelAsync();
    await run;
}
```

Add `BlockingConnectionAcceptor`, whose `AcceptAsync` waits indefinitely until
the supplied cancellation token is cancelled and whose `ListenAddress` returns
the constructor value.

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test tests/Lakona.Rpc.Tests/Lakona.Rpc.Tests.csproj --no-restore --filter "FullyQualifiedName~RunAsync_notifies_listening_only_after_acceptor_is_created"
```

Expected: compilation fails because the internal callback overload does not
exist. This is the intended missing-contract failure.

- [ ] **Step 3: Add the minimal internal callback path**

Keep public `RunAsync(CancellationToken)` unchanged. Add these internal overloads:

```csharp
// RpcServerHostBuilder
internal ValueTask RunAsync(
    CancellationToken cancellationToken,
    Action<string> onListening)
{
    ArgumentNullException.ThrowIfNull(onListening);
    return Build().RunAsync(cancellationToken, onListening);
}
```

```csharp
// RpcServerHost
public ValueTask RunAsync(CancellationToken ct = default)
{
    return RunAsync(ct, onListening: null);
}

internal async ValueTask RunAsync(
    CancellationToken ct,
    Action<string>? onListening)
{
    // Preserve the existing method body. Immediately after the acceptor and
    // BoundedConnectionAcceptor are created, before entering AcceptAsync:
    _logger.LogInformation(
        "RPC server listening on {ListenAddress}. Press Ctrl+C to stop.",
        baseAcceptor.ListenAddress);
    onListening?.Invoke(baseAcceptor.ListenAddress);
}
```

Add a second `InternalsVisibleTo` item for `Lakona.Game.Server` in
`Lakona.Rpc.Server.csproj`; retain the existing test friendship.

- [ ] **Step 4: Run focused and project tests to verify GREEN**

Run:

```powershell
dotnet test tests/Lakona.Rpc.Tests/Lakona.Rpc.Tests.csproj --no-restore --filter "FullyQualifiedName~RunAsync_notifies_listening_only_after_acceptor_is_created"
dotnet test tests/Lakona.Rpc.Tests/Lakona.Rpc.Tests.csproj --no-restore
```

Expected: both commands pass; existing public server lifetime and logging tests
remain unchanged.

- [ ] **Step 5: Commit the RPC readiness hook**

```powershell
git add src/Lakona.Rpc.Server/Hosting/RpcServerHost.cs src/Lakona.Rpc.Server/Hosting/RpcServerHostBuilder.cs src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj tests/Lakona.Rpc.Tests/RpcServerTests.cs
git commit -m "Expose internal RPC listener readiness"
```

### Task 2: Make aggregate RPC hosted-service startup truthful

**Files:**
- Create: `tests/Lakona.Game.Server.Tests/Hosting/RpcServersHostedServiceTests.cs`
- Modify: `src/Lakona.Game.Server/Hosting/RpcServersHostedService.cs`

- [ ] **Step 1: Write failing aggregate readiness tests**

Create focused tests with fake `IRpcServerConfigurator` implementations. Each
configurator uses `JsonRpcSerializer`, calls `ConfigureServices(_ => { })`, and
provides either a delayed `BlockingConnectionAcceptor` or a throwing acceptor
factory.

Protect these cases as separate tests:

```csharp
[Fact]
public async Task StartAsync_waits_until_every_rpc_acceptor_is_listening()
{
    var first = new DelayedConfigurator("first");
    var second = new DelayedConfigurator("second");
    await using var services = new ServiceCollection()
        .AddSingleton(new LakonaGameRuntimeOptions())
        .BuildServiceProvider();
    var hosted = new RpcServersHostedService([first, second], services);

    var start = hosted.StartAsync(TestContext.Current.CancellationToken);
    first.Release("test://first");
    await Task.Yield();
    Assert.False(start.IsCompleted);

    second.Release("test://second");
    await start;
    await hosted.StopAsync(TestContext.Current.CancellationToken);
}
```

Also add:

- `StartAsync_propagates_acceptor_creation_failure`;
- `StartAsync_completes_when_no_rpc_configurators_exist`;
- `StartAsync_cancels_without_hanging_before_listener_readiness`.

- [ ] **Step 2: Run the tests and verify RED**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RpcServersHostedServiceTests"
```

Expected: the first test observes `StartAsync` completing before either acceptor
is released, proving current host startup is not tied to listener readiness.

- [ ] **Step 3: Add minimal aggregate readiness**

Give `RpcServersHostedService` one run-scoped completion source and override
`StartAsync`:

```csharp
private readonly TaskCompletionSource _listening = new(
    TaskCreationOptions.RunContinuationsAsynchronously);

public override async Task StartAsync(CancellationToken cancellationToken)
{
    await base.StartAsync(cancellationToken).ConfigureAwait(false);
    await _listening.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
}
```

In `ExecuteAsync`, complete `_listening` immediately for zero configurators.
For configured servers, pass a callback into each internal builder `RunAsync`
overload and decrement a shared remaining count. Complete `_listening` only when
the count reaches zero. If any server fails before aggregate readiness, fault
`_listening` with the original exception and rethrow. If shutdown wins before
readiness, cancel `_listening` with the stopping token.

The callback used by `RunServerAsync` is:

```csharp
_ =>
{
    if (Interlocked.Decrement(ref remaining) == 0)
    {
        _listening.TrySetResult();
    }
}
```

- [ ] **Step 4: Run focused tests to verify GREEN**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~RpcServersHostedServiceTests"
```

Expected: all four cases pass without timeout or background-service warnings.

- [ ] **Step 5: Commit aggregate readiness**

```powershell
git add src/Lakona.Game.Server/Hosting/RpcServersHostedService.cs tests/Lakona.Game.Server.Tests/Hosting/RpcServersHostedServiceTests.cs
git commit -m "Wait for every RPC listener during startup"
```

### Task 3: Make health and local-admin startup wait for binding

**Files:**
- Modify: `tests/Lakona.Game.Server.Tests/Health/LakonaHealthHttpHostedServiceTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/LocalAdmin/LakonaLocalAdminHostedServiceTests.cs`
- Modify: `src/Lakona.Game.Server/Health/LakonaHealthHttpHostedService.cs`
- Modify: `src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminHostedService.cs`

- [ ] **Step 1: Write failing health readiness tests**

Change the existing enabled-health test so it performs one HTTP request
immediately after `await service.StartAsync(...)`; remove `WaitForBodyAsync`.
Add an occupied-port test:

```csharp
[Fact]
public async Task StartAsync_propagates_health_listener_bind_failure()
{
    var blocker = new TcpListener(IPAddress.Loopback, 0);
    blocker.Start();
    try
    {
        var port = ((IPEndPoint)blocker.LocalEndpoint).Port;
        var service = CreateHealthService(enabled: true, port: port);

        await Assert.ThrowsAnyAsync<SocketException>(() =>
            service.StartAsync(TestContext.Current.CancellationToken));
    }
    finally
    {
        blocker.Stop();
    }
}
```

Add `StartAsync_completes_when_health_listener_is_disabled`.

- [ ] **Step 2: Write failing local-admin readiness tests**

Add a helper that creates `LakonaLocalAdminHostedService` with an empty
`LakonaLocalAdminRouter`. Protect three independent cases. The enabled case
uses an OS-supported loopback prefix and a dynamically selected free port:

```csharp
[Fact]
public async Task StartAsync_returns_after_local_admin_listener_is_bound()
{
    var port = GetFreePort();
    var service = CreateLocalAdminService(
        enabled: true,
        host: "127.0.0.1",
        port: port);

    await service.StartAsync(TestContext.Current.CancellationToken);
    try
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync(
            $"http://127.0.0.1:{port}/missing",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    finally
    {
        await service.StopAsync(TestContext.Current.CancellationToken);
    }
}

[Fact]
public async Task StartAsync_completes_when_local_admin_is_disabled()
{
    var service = CreateLocalAdminService(
        enabled: false,
        host: "127.0.0.1",
        port: GetFreePort());

    await service.StartAsync(TestContext.Current.CancellationToken);
    await service.StopAsync(TestContext.Current.CancellationToken);
}

[Fact]
public async Task StartAsync_propagates_invalid_local_admin_prefix()
{
    var service = CreateLocalAdminService(
        enabled: true,
        host: "bad host",
        port: 20090);

    await Assert.ThrowsAsync<ArgumentException>(() =>
        service.StartAsync(TestContext.Current.CancellationToken));
}
```

If the platform rejects valid enabled `HttpListener` startup for environmental
URL ACL reasons, the enabled test must surface that real failure rather than
skip it.

- [ ] **Step 3: Run both test classes and verify RED**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~LakonaHealthHttpHostedServiceTests|FullyQualifiedName~LakonaLocalAdminHostedServiceTests"
```

Expected: immediate requests or bind-failure assertions fail because current
`BackgroundService.StartAsync` returns before `ExecuteAsync` binds.

- [ ] **Step 4: Add the minimal bind-ready gates**

In each listener service add:

```csharp
private readonly TaskCompletionSource _listening = new(
    TaskCreationOptions.RunContinuationsAsynchronously);

internal Task Listening => _listening.Task;

public override async Task StartAsync(CancellationToken cancellationToken)
{
    await base.StartAsync(cancellationToken).ConfigureAwait(false);
    await _listening.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
}
```

Wrap the beginning of `ExecuteAsync` so disabled endpoints call
`_listening.TrySetResult()`, while enabled endpoints call
`_listening.TrySetResult()` immediately after the existing component-level
listening log. Bind failures call `_listening.TrySetException(exception)` before
rethrowing. In `finally`, if the signal is still incomplete and shutdown was requested, call
`_listening.TrySetCanceled(stoppingToken)`.

Do not swallow exceptions and do not remove either existing component-level
`... endpoint listening ...` log.

- [ ] **Step 5: Run focused tests to verify GREEN**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~LakonaHealthHttpHostedServiceTests|FullyQualifiedName~LakonaLocalAdminHostedServiceTests"
```

Expected: enabled startup returns only after the endpoint can be contacted,
disabled startup returns, and bind failures propagate directly.

- [ ] **Step 6: Commit listener gates**

```powershell
git add src/Lakona.Game.Server/Health/LakonaHealthHttpHostedService.cs src/Lakona.Game.Server/LocalAdmin/LakonaLocalAdminHostedService.cs tests/Lakona.Game.Server.Tests/Health/LakonaHealthHttpHostedServiceTests.cs tests/Lakona.Game.Server.Tests/LocalAdmin/LakonaLocalAdminHostedServiceTests.cs
git commit -m "Make framework HTTP listeners startup-ready"
```

### Task 4: Add the final structured startup success log

**Files:**
- Create: `src/Lakona.Game.Server/Hosting/LakonaServerStartupHostedService.cs`
- Create: `tests/Lakona.Game.Server.Tests/Hosting/LakonaServerStartupHostedServiceTests.cs`
- Modify: `src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs`

- [ ] **Step 1: Write failing final-log tests**

Add a recording `ILoggerProvider` that captures category, level, formatted
message, exception, and the key/value state. Build small Generic Hosts to prove:

```csharp
[Fact]
public async Task Success_log_is_written_after_all_hosted_services_start()
{
    var events = new List<string>();
    using var provider = new RecordingLoggerProvider(events);
    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton(new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions { Id = "node-a" }
            });
            services.AddSingleton<IHostedService>(new RecordingHostedService(events));
            services.AddSingleton<IHostedService, LakonaServerStartupHostedService>();
        })
        .ConfigureLogging(logging => logging.ClearProviders().AddProvider(provider))
        .Build();

    await host.StartAsync(TestContext.Current.CancellationToken);

    Assert.Equal("component-started", events[0]);
    Assert.Equal("startup-log", events[1]);
    var entry = Assert.Single(provider.Entries, entry =>
        entry.Message == "Lakona server started successfully. NodeId=node-a.");
    Assert.Equal(LogLevel.Information, entry.Level);
    Assert.Equal("node-a", entry.State["NodeId"]);
}
```

Add separate tests that prove:

- the success message is written exactly once;
- the structured state contains `NodeId` and does not contain
  `StartupActors` or `Listeners`;
- when an earlier hosted service throws from `StartAsync`, `host.StartAsync`
  fails and no success message is recorded;
- `AddLakonaGameServer` registers exactly one final lifecycle service even when
  called through normal framework composition.

- [ ] **Step 2: Run the tests and verify RED**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~LakonaServerStartupHostedServiceTests"
```

Expected: compilation fails because `LakonaServerStartupHostedService` does not
exist.

- [ ] **Step 3: Implement the lifecycle logger**

Create the internal service with no-op lifecycle methods except `StartedAsync`:

```csharp
internal sealed class LakonaServerStartupHostedService(
    LakonaGameRuntimeOptions runtimeOptions,
    ILogger<LakonaServerStartupHostedService> logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Lakona server started successfully. NodeId={NodeId}.",
            runtimeOptions.Node.Id);
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Register it once in `AddLakonaGameServer`:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IHostedService, LakonaServerStartupHostedService>());
```

The service may be registered before or after other hosted services because the
Generic Host invokes every `StartedAsync` only after all `StartAsync` calls have
completed. Do not log from `LakonaGameServer.RunAsync` or
`IHostApplicationLifetime.ApplicationStarted`, because neither replaces the
listener gates.

- [ ] **Step 4: Run focused game-server tests to verify GREEN**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~LakonaServerStartupHostedServiceTests"
```

Expected: all final-log ordering, structure, failure, and registration tests
pass.

- [ ] **Step 5: Commit the final log**

```powershell
git add src/Lakona.Game.Server/Hosting/LakonaServerStartupHostedService.cs src/Lakona.Game.Server/LakonaGameServerServiceCollectionExtensions.cs tests/Lakona.Game.Server.Tests/Hosting/LakonaServerStartupHostedServiceTests.cs
git commit -m "Log trustworthy Lakona server startup success"
```

### Task 5: Document the contract and bump affected packages

**Files:**
- Modify: `docs/cluster.md`
- Modify: `docs/configuration.md`
- Modify: `src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj`
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`

- [ ] **Step 1: Update current startup documentation**

In `docs/cluster.md`, append the final startup-order step:

```markdown
7. After every enabled framework listener has bound and all hosted startup work
   has completed, log `Lakona server started successfully` with the node id.
```

In `docs/configuration.md`, clarify below the ready endpoint example:

```markdown
The framework emits `Lakona server started successfully. NodeId={NodeId}.` only
after startup actors and lifecycle callbacks complete, cluster registration
succeeds, and every enabled RPC, cluster, health, and local-admin listener has
bound successfully.
```

- [ ] **Step 2: Bump shippable package versions**

Change:

```xml
<!-- src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj -->
<Version>0.13.7</Version>

<!-- src/Lakona.Game.Server/Lakona.Game.Server.csproj -->
<Version>0.11.1</Version>
```

The tool reads these versions from project metadata at build time, so no
hard-coded template version file needs editing.

- [ ] **Step 3: Run package and documentation guards**

Run:

```powershell
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
```

Expected: both scripts exit 0. If the package graph requires an additional
consumer bump, inspect the reported dependency edge and update only the required
shippable consumer and its generated-project version source.

- [ ] **Step 4: Commit docs and versions**

```powershell
git add docs/cluster.md docs/configuration.md src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj src/Lakona.Game.Server/Lakona.Game.Server.csproj
git commit -m "Document framework startup readiness contract"
```

### Task 6: Final verification and hygiene

**Files:**
- Verify all files changed by Tasks 1–5
- Do not stage: `samples/Game.Unity.Agar/Client/ProjectSettings/EditorSettings.asset`
- Do not stage: `samples/Game.Unity.Agar/docker-compose.yml`

- [ ] **Step 1: Run focused tests sequentially**

```powershell
dotnet test tests/Lakona.Rpc.Tests/Lakona.Rpc.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore
```

Expected: both projects pass with no test-host errors or unobserved background
service failures.

- [ ] **Step 2: Build and test the solution**

```powershell
dotnet build Lakona.slnx --no-restore
dotnet test Lakona.slnx --no-build --no-restore
```

Expected: solution build and tests pass. If the solution test run times out,
run test projects sequentially using the loop documented in `CONTRIBUTING.md`
and record any intentionally skipped environment-dependent suite.

- [ ] **Step 3: Run repository hygiene checks**

```powershell
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
git diff --check
git status --short
```

Expected: guards and whitespace checks pass. Status still shows the two
pre-existing Agar files as user-owned modifications; they are not part of this
change.

- [ ] **Step 4: Inspect the complete implementation diff**

```powershell
git diff HEAD~4 -- src/Lakona.Rpc.Server src/Lakona.Game.Server tests/Lakona.Rpc.Tests tests/Lakona.Game.Server.Tests docs/cluster.md docs/configuration.md
```

Confirm:

- there is exactly one final success log call;
- its template is exactly
  `Lakona server started successfully. NodeId={NodeId}.`;
- no success log path bypasses listener readiness;
- bind exceptions retain their original exception and stack;
- public RPC APIs are unchanged;
- component listener logs remain present;
- only `NodeId` is added as a structured business property;
- both package versions are bumped.

- [ ] **Step 5: Perform final review**

Use the repository code-review workflow over the complete implementation diff.
Fix any actionable lifecycle, cancellation, logging, or test-isolation finding,
then rerun the smallest affected test followed by both focused test projects.
