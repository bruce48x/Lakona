# Suppress Host Shutdown Prompts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove repeated Ctrl+C instructions from Lakona startup output while retaining Lakona-owned readiness logs.

**Architecture:** Shorten the RPC listener message at its source so every listener reports only its bound address. Configure the Lakona game server's .NET console lifetime through the official options object, avoiding a custom `IHostLifetime` or message-level logging filters.

**Tech Stack:** C# 13, .NET 10 Generic Host, Microsoft.Extensions.Options, xUnit v3, PowerShell repository guards.

---

## File Map

- Modify `tests/Lakona.Rpc.Tests/RpcServerHostBuilderTests.cs`: protect the exact RPC listener log contract.
- Modify `src/Lakona.Rpc.Server/Hosting/RpcServerHost.cs`: remove the interactive shutdown suffix.
- Create `tests/Lakona.Game.Server.Tests/Hosting/LakonaGameServerConsoleLifetimeTests.cs`: protect the host lifetime options contract.
- Modify `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs`: suppress .NET console-lifetime status messages.
- Modify the five package project files listed in Task 3: apply the direct and dependency-closure patch bumps.

### Task 1: Shorten the RPC Listener Log

**Files:**
- Modify: `tests/Lakona.Rpc.Tests/RpcServerHostBuilderTests.cs`
- Modify: `src/Lakona.Rpc.Server/Hosting/RpcServerHost.cs:74`

- [ ] **Step 1: Write the failing RPC log test**

Add this test beside the existing `RunAsync` tests. It reuses
`TrackingNeverAcceptAcceptor`, reaching the listening state before cancellation.

```csharp
[Fact]
public async Task RunAsync_LogsListeningAddressWithoutShutdownPrompt()
{
    var listeningLog = new TaskCompletionSource<string>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    using var cts = new CancellationTokenSource();
    var acceptor = new TrackingNeverAcceptAcceptor(() => { });

    var host = RpcServerHostBuilder.Create()
        .UseSerializer(new JsonRpcSerializer())
        .UseAcceptor(_ => ValueTask.FromResult<IRpcConnectionAcceptor>(acceptor))
        .UseLogger(message =>
        {
            if (message.StartsWith("RPC server listening on ", StringComparison.Ordinal))
                listeningLog.TrySetResult(message);
        })
        .ConfigureServices(_ => { })
        .Build();

    var runTask = host.RunAsync(cts.Token).AsTask();
    var message = await listeningLog.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cts.Cancel();
    await runTask.WaitAsync(TimeSpan.FromSeconds(2));

    Assert.Equal("RPC server listening on test://tracking.", message);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

```powershell
dotnet test tests/Lakona.Rpc.Tests/Lakona.Rpc.Tests.csproj --no-restore --filter "FullyQualifiedName~RunAsync_LogsListeningAddressWithoutShutdownPrompt"
```

Expected: FAIL because the actual message still ends with
`Press Ctrl+C to stop.`

- [ ] **Step 3: Make the minimal RPC log change**

Replace the listener log call in `RpcServerHost.RunAsync` with:

```csharp
_logger.LogInformation(
    "RPC server listening on {ListenAddress}.",
    baseAcceptor.ListenAddress);
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2 again.

Expected: PASS with 1 test passed and no failures.

- [ ] **Step 5: Commit the RPC behavior**

```powershell
git add tests/Lakona.Rpc.Tests/RpcServerHostBuilderTests.cs src/Lakona.Rpc.Server/Hosting/RpcServerHost.cs
git commit -m "Remove RPC shutdown prompt"
```

### Task 2: Suppress Generic Host Status Messages

**Files:**
- Create: `tests/Lakona.Game.Server.Tests/Hosting/LakonaGameServerConsoleLifetimeTests.cs`
- Modify: `src/Lakona.Game.Server/Hosting/LakonaGameServer.cs:97-106`

- [ ] **Step 1: Write the failing console-lifetime options test**

Create the test file below. Reflection leaves the production factory private
while inspecting the options registered by the real builder path.

```csharp
using System.Reflection;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaGameServerConsoleLifetimeTests
{
    [Fact]
    public void CreateApplicationBuilder_SuppressesConsoleLifetimeStatusMessages()
    {
        var createBuilder = typeof(LakonaGameServer).GetMethod(
            "CreateApplicationBuilder",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(createBuilder);

        var builder = Assert.IsType<HostApplicationBuilder>(
            createBuilder.Invoke(null, [Array.Empty<string>()]));
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<ConsoleLifetimeOptions>>()
            .Value;

        Assert.True(options.SuppressStatusMessages);
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~CreateApplicationBuilder_SuppressesConsoleLifetimeStatusMessages"
```

Expected: FAIL because `SuppressStatusMessages` is currently `false`.

- [ ] **Step 3: Configure the official console-lifetime option**

After `builder.Logging.ClearProviders()` in
`LakonaGameServer.CreateApplicationBuilder`, add:

```csharp
builder.Services.Configure<ConsoleLifetimeOptions>(options =>
    options.SuppressStatusMessages = true);
```

No custom lifetime, log-message matching, or category filter is added.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2 again.

Expected: PASS with 1 test passed and no failures.

- [ ] **Step 5: Commit the host behavior**

```powershell
git add tests/Lakona.Game.Server.Tests/Hosting/LakonaGameServerConsoleLifetimeTests.cs src/Lakona.Game.Server/Hosting/LakonaGameServer.cs
git commit -m "Suppress host lifetime status messages"
```

### Task 3: Apply Package Versions and Complete Verification

**Files:**
- Modify: `src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj`
- Modify: `src/Lakona.Game.Cluster.Rpc/Lakona.Game.Cluster.Rpc.csproj`
- Modify: `src/Lakona.Game.Cluster.Rpc.MemoryPack/Lakona.Game.Cluster.Rpc.MemoryPack.csproj`
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Modify: `src/Lakona.Tool/Lakona.Tool.csproj`

- [ ] **Step 1: Apply direct and dependency-closure patch bumps**

Update the five package versions:

```xml
<!-- src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj -->
<Version>0.13.8</Version>

<!-- src/Lakona.Game.Cluster.Rpc/Lakona.Game.Cluster.Rpc.csproj -->
<Version>0.3.2</Version>

<!-- src/Lakona.Game.Cluster.Rpc.MemoryPack/Lakona.Game.Cluster.Rpc.MemoryPack.csproj -->
<Version>0.2.2</Version>

<!-- src/Lakona.Game.Server/Lakona.Game.Server.csproj -->
<Version>0.11.3</Version>

<!-- src/Lakona.Tool/Lakona.Tool.csproj -->
<Version>0.17.3</Version>
```

The closure follows `Game.Cluster.Rpc -> Rpc.Server`,
`Game.Cluster.Rpc.MemoryPack -> Game.Cluster.Rpc`,
`Game.Server -> both RPC packages`, and the Tool version-source edges.

- [ ] **Step 2: Run affected test projects**

```powershell
dotnet test tests/Lakona.Rpc.Tests/Lakona.Rpc.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore
```

Expected: both projects pass with zero failed tests.

- [ ] **Step 3: Run the package-version graph guard**

```powershell
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
```

Expected: PASS with all package-version graph tests passing.

- [ ] **Step 4: Confirm removed wording and diff hygiene**

```powershell
rg -n --glob '!docs/superpowers/**' --glob '!**/bin/**' --glob '!**/obj/**' "Press Ctrl\+C to (shut down|stop)" src tests samples
git diff --check
git status --short
```

Expected: `rg` finds no runtime or test occurrence of either prompt;
`git diff --check` reports no errors; status lists only the five intended
version files.

- [ ] **Step 5: Commit package versions**

```powershell
git add src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj src/Lakona.Game.Cluster.Rpc/Lakona.Game.Cluster.Rpc.csproj src/Lakona.Game.Cluster.Rpc.MemoryPack/Lakona.Game.Cluster.Rpc.MemoryPack.csproj src/Lakona.Game.Server/Lakona.Game.Server.csproj src/Lakona.Tool/Lakona.Tool.csproj
git commit -m "Bump packages after startup log cleanup"
```

- [ ] **Step 6: Verify the branch is clean**

```powershell
git status --short
git log -4 --oneline
```

Expected: no status output; the latest commits contain the RPC prompt removal,
host status suppression, and package version closure.
