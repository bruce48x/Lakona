# One-RPC-Session Lifecycle And Generated Hotfix Services Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the target Lakona.Game model where `GameSessionKey` represents one game RPC session, endpoint names disappear from game-session APIs, and hotfix-backed RPC service glue is generated from shared `[RpcService]` contracts.

**Architecture:** Keep transport listener configuration separate from game session identity. `Lakona.Game.Server` owns one active RPC connection per `GameSessionKey`, stores callback proxies by callback contract type, and publishes session lifecycle hooks. `Lakona.Game.Server.Hotfix.Generators` discovers shared RPC contracts and generates stable proxies and service bindings without marker files.

**Tech Stack:** .NET 10, C# source generators, xUnit v3, `Lakona.Rpc.Core`, `Lakona.Rpc.Server`, `Lakona.Game.Server`, `Lakona.Game.Server.Hotfix`, `Lakona.Tool`.

---

## Authority

This plan implements [generated-hotfix-services-and-session-lifecycle.md](./generated-hotfix-services-and-session-lifecycle.md).

Repository rules come from [CONTRIBUTING.md](../../CONTRIBUTING.md). Do not place durable architecture material under `docs/superpowers/**`.

## Non-Negotiable Outcomes

- `GameSessionKey` identifies exactly one game RPC session.
- One active RPC connection maps to at most one `GameSessionKey`.
- One game RPC session has at most one active RPC connection.
- Multiple game RPC sessions for one account, player, character, room, or match are user-owned business state.
- `EndpointName`, `GameEndpointName`, and `SessionEndpointKey` are removed from public game session, hotfix call, generated hotfix proxy, lifecycle, and reliable push APIs.
- `Lakona.Game:Endpoints[]` remains listener configuration only.
- Generated projects do not contain `Server/App/Services/GeneratedServiceEndpoints.cs`.
- Generated projects do not contain `[HotfixRpcService(...)]` marker declarations.
- Generated hotfix service proxies call `IHotfixServiceInvoker` by `[RpcMethod]` id, not by C# method name.

Do not rename or remove `Lakona.Game.Cluster.Messaging.ClusterNodeSenderOptions.EndpointName` in this refactor. That name selects a cluster node endpoint from node-directory records and is outside the game client session model.

## Implementation Clarifications

These clarifications resolve handoff questions from the first implementation
reader. Treat them as binding decisions for this plan.

- Do not add `ILakonaGameServerBuilder`. Generated extensions continue to
  extend the existing concrete `Lakona.Game.Server.Hosting.LakonaGameServerBuilder`.
- `UseGeneratedHotfixServices()` must keep using
  `LakonaGameServerBuilder.BindServices(...)`. `LakonaGameRpcConfigurator`
  binds services through the builder's binder delegate; registering a
  standalone `RpcServiceRegistry` in DI does not affect endpoint configuration.
- `IHotfixServiceInvoker` remains in
  `Lakona.Game.Server.Hotfix.Abstractions`. `Lakona.Game.Server.Hotfix.Dispatch`
  owns `HotfixServiceInvoker`, the implementation.
- After disconnect, `SessionState.ConnectionId` represents only an active
  connection. Store the disconnected id separately as
  `LastDisconnectedConnectionId`; this lets rebinding the same session publish a
  fresh active bind result.
- Shared RPC contracts must be discovered from both the current compilation
  assembly and referenced assemblies. Generated projects usually expose
  `Shared/Contracts` through a project reference, which appears to the source
  generator as metadata.
- The stable generator must emit a required hotfix service contract provider.
  `HotfixManager` consumes provider output when it calls
  `HotfixBehaviorScanner.Scan(...)`, so missing or duplicate
  `[HotfixService(typeof(TContract))]` implementations fail during validate or
  reload.
- Failing-test steps are local TDD checkpoints only. Do not commit a state where
  the relevant test project cannot compile. Every committed checkpoint should be
  buildable for the affected projects; the final validation task restores full
  repository confidence.

## Target Public Shape

Use these names consistently unless an existing package-local convention forces a narrower rename.

```csharp
public sealed class GameSessionBindResult
{
    public GameSessionBindResult(GameSessionSnapshot? sessionBecameActive)
    {
        SessionBecameActive = sessionBecameActive;
    }

    public GameSessionSnapshot? SessionBecameActive { get; }
}

public sealed class GameSessionSnapshot
{
    public GameSessionSnapshot(
        GameSessionKey session,
        string connectionId,
        IReadOnlyList<Type> callbackContractTypes)
    {
        Session = session;
        ConnectionId = connectionId;
        CallbackContractTypes = callbackContractTypes;
    }

    public GameSessionKey Session { get; }
    public string ConnectionId { get; }
    public IReadOnlyList<Type> CallbackContractTypes { get; }
}

public sealed class GameSessionBinding<TCallback>
    where TCallback : class
{
    public GameSessionBinding(
        GameSessionKey session,
        string connectionId,
        TCallback callback)
    {
        Session = session;
        ConnectionId = connectionId;
        Callback = callback;
    }

    public GameSessionKey Session { get; }
    public string ConnectionId { get; }
    public TCallback Callback { get; }
}
```

```csharp
public interface IGameSessionDirectory
{
    ValueTask<GameSessionKey> StartNewSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default);

    ValueTask<SessionResumeDecision> TryResumeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionBindResult> BindSessionAsync<TCallback>(
        GameSessionKey session,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<GameSessionSnapshot?> MarkConnectionDisconnectedAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask MarkSessionDisconnectedAsync(
        GameSessionKey session,
        string? connectionId = null,
        CancellationToken cancellationToken = default);

    ValueTask MarkSessionTerminatedAsync(
        GameSessionKey session,
        SessionTerminationNotice notice,
        bool keepForResume,
        CancellationToken cancellationToken = default);

    ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<GameSessionBinding<TCallback>?> GetSessionBindingAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<IReadOnlyList<GameSessionSnapshot>> ExpireDisconnectedSessionsAsync(
        DateTimeOffset disconnectedBefore,
        CancellationToken cancellationToken = default);
}
```

```csharp
public interface ILakonaGameServer
{
    ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
        string ownerKey,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
        GameSessionResumeRequest request,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask BindSessionAsync<TCallback>(
        GameSessionKey session,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask MarkSessionDisconnectedAsync(
        GameSessionKey session,
        string? connectionId = null,
        CancellationToken cancellationToken = default);

    ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask TerminateSessionAsync(
        GameSessionKey session,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<long> PublishReliablePushAsync<TCallback, TPayload>(
        GameSessionKey session,
        string kind,
        TPayload payload,
        ReliablePushDeliver<TCallback, TPayload> deliver,
        CancellationToken cancellationToken = default)
        where TCallback : class;
}
```

## File Map

### Runtime APIs

- Delete: `src/Lakona.Game.Abstractions/Sessions/GameEndpointName.cs`
- Delete: `src/Lakona.Game.Server/Sessions/SessionEndpointKey.cs`
- Rename or replace: `src/Lakona.Game.Server/Sessions/GameSessionEndpointBinding.cs`
- Rename or replace: `src/Lakona.Game.Server/Sessions/GameSessionEndpointSnapshot.cs`
- Modify: `src/Lakona.Game.Server/Sessions/IGameSessionDirectory.cs`
- Modify: `src/Lakona.Game.Server/Sessions/InMemoryGameSessionDirectory.cs`
- Modify: `src/Lakona.Game.Server/Sessions/GameSessionLifecycle.cs`
- Modify: `src/Lakona.Game.Server/Sessions/GameSessionRpcLifecycleObserver.cs`
- Modify: `src/Lakona.Game.Server/Sessions/GameSessionCleanupHostedService.cs`
- Rename or replace: `src/Lakona.Game.Server/Sessions/IGameSessionEndpointCloser.cs`
- Rename or replace: `src/Lakona.Game.Server/Sessions/NoopGameSessionEndpointCloser.cs`
- Modify: `src/Lakona.Game.Server/Sessions/SessionCleanupOptions.cs`
- Modify: `src/Lakona.Game.Server/ILakonaGameServer.cs`
- Modify: `src/Lakona.Game.Server/LakonaGameServer.cs`
- Modify: `src/Lakona.Game.Server/Hotfix/HotfixServiceCall.cs`
- Modify: `src/Lakona.Game.Server/ReliablePush/ReliablePushOutboxSessionExtensions.cs`
- Modify: other files under `src/Lakona.Game.Server/ReliablePush/**` only when they reference endpoint-specific server API overloads.

### Hotfix Generator And Dispatch

- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/IHotfixRequiredServiceContracts.cs` in Task 7 before generated code references it.
- Modify: `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`
- Modify: `src/Lakona.Game.Server.Hotfix.Generators/HotfixGeneratorDiagnostics.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanner.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/HotfixManager.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/HotfixServiceCollectionExtensions.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/Dispatch/HotfixServiceInvoker.cs` only if generated service proxies still call string method overloads.
- Modify: `src/Lakona.Game.Server.Hotfix.Abstractions/Attributes/HotfixRpcServiceAttribute.cs` only to remove active generator usage; delete it in Task 11 after all sample and tool references are gone.

### Tool And Samples

- Modify: `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`
- Modify: `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`
- Delete from generated output: `Server/App/Services/GeneratedServiceEndpoints.cs`
- Delete from sample: `samples/Game.Godot.Chat/Server/App/Services/GeneratedServiceEndpoints.cs`
- Modify: `samples/Game.Godot.Chat/Server/App/Program.cs`
- Modify: `samples/Game.Godot.Chat/Server/App/Lifecycle/ChatPresenceLifecycleHandler.cs`
- Modify: `samples/Game.Godot.Chat/Server/Hotfix/Login/LoginService.cs`
- Modify: `samples/Game.Godot.Chat/Server/Hotfix/Chat/ChatService.cs`
- Modify Unity Agar server files that reference `SessionEndpointKey` after source scan lists exact paths.

### Tests

- Modify: `tests/Lakona.Game.Server.Tests/GameSessionDirectoryTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/GameSessionCleanupHostedServiceTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/GameSessionLifecycleBridgeTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixBehaviorScannerTests.cs`
- Modify: `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs`
- Modify: `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`

## Task 1: Add Failing Tool Output Guardrails

**Files:**
- Modify: `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs`
- Modify: `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`

- [ ] **Step 1: Update renderer test expectations**

Replace the `GeneratedServiceEndpoints.cs` assertions in `ServerAppRendererTests.AddFiles_EmitsServerAppProjectProgramAndCompactSettings` with this block:

```csharp
Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Server/App/Services/GeneratedServiceEndpoints.cs");
Assert.DoesNotContain(plan.Files, file => file.Content.Contains("GeneratedServiceEndpoints", StringComparison.Ordinal));
Assert.DoesNotContain(plan.Files, file => file.Content.Contains("HotfixRpcService", StringComparison.Ordinal));
Assert.DoesNotContain(plan.Files, file => file.Content.Contains("EndpointName", StringComparison.Ordinal));
Assert.DoesNotContain(plan.Files, file => file.Content.Contains("GameEndpointName", StringComparison.Ordinal));
Assert.DoesNotContain(plan.Files, file => file.Content.Contains("SessionEndpointKey", StringComparison.Ordinal));

var lifecycle = AssertPath(plan, "Server/App/Lifecycle/ChatPresenceLifecycleHandler.cs").Content;
Assert.Contains("internal sealed class ChatPresenceLifecycleHandler : IGameSessionLifecycleHandler", lifecycle, StringComparison.Ordinal);
Assert.Contains("OnSessionExpiredAsync", lifecycle, StringComparison.Ordinal);
Assert.DoesNotContain("OnEndpoint", lifecycle, StringComparison.Ordinal);
Assert.DoesNotContain("RpcSession", lifecycle, StringComparison.Ordinal);
Assert.DoesNotContain("Disconnected +=", lifecycle, StringComparison.Ordinal);
```

In the same test, change the cleanup option assertion:

```csharp
Assert.Contains("options.DisconnectedSessionRetention = TimeSpan.FromSeconds(30);", program, StringComparison.Ordinal);
Assert.DoesNotContain("DisconnectedEndpointRetention", program, StringComparison.Ordinal);
```

Remove this assertion because `Server.App.Services` disappears:

```csharp
Assert.Contains("using Server.App.Services;", program, StringComparison.Ordinal);
```

- [ ] **Step 2: Add generated project source scan**

Append this test to `ToolArchitectureScanTests`:

```csharp
[Fact]
public async Task NewProject_DoesNotGenerateManualHotfixServiceGlueOrEndpointSessionNames()
{
    var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-tool-session-shape-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(parentRoot);
    try
    {
        var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            parentRoot,
            ClientEngine.Godot,
            TransportKind.WebSocket,
            SerializerKind.Json,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
        var generator = CreateGenerator();

        await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Services", "GeneratedServiceEndpoints.cs")));

        var generatedText = ReadAllTextFiles(spec.Layout.RootPath);
        Assert.DoesNotContain("GeneratedServiceEndpoints", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("HotfixRpcService(", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("GameEndpointName", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionEndpointKey", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("OnEndpointBound", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("OnEndpointDisconnected", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("OnEndpointExpired", generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("RpcSession.Disconnected +=", generatedText, StringComparison.Ordinal);
    }
    finally
    {
        Directory.Delete(parentRoot, recursive: true);
    }
}
```

- [ ] **Step 3: Run tool tests and confirm failure**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore --filter "FullyQualifiedName~ServerAppRendererTests|FullyQualifiedName~ToolArchitectureScanTests"
```

Expected: FAIL. The failure must mention `GeneratedServiceEndpoints.cs`, `HotfixRpcService`, `EndpointName`, `DisconnectedEndpointRetention`, or `OnEndpointExpiredAsync`.

- [ ] **Step 4: Keep the failing tests local**

Do not commit after this step. These failing tests are a local TDD checkpoint.
Commit only after Task 9 removes the generated marker file and the affected tool
tests pass.

Expected: `git status --short` shows test edits, and the next task continues
from the same working tree.

## Task 2: Add Failing Game Session Directory Tests

**Files:**
- Modify: `tests/Lakona.Game.Server.Tests/GameSessionDirectoryTests.cs`

- [ ] **Step 1: Replace endpoint-centric tests with session-centric tests**

Replace the current endpoint tests in `GameSessionDirectoryTests` with these tests. Keep `AddSessionsRegistersDirectory`.

```csharp
[Fact]
public async Task StartingSecondSessionForSameOwnerLeavesBothSessionsResumable()
{
    var directory = new InMemoryGameSessionDirectory();
    var first = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    var second = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

    var firstDecision = await directory.TryResumeAsync(first, TestContext.Current.CancellationToken);
    var secondDecision = await directory.TryResumeAsync(second, TestContext.Current.CancellationToken);

    Assert.Equal(SessionResumeStatus.Resumed, firstDecision.Status);
    Assert.Equal(SessionResumeStatus.Resumed, secondDecision.Status);
}

[Fact]
public async Task MultipleCallbackContractsShareOneSessionWithoutOverwritingEachOther()
{
    var directory = new InMemoryGameSessionDirectory();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    var login = new LoginCallback("login");
    var chat = new ChatCallback("chat");

    await directory.BindSessionAsync(session, "connection-a", login, TestContext.Current.CancellationToken);
    await directory.BindSessionAsync(session, "connection-a", chat, TestContext.Current.CancellationToken);

    Assert.Same(login, await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
    Assert.Same(chat, await directory.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
}

[Fact]
public async Task RebindingSameCallbackContractOnSameConnectionReplacesOnlyThatContract()
{
    var directory = new InMemoryGameSessionDirectory();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    var firstLogin = new LoginCallback("first-login");
    var secondLogin = new LoginCallback("second-login");
    var chat = new ChatCallback("chat");

    await directory.BindSessionAsync(session, "connection-a", firstLogin, TestContext.Current.CancellationToken);
    await directory.BindSessionAsync(session, "connection-a", chat, TestContext.Current.CancellationToken);
    await directory.BindSessionAsync(session, "connection-a", secondLogin, TestContext.Current.CancellationToken);

    Assert.Same(secondLogin, await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
    Assert.Same(chat, await directory.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
}

[Fact]
public async Task RebindingSameSessionToNewConnectionClearsCallbacksFromOldConnection()
{
    var directory = new InMemoryGameSessionDirectory();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    var login = new LoginCallback("login");

    await directory.BindSessionAsync(session, "old-connection", new LoginCallback("old-login"), TestContext.Current.CancellationToken);
    await directory.BindSessionAsync(session, "old-connection", new ChatCallback("old-chat"), TestContext.Current.CancellationToken);
    await directory.BindSessionAsync(session, "new-connection", login, TestContext.Current.CancellationToken);

    Assert.Same(login, await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
    Assert.Null(await directory.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
}

[Fact]
public async Task BindingSecondActiveSessionToSameConnectionIsRejected()
{
    var directory = new InMemoryGameSessionDirectory();
    var first = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    var second = await directory.StartNewSessionAsync("player-b", TestContext.Current.CancellationToken);

    await directory.BindSessionAsync(first, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);

    await Assert.ThrowsAsync<InvalidOperationException>(() => directory
        .BindSessionAsync(second, "connection-a", new LoginCallback("other"), TestContext.Current.CancellationToken)
        .AsTask());
}

[Fact]
public async Task MarkConnectionDisconnectedReturnsOneSessionSnapshotAndClearsCallbacks()
{
    var directory = new InMemoryGameSessionDirectory();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

    await directory.BindSessionAsync(session, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);
    await directory.BindSessionAsync(session, "connection-a", new ChatCallback("chat"), TestContext.Current.CancellationToken);

    var disconnected = await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

    Assert.NotNull(disconnected);
    Assert.Equal(session, disconnected.Session);
    Assert.Equal("connection-a", disconnected.ConnectionId);
    Assert.Equal(2, disconnected.CallbackContractTypes.Count);
    Assert.Contains(typeof(LoginCallback), disconnected.CallbackContractTypes);
    Assert.Contains(typeof(ChatCallback), disconnected.CallbackContractTypes);
    Assert.Null(await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
    Assert.Null(await directory.GetCallbackAsync<ChatCallback>(session, TestContext.Current.CancellationToken));
}

[Fact]
public async Task ExpireDisconnectedSessionsReturnsStaleDisconnectedSessionOnce()
{
    var directory = new InMemoryGameSessionDirectory();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

    await directory.BindSessionAsync(session, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);
    await directory.BindSessionAsync(session, "connection-a", new ChatCallback("chat"), TestContext.Current.CancellationToken);
    await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

    var expired = await directory.ExpireDisconnectedSessionsAsync(DateTimeOffset.UtcNow.AddSeconds(1), TestContext.Current.CancellationToken);

    var snapshot = Assert.Single(expired);
    Assert.Equal(session, snapshot.Session);
    Assert.Equal("connection-a", snapshot.ConnectionId);
    Assert.Equal(2, snapshot.CallbackContractTypes.Count);
}

[Fact]
public async Task StaleConnectionIdCannotDetachNewerBinding()
{
    var directory = new InMemoryGameSessionDirectory();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    var callback = new LoginCallback("new");

    await directory.BindSessionAsync(session, "old", new LoginCallback("old"), TestContext.Current.CancellationToken);
    await directory.BindSessionAsync(session, "new", callback, TestContext.Current.CancellationToken);
    await directory.MarkSessionDisconnectedAsync(session, "old", TestContext.Current.CancellationToken);

    Assert.Same(callback, await directory.GetCallbackAsync<LoginCallback>(session, TestContext.Current.CancellationToken));
}

[Fact]
public async Task BindingSessionAfterTerminationIsRejected()
{
    var directory = new InMemoryGameSessionDirectory();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    var notice = new SessionTerminationNotice(session, SessionTerminationReason.Policy);

    await directory.MarkSessionTerminatedAsync(
        session,
        notice,
        keepForResume: true,
        TestContext.Current.CancellationToken);

    await Assert.ThrowsAsync<InvalidOperationException>(() => directory
        .BindSessionAsync(
            session,
            "connection-a",
            new LoginCallback("login"),
            TestContext.Current.CancellationToken)
        .AsTask());
}
```

- [ ] **Step 2: Run directory tests and confirm compile failure**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~GameSessionDirectoryTests
```

Expected: FAIL. The failure must mention missing `BindSessionAsync`, `GameSessionSnapshot`, `ExpireDisconnectedSessionsAsync`, or removed `SessionEndpointKey` references.

- [ ] **Step 3: Keep the failing tests local**

Do not commit after this step. These failing tests are a local TDD checkpoint.
Commit them together with the session directory implementation in Task 3.

Expected: `git status --short` shows test edits, and Task 3 makes the affected
test project compile and pass.

## Task 3: Refactor Session Directory To Direct GameSessionKey Storage

**Files:**
- Delete: `src/Lakona.Game.Abstractions/Sessions/GameEndpointName.cs`
- Delete: `src/Lakona.Game.Server/Sessions/SessionEndpointKey.cs`
- Rename: `src/Lakona.Game.Server/Sessions/GameSessionEndpointBinding.cs` to `src/Lakona.Game.Server/Sessions/GameSessionBinding.cs`
- Rename: `src/Lakona.Game.Server/Sessions/GameSessionEndpointSnapshot.cs` to `src/Lakona.Game.Server/Sessions/GameSessionSnapshot.cs`
- Modify: `src/Lakona.Game.Server/Sessions/IGameSessionDirectory.cs`
- Modify: `src/Lakona.Game.Server/Sessions/InMemoryGameSessionDirectory.cs`
- Modify: `tests/Lakona.Game.Server.Tests/GameSessionDirectoryTests.cs`

- [ ] **Step 1: Replace endpoint DTOs with session DTOs**

`GameSessionBinding.cs` must contain:

```csharp
using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionBinding<TCallback>
    where TCallback : class
{
    public GameSessionBinding(
        GameSessionKey session,
        string connectionId,
        TCallback callback)
    {
        Session = session;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public GameSessionKey Session { get; }

    public string ConnectionId { get; }

    public TCallback Callback { get; }
}
```

`GameSessionSnapshot.cs` must contain:

```csharp
using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionSnapshot
{
    public GameSessionSnapshot(
        GameSessionKey session,
        string connectionId,
        IReadOnlyList<Type> callbackContractTypes)
    {
        Session = session;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        CallbackContractTypes = callbackContractTypes ?? throw new ArgumentNullException(nameof(callbackContractTypes));
    }

    public GameSessionKey Session { get; }

    public string ConnectionId { get; }

    public IReadOnlyList<Type> CallbackContractTypes { get; }
}
```

Add `GameSessionBindResult` to `GameSessionLifecycle.cs` or a new `GameSessionBindResult.cs` file:

```csharp
namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionBindResult
{
    public GameSessionBindResult(GameSessionSnapshot? sessionBecameActive)
    {
        SessionBecameActive = sessionBecameActive;
    }

    public GameSessionSnapshot? SessionBecameActive { get; }
}
```

- [ ] **Step 2: Replace `IGameSessionDirectory`**

Replace the interface body with the target shape from the "Target Public Shape" section of this plan.

- [ ] **Step 3: Rework `InMemoryGameSessionDirectory` state**

Use session-keyed storage with these invariants:

```csharp
private sealed class SessionState
{
    public SessionState(GameSessionKey session, string ownerKey)
    {
        Session = session;
        OwnerKey = ownerKey;
    }

    public GameSessionKey Session { get; }
    public string OwnerKey { get; }
    public string? ConnectionId { get; set; }
    public string? LastDisconnectedConnectionId { get; set; }
    public DateTimeOffset? DisconnectedAt { get; set; }
    public SessionTerminationNotice? Termination { get; set; }
    public bool KeepTerminationForResume { get; set; }
    public Dictionary<Type, object> Callbacks { get; } = new();
}
```

Keep these indexes:

```csharp
private readonly object _gate = new();
private readonly Dictionary<GameSessionKey, SessionState> _sessions = new();
private readonly Dictionary<string, GameSessionKey> _connectionToSession = new(StringComparer.Ordinal);
```

`StartNewSessionAsync` must always add a new `GameSessionKey`. It must not remove or mark older sessions for the same `ownerKey` as `StateLost`.

`BindSessionAsync` must follow this sequence under `_gate`:

```csharp
if (!_sessions.TryGetValue(session, out var state))
{
    throw new InvalidOperationException($"Game session '{session}' does not exist.");
}

if (state.Termination is not null)
{
    throw new InvalidOperationException($"Game session '{session}' is terminated.");
}

if (_connectionToSession.TryGetValue(connectionId, out var boundSession)
    && boundSession != session)
{
    throw new InvalidOperationException($"RPC connection '{connectionId}' is already bound to game session '{boundSession}'.");
}

var previousConnectionId = state.ConnectionId;
var sessionBecameActive = previousConnectionId is null;
if (!string.Equals(previousConnectionId, connectionId, StringComparison.Ordinal))
{
    if (previousConnectionId is not null)
    {
        _connectionToSession.Remove(previousConnectionId);
        state.Callbacks.Clear();
    }

    state.ConnectionId = connectionId;
    _connectionToSession[connectionId] = session;
}

state.LastDisconnectedConnectionId = null;
state.DisconnectedAt = null;
state.Callbacks[typeof(TCallback)] = callback;

return new GameSessionBindResult(sessionBecameActive
    ? CreateSnapshot(state)
    : null);
```

When `previousConnectionId` differs from `connectionId`, clear all old callbacks because they point at the previous RPC session.

`MarkConnectionDisconnectedAsync` must remove the connection index, clear callbacks, set `DisconnectedAt`, move the active id into `LastDisconnectedConnectionId`, clear `ConnectionId`, and return one `GameSessionSnapshot?`.

Use this order so the returned snapshot still contains the disconnected id:

```csharp
if (!_connectionToSession.TryGetValue(connectionId, out var session))
{
    return null;
}

var state = _sessions[session];
var snapshot = CreateSnapshot(state, connectionId);
_connectionToSession.Remove(connectionId);
state.ConnectionId = null;
state.LastDisconnectedConnectionId = connectionId;
state.DisconnectedAt = DateTimeOffset.UtcNow;
state.Callbacks.Clear();
return snapshot;
```

`MarkSessionDisconnectedAsync` must ignore stale `connectionId` values:

```csharp
if (connectionId is not null
    && !string.Equals(state.ConnectionId, connectionId, StringComparison.Ordinal))
{
    return default;
}
```

After the stale-id check passes, `MarkSessionDisconnectedAsync` must follow the
same state transition as `MarkConnectionDisconnectedAsync`: remove the active
connection index, set `LastDisconnectedConnectionId`, clear `ConnectionId`, set
`DisconnectedAt`, and clear callbacks.

`ExpireDisconnectedSessionsAsync` must return one snapshot per expired session
using `LastDisconnectedConnectionId` as the snapshot connection id, then remove
the session from `_sessions`. It should also remove `_connectionToSession` only
when `ConnectionId` is not null, which should happen only for defensive cleanup
of inconsistent state.

- [ ] **Step 4: Run directory tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~GameSessionDirectoryTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add src/Lakona.Game.Abstractions/Sessions src/Lakona.Game.Server/Sessions tests/Lakona.Game.Server.Tests/GameSessionDirectoryTests.cs
git commit -m "refactor: store game sessions without endpoint children"
```

## Task 4: Rename Lifecycle Hooks From Endpoint To Session

**Files:**
- Modify: `src/Lakona.Game.Server/Sessions/GameSessionLifecycle.cs`
- Modify: `src/Lakona.Game.Server/Sessions/GameSessionRpcLifecycleObserver.cs`
- Modify: `src/Lakona.Game.Server/Sessions/GameSessionCleanupHostedService.cs`
- Modify: `src/Lakona.Game.Server/Sessions/SessionCleanupOptions.cs`
- Rename: `src/Lakona.Game.Server/Sessions/IGameSessionEndpointCloser.cs` to `src/Lakona.Game.Server/Sessions/IGameSessionConnectionCloser.cs`
- Rename: `src/Lakona.Game.Server/Sessions/NoopGameSessionEndpointCloser.cs` to `src/Lakona.Game.Server/Sessions/NoopGameSessionConnectionCloser.cs`
- Modify: `tests/Lakona.Game.Server.Tests/GameSessionLifecycleBridgeTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/GameSessionCleanupHostedServiceTests.cs`

- [ ] **Step 1: Replace lifecycle context and handler names**

`GameSessionLifecycle.cs` must expose:

```csharp
public sealed class GameSessionBindingContext
{
    public GameSessionBindingContext(
        GameSessionKey session,
        string connectionId,
        IReadOnlyList<Type> callbackContractTypes)
    {
        Session = session;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        CallbackContractTypes = callbackContractTypes ?? throw new ArgumentNullException(nameof(callbackContractTypes));
    }

    public GameSessionKey Session { get; }

    public string ConnectionId { get; }

    public IReadOnlyList<Type> CallbackContractTypes { get; }
}

public interface IGameSessionLifecycleHandler
{
    ValueTask OnConnectionOpenedAsync(
        GameConnectionContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionBoundAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionDisconnectedAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionExpiredAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionTerminatedAsync(
        GameSessionTerminationContext context,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Rename cleanup option**

In `SessionCleanupOptions.cs`, replace `DisconnectedEndpointRetention` with:

```csharp
public TimeSpan DisconnectedSessionRetention { get; set; } = TimeSpan.FromMinutes(5);
```

Update validation and hosted-service usage to read `DisconnectedSessionRetention`.

- [ ] **Step 3: Update lifecycle bridge tests**

Update test assertions so connection disconnect returns one session context:

```csharp
Assert.Equal(session, disconnected.Session);
Assert.Equal("connection-a", disconnected.ConnectionId);
Assert.Contains(typeof(LoginCallback), disconnected.CallbackContractTypes);
Assert.Contains(typeof(ChatCallback), disconnected.CallbackContractTypes);
```

Tracking handlers in tests must implement `OnSessionBoundAsync`, `OnSessionDisconnectedAsync`, and `OnSessionExpiredAsync`.

- [ ] **Step 4: Update runtime bridge**

`GameSessionRpcLifecycleObserver` must call:

```csharp
var snapshot = await directory.MarkConnectionDisconnectedAsync(context.ConnectionId, cancellationToken)
    .ConfigureAwait(false);
if (snapshot is null)
{
    return;
}

var sessionContext = new GameSessionBindingContext(
    snapshot.Session,
    snapshot.ConnectionId,
    snapshot.CallbackContractTypes);

foreach (var handler in handlers)
{
    await handler.OnSessionDisconnectedAsync(sessionContext, cancellationToken)
        .ConfigureAwait(false);
}
```

`GameSessionCleanupHostedService` must call `ExpireDisconnectedSessionsAsync` and publish `OnSessionExpiredAsync` once per returned snapshot.

- [ ] **Step 5: Run lifecycle tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GameSessionLifecycleBridgeTests|FullyQualifiedName~GameSessionCleanupHostedServiceTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/Lakona.Game.Server/Sessions tests/Lakona.Game.Server.Tests/GameSessionLifecycleBridgeTests.cs tests/Lakona.Game.Server.Tests/GameSessionCleanupHostedServiceTests.cs
git commit -m "refactor: publish session lifecycle hooks"
```

## Task 5: Remove Endpoint Names From ILakonaGameServer And Reliable Push

**Files:**
- Modify: `src/Lakona.Game.Server/ILakonaGameServer.cs`
- Modify: `src/Lakona.Game.Server/LakonaGameServer.cs`
- Modify: `src/Lakona.Game.Server/ReliablePush/**`
- Modify: `tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs`

- [ ] **Step 1: Replace `ILakonaGameServer`**

Use the interface from the "Target Public Shape" section. Keep existing non-endpoint overloads for raw reliable push:

```csharp
ValueTask<long> PublishReliablePushAsync(
    GameSessionKey session,
    string kind,
    object payload,
    Func<ReliablePushRecord, ValueTask> deliver,
    CancellationToken cancellationToken = default);

ValueTask ReplayReliablePushAsync(
    GameSessionKey session,
    Func<ReliablePushRecord, ValueTask> deliver,
    CancellationToken cancellationToken = default);
```

Update the typed replay overload to remove endpoint name:

```csharp
ValueTask ReplayReliablePushAsync<TCallback, TPayload>(
    GameSessionKey session,
    string kind,
    ReliablePushDeliver<TCallback, TPayload> deliver,
    CancellationToken cancellationToken = default)
    where TCallback : class;
```

- [ ] **Step 2: Update `LakonaGameServer` implementation**

Use `IGameSessionDirectory.BindSessionAsync`, `MarkSessionDisconnectedAsync`, and `GetCallbackAsync<TCallback>(session)`.

Typed reliable push delivery must resolve the callback from the session:

```csharp
var callback = await GetCallbackAsync<TCallback>(session, cancellationToken).ConfigureAwait(false);
if (callback is null)
{
    return;
}

await deliver(callback, payload, cancellationToken).ConfigureAwait(false);
```

Keep termination by session only. Remove the overload that terminates a single endpoint.

- [ ] **Step 3: Update tests**

In `LakonaGameServerTests`, replace calls like this:

```csharp
await server.StartSessionAsync("player-a", GameEndpointName.Control, "connection-a", callback, TestContext.Current.CancellationToken);
```

with:

```csharp
await server.StartSessionAsync("player-a", "connection-a", callback, TestContext.Current.CancellationToken);
```

Replace `SessionEndpointKey` assertions with `GameSessionKey` assertions. A fake closer must record:

```csharp
public List<(GameSessionKey Session, string ConnectionId, SessionTerminationNotice Notice)> Closed { get; } = new();
```

- [ ] **Step 4: Run server tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore --filter FullyQualifiedName~LakonaGameServerTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests/LakonaGameServerTests.cs
git commit -m "refactor: remove endpoint names from game server api"
```

## Task 6: Remove Endpoint Names From Hotfix Call Context

**Files:**
- Modify: `src/Lakona.Game.Server/Hotfix/HotfixServiceCall.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixDispatchTests.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Tests/TestHotfixServiceCall.cs`
- Modify: hotfix sample files under `samples/**/Server/Hotfix/**`

- [ ] **Step 1: Replace `HotfixServiceCall` constructors**

`HotfixServiceCall.cs` must match:

```csharp
public class HotfixServiceCall<TRequest>
{
    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
    {
        Request = request;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Actors = actors ?? throw new ArgumentNullException(nameof(actors));
        GameServer = gameServer ?? throw new ArgumentNullException(nameof(gameServer));
    }

    public TRequest Request { get; }
    public string ConnectionId { get; }
    public IServiceProvider Services { get; }
    public IActorRuntime Actors { get; }
    public ILakonaGameServer GameServer { get; }
}

public sealed class HotfixServiceCall<TRequest, TCallback> : HotfixServiceCall<TRequest>
    where TCallback : class
{
    public HotfixServiceCall(
        TRequest request,
        string connectionId,
        TCallback callback,
        IServiceProvider services,
        IActorRuntime actors,
        ILakonaGameServer gameServer)
        : base(request, connectionId, services, actors, gameServer)
    {
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public TCallback Callback { get; }
}
```

- [ ] **Step 2: Update hotfix tests**

Any test helper constructing `HotfixServiceCall` must remove `GameEndpointName.Control` or endpoint string arguments.

- [ ] **Step 3: Run hotfix runtime tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "FullyQualifiedName~HotfixDispatchTests|FullyQualifiedName~HotfixBehaviorScannerTests"
```

Expected: PASS.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src/Lakona.Game.Server/Hotfix tests/Lakona.Game.Server.Hotfix.Tests samples
git commit -m "refactor: remove endpoint name from hotfix call context"
```

## Task 7: Generate Hotfix Service Proxies From Shared RPC Contracts

**Files:**
- Create: `src/Lakona.Game.Server.Hotfix.Abstractions/IHotfixRequiredServiceContracts.cs`
- Modify: `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`
- Modify: `src/Lakona.Game.Server.Hotfix.Generators/HotfixGeneratorDiagnostics.cs`
- Keep temporarily: `src/Lakona.Game.Server.Hotfix.Abstractions/Attributes/HotfixRpcServiceAttribute.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Generators.Tests/GeneratorTestHost.cs`

- [ ] **Step 1: Add required-contract provider abstraction**

Create `IHotfixRequiredServiceContracts.cs` before adding generator output that
references it:

```csharp
namespace Lakona.Game.Server.Hotfix.Abstractions;

public interface IHotfixRequiredServiceContracts
{
    IReadOnlyList<Type> ServiceContracts { get; }
}
```

- [ ] **Step 2: Add generator tests for marker-free discovery**

Replace marker-based service tests in `HotfixGeneratorTests` with:

```csharp
[Fact]
public void Generator_discovers_shared_rpc_service_contract_without_marker()
{
    var source = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Shared.Contracts.Chat
        {
            public static class RpcContractIds
            {
                public const int ChatService = 1;
                public const int Bind = 7;
            }

            public sealed class ChatBindRequest
            {
            }

            public interface IChatCallback
            {
            }

            [RpcService(RpcContractIds.ChatService, NotificationContract = typeof(IChatCallback))]
            public interface IChatService
            {
                [RpcMethod(RpcContractIds.Bind)]
                ValueTask BindAsync(ChatBindRequest req);
            }
        }

        namespace Server.App.Generated
        {
            using System;
            using Lakona.Rpc.Server;
            using Shared.Contracts.Chat;

            public sealed class ChatCallbackProxy : IChatCallback
            {
                public ChatCallbackProxy(RpcSession session)
                {
                }
            }

            public static class ChatServiceBinder
            {
                public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, IChatService> implFactory)
                {
                }
            }
        }
        """;

    var result = GeneratorTestHost.Run(source);

    Assert.Empty(result.ErrorDiagnostics);
    Assert.Contains("internal sealed class ChatServiceProxy : global::Shared.Contracts.Chat.IChatService", result.GeneratedSource);
    Assert.Contains("HotfixServiceCall<global::Shared.Contracts.Chat.ChatBindRequest, global::Shared.Contracts.Chat.IChatCallback>", result.GeneratedSource);
    Assert.Contains("global::Server.App.Generated.ChatServiceBinder.BindFactory", result.GeneratedSource);
    Assert.Contains("UseGeneratedHotfixServices", result.GeneratedSource);
    Assert.DoesNotContain("HotfixRpcService", result.GeneratedSource, StringComparison.Ordinal);
    Assert.DoesNotContain("GameEndpointName", result.GeneratedSource, StringComparison.Ordinal);
}
```

Add a result-returning method test:

```csharp
[Fact]
public void Generator_uses_rpc_method_id_for_result_returning_hotfix_call()
{
    var source = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Shared.Contracts.Login
        {
            public sealed class LoginRequest
            {
            }

            public sealed class LoginReply
            {
            }

            public interface ILoginCallback
            {
            }

            [RpcService(10, NotificationContract = typeof(ILoginCallback))]
            public interface ILoginService
            {
                [RpcMethod(9)]
                ValueTask<LoginReply> LoginAsync(LoginRequest request);
            }
        }

        namespace Server.App.Generated
        {
            using System;
            using Lakona.Rpc.Server;
            using Shared.Contracts.Login;

            public sealed class LoginCallbackProxy : ILoginCallback
            {
                public LoginCallbackProxy(RpcSession session)
                {
                }
            }

            public static class LoginServiceBinder
            {
                public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, ILoginService> implFactory)
                {
                }
            }
        }
        """;

    var result = GeneratorTestHost.Run(source);

    Assert.Empty(result.ErrorDiagnostics);
    Assert.Contains("InvokeAsync<global::Shared.Contracts.Login.ILoginService", result.GeneratedSource);
    Assert.Contains("9,", result.GeneratedSource);
    Assert.Contains("global::Shared.Contracts.Login.LoginReply", result.GeneratedSource);
    Assert.DoesNotContain("nameof(LoginAsync)", result.GeneratedSource, StringComparison.Ordinal);
    Assert.DoesNotContain("\"LoginAsync\"", result.GeneratedSource, StringComparison.Ordinal);
}
```

- [ ] **Step 3: Add metadata-reference discovery test support**

Extend `GeneratorTestHost` with a helper that compiles referenced shared
contracts into a metadata reference:

```csharp
public static GeneratorRunResult RunWithReference(string appSource, string referencedSource)
{
    var references = CreateDefaultReferences();
    var referencedCompilation = CSharpCompilation.Create(
        "Shared",
        new[] { CSharpSyntaxTree.ParseText(referencedSource) },
        references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    using var stream = new MemoryStream();
    var emit = referencedCompilation.Emit(stream);
    if (!emit.Success)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
    }

    stream.Position = 0;
    var sharedReference = MetadataReference.CreateFromStream(stream);
    return Run(appSource, references.Concat(new[] { sharedReference }).ToArray());
}
```

Refactor existing `Run(string source)` to call:

```csharp
private static GeneratorRunResult Run(string source, IReadOnlyList<MetadataReference> references)
```

Then add this test:

```csharp
[Fact]
public void Generator_discovers_shared_rpc_service_contract_from_metadata_reference()
{
    var sharedSource = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Shared.Contracts.Login
        {
            public sealed class LoginRequest
            {
            }

            public interface ILoginCallback
            {
            }

            [RpcService(10, NotificationContract = typeof(ILoginCallback))]
            public interface ILoginService
            {
                [RpcMethod(9)]
                ValueTask LoginAsync(LoginRequest request);
            }
        }
        """;

    var appSource = """
        namespace Server.App.Generated
        {
            using System;
            using Lakona.Rpc.Server;
            using Shared.Contracts.Login;

            public sealed class LoginCallbackProxy : ILoginCallback
            {
                public LoginCallbackProxy(RpcSession session)
                {
                }
            }

            public static class LoginServiceBinder
            {
                public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, ILoginService> implFactory)
                {
                }
            }
        }
        """;

    var result = GeneratorTestHost.RunWithReference(appSource, sharedSource);

    Assert.Empty(result.ErrorDiagnostics);
    Assert.Contains("ILoginService", result.GeneratedSource, StringComparison.Ordinal);
    Assert.Contains("LoginServiceProxy", result.GeneratedSource, StringComparison.Ordinal);
}
```

- [ ] **Step 4: Replace marker discovery with RPC service discovery**

In `HotfixGenerator.cs`, remove syntax-provider logic that finds `[HotfixRpcService]` declarations. Discover interface symbols with `[RpcService]` from the current compilation assembly and from metadata reference assemblies.

Use assembly-symbol traversal instead of current-project syntax-only discovery:

```csharp
private static IEnumerable<INamedTypeSymbol> DiscoverRpcServiceContracts(Compilation compilation)
{
    foreach (var contract in EnumerateTypes(compilation.Assembly.GlobalNamespace))
    {
        if (IsUserRpcService(contract))
        {
            yield return contract;
        }
    }

    foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
    {
        foreach (var contract in EnumerateTypes(assembly.GlobalNamespace))
        {
            if (IsUserRpcService(contract))
            {
                yield return contract;
            }
        }
    }
}

private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
{
    foreach (var type in namespaceSymbol.GetTypeMembers())
    {
        foreach (var nested in EnumerateTypes(type))
        {
            yield return nested;
        }
    }

    foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
    {
        foreach (var type in EnumerateTypes(childNamespace))
        {
            yield return type;
        }
    }
}

private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamedTypeSymbol type)
{
    yield return type;
    foreach (var nested in type.GetTypeMembers())
    {
        foreach (var item in EnumerateTypes(nested))
        {
            yield return item;
        }
    }
}
```

Filter out framework contracts:

```csharp
private static bool IsUserRpcService(INamedTypeSymbol contract)
{
    var assemblyName = contract.ContainingAssembly?.Name ?? string.Empty;
    return !assemblyName.StartsWith("Lakona.", StringComparison.Ordinal)
        && HasRpcServiceAttribute(contract);
}
```

Keep only supported contracts:

```csharp
private static bool HasRpcServiceAttribute(INamedTypeSymbol contract)
{
    return contract.GetAttributes().Any(static attribute =>
        attribute.AttributeClass?.ToDisplayString() == "Lakona.Rpc.Core.RpcServiceAttribute");
}
```

Remove `EndpointName` and `BindingSetName` from the generator model. A service model needs:

```csharp
private sealed class HotfixRpcServiceInfo
{
    public HotfixRpcServiceInfo(
        INamedTypeSymbol contract,
        string generatedProxyNamespace,
        string generatedServerNamespace)
    {
        Contract = contract;
        GeneratedProxyNamespace = generatedProxyNamespace;
        GeneratedServerNamespace = generatedServerNamespace;
    }

    public INamedTypeSymbol Contract { get; }
    public string GeneratedProxyNamespace { get; }
    public string GeneratedServerNamespace { get; }
}
```

Use existing MSBuild properties for generated namespaces. If no property exists for generated server binder namespace, derive it from the RPC generator output currently used by tests: `Server.App.Generated`.

- [ ] **Step 5: Generate one binding extension without binding-set switch**

The generated extension must have one path:

```csharp
public static class GeneratedHotfixServiceExtensions
{
    public static global::Lakona.Game.Server.Hosting.LakonaGameServerBuilder UseGeneratedHotfixServices(
        this global::Lakona.Game.Server.Hosting.LakonaGameServerBuilder builder)
    {
        global::System.ArgumentNullException.ThrowIfNull(builder);

        builder.AddServices(services =>
        {
            global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<
                global::Lakona.Game.Server.Hotfix.Abstractions.IHotfixRequiredServiceContracts,
                GeneratedHotfixRequiredServiceContracts>(
                services);
        });

        return builder.BindServices(BindGeneratedHotfixServices);
    }

    private static void BindGeneratedHotfixServices(
        global::Lakona.Rpc.Server.RpcServiceRegistry registry,
        global::System.IServiceProvider services)
    {
        // one generated BindFactory call per shared RPC service
    }
}
```

`LakonaGameRpcConfigurator` uses the binder delegate returned by
`LakonaGameServerBuilder.GetServiceBinder()`. Do not register a standalone
`RpcServiceRegistry` singleton for generated RPC services; that registry is not
used by the current endpoint configuration path.

Generated code must reference
`global::Lakona.Game.Server.Hotfix.Abstractions.IHotfixServiceInvoker` when it
requests the invoker from DI. The implementation type remains
`global::Lakona.Game.Server.Hotfix.Dispatch.HotfixServiceInvoker` and is
registered by the existing hotfix service registration path.

Generate the required service contract provider in the same generated source:

```csharp
internal sealed class GeneratedHotfixRequiredServiceContracts :
    global::Lakona.Game.Server.Hotfix.Abstractions.IHotfixRequiredServiceContracts
{
    public global::System.Collections.Generic.IReadOnlyList<global::System.Type> ServiceContracts { get; } =
    [
        typeof(global::Shared.Contracts.Login.ILoginService),
        typeof(global::Shared.Contracts.Chat.IChatService)
    ];
}
```

Do not generate `case "default"`, `case "control"`, `case "realtime"`, or any binding-set parameter.

- [ ] **Step 6: Generate proxy methods with method id**

For a `ValueTask<TResult>` RPC method, generate:

```csharp
return _hotfix.InvokeAsync<global::Shared.Contracts.Login.ILoginService, global::Lakona.Game.Server.Hotfix.HotfixServiceCall<global::Shared.Contracts.Login.LoginRequest, global::Shared.Contracts.Login.ILoginCallback>, global::Shared.Contracts.Login.LoginReply>(
    9,
    call,
    cancellationToken);
```

For a `ValueTask` RPC method, generate:

```csharp
return _hotfix.InvokeAsync<global::Shared.Contracts.Chat.IChatService, global::Lakona.Game.Server.Hotfix.HotfixServiceCall<global::Shared.Contracts.Chat.ChatBindRequest, global::Shared.Contracts.Chat.IChatCallback>>(
    7,
    call,
    cancellationToken);
```

The generated `HotfixServiceCall` constructor call must pass:

```csharp
new global::Lakona.Game.Server.Hotfix.HotfixServiceCall<TRequest, TCallback>(
    request,
    session.ConnectionId,
    callback,
    _services,
    _actors,
    _gameServer)
```

No generated constructor call may pass `GameEndpointName`.

- [ ] **Step 7: Remove marker diagnostics**

Delete diagnostics that only describe marker partial classes, duplicate marker declarations, and binding-set endpoint mismatch:

```txt
ULGHOTFIX003
ULGHOTFIX004
ULGHOTFIX005
```

Keep diagnostics for unsupported service contract, missing `[RpcMethod]`, invalid request parameter count, invalid return type, and invalid callback metadata.

- [ ] **Step 8: Run generator tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Generators.Tests\Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src/Lakona.Game.Server.Hotfix.Generators src/Lakona.Game.Server.Hotfix.Abstractions tests/Lakona.Game.Server.Hotfix.Generators.Tests
git commit -m "feat: generate hotfix service proxies from rpc contracts"
```

## Task 8: Validate Hotfix Service Implementations

**Files:**
- Modify: `src/Lakona.Game.Server.Hotfix/Scanning/HotfixBehaviorScanner.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/HotfixManager.cs`
- Modify: `src/Lakona.Game.Server.Hotfix/HotfixServiceCollectionExtensions.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Tests/HotfixBehaviorScannerTests.cs`

- [ ] **Step 1: Add scanner tests for exact hotfix service implementation count**

Add tests:

```csharp
[Fact]
public void Scanner_requires_one_hotfix_service_for_declared_rpc_contract()
{
    var scan = HotfixBehaviorScanner.Scan(
        typeof(MissingHotfixContract).Assembly,
        [typeof(UnrelatedHotfixService)],
        requiredServiceContracts: [typeof(MissingHotfixContract)]);

    Assert.False(scan.Succeeded);
    Assert.Contains(scan.Diagnostics, diagnostic =>
        diagnostic.Contains("MissingHotfixContract", StringComparison.Ordinal)
        && diagnostic.Contains("exactly one", StringComparison.Ordinal));
}

[Fact]
public void Scanner_rejects_duplicate_hotfix_services_for_declared_rpc_contract()
{
    var scan = HotfixBehaviorScanner.Scan(
        typeof(DuplicateHotfixContract).Assembly,
        [typeof(DuplicateHotfixServiceA), typeof(DuplicateHotfixServiceB)],
        requiredServiceContracts: [typeof(DuplicateHotfixContract)]);

    Assert.False(scan.Succeeded);
    Assert.Contains(scan.Diagnostics, diagnostic =>
        diagnostic.Contains("DuplicateHotfixContract", StringComparison.Ordinal)
        && diagnostic.Contains("2", StringComparison.Ordinal));
}
```

Add local contract and service types in the test file:

```csharp
[RpcService(201)]
public interface MissingHotfixContract
{
    [RpcMethod(1)]
    ValueTask PingAsync(MissingHotfixRequest request);
}

public sealed class MissingHotfixRequest
{
}

public sealed class UnrelatedHotfixService
{
}

[RpcService(202)]
public interface DuplicateHotfixContract
{
    [RpcMethod(1)]
    ValueTask PingAsync(DuplicateHotfixRequest request);
}

public sealed class DuplicateHotfixRequest
{
}

[HotfixService(typeof(DuplicateHotfixContract))]
public sealed class DuplicateHotfixServiceA
{
    public static ValueTask PingAsync(HotfixServiceCall<DuplicateHotfixRequest> call)
    {
        return default;
    }
}

[HotfixService(typeof(DuplicateHotfixContract))]
public sealed class DuplicateHotfixServiceB
{
    public static ValueTask PingAsync(HotfixServiceCall<DuplicateHotfixRequest> call)
    {
        return default;
    }
}
```

- [ ] **Step 2: Add scanner overload**

Add an overload or optional parameter:

```csharp
public static HotfixBehaviorScanResult Scan(
    Assembly assembly,
    IReadOnlyList<Type>? candidateTypes = null,
    IReadOnlyList<Type>? requiredServiceContracts = null)
```

While scanning `[HotfixService]` types, record implementation classes once per
contract type:

```csharp
var serviceImplementations = new Dictionary<Type, HashSet<Type>>();
```

When a type has `[HotfixService(typeof(TContract))]`, record it before scanning
methods:

```csharp
if (!serviceImplementations.TryGetValue(service.ContractType, out var implementations))
{
    implementations = new HashSet<Type>();
    serviceImplementations.Add(service.ContractType, implementations);
}

implementations.Add(type);
```

After scanning, validate implementation class counts, not method binding counts:

```csharp
foreach (var contract in requiredServiceContracts ?? Array.Empty<Type>())
{
    serviceImplementations.TryGetValue(contract, out var implementations);
    var count = implementations?.Count ?? 0;
    if (count != 1)
    {
        diagnostics.Add($"Hotfix service contract '{contract.FullName}' requires exactly one [HotfixService] implementation; found {count}.");
    }
}
```

Use the existing internal service binding model when the scanner stores service metadata under another name. Do not create runtime reflection dispatch outside the scanner.

- [ ] **Step 3: Pass required contracts into `HotfixManager`**

Update `HotfixManager` so the constructor accepts required service contracts:

```csharp
private readonly IReadOnlyList<Type> _requiredServiceContracts;

public HotfixManager(
    IHotfixAssemblySource source,
    IEnumerable<string>? sharedAssemblyNames = null,
    IEnumerable<Type>? requiredServiceContracts = null)
{
    _source = source ?? throw new ArgumentNullException(nameof(source));
    _sharedAssemblyNames = (sharedAssemblyNames ?? Array.Empty<string>())
        .Where(static name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    _requiredServiceContracts = (requiredServiceContracts ?? Array.Empty<Type>())
        .Distinct()
        .ToArray();
}
```

Update the reload path:

```csharp
var scan = HotfixBehaviorScanner.Scan(
    assembly,
    requiredServiceContracts: _requiredServiceContracts);
```

Update `HotfixServiceCollectionExtensions.AddLakonaGameHotfix` so it reads all
registered providers when it creates the manager:

```csharp
services.AddSingleton<IHotfixManager>(provider =>
{
    var requiredContracts = provider
        .GetServices<IHotfixRequiredServiceContracts>()
        .SelectMany(static item => item.ServiceContracts)
        .Distinct()
        .ToArray();

    return new HotfixManager(
        provider.GetRequiredService<IHotfixAssemblySource>(),
        sharedNames,
        requiredContracts);
});
```

This is the bridge from stable generated contract discovery to hotfix reload
validation. Do not make `HotfixManager` scan `Server.App` on its own.

- [ ] **Step 4: Run hotfix scanner tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter FullyQualifiedName~HotfixBehaviorScannerTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add src/Lakona.Game.Server.Hotfix src/Lakona.Game.Server.Hotfix.Abstractions tests/Lakona.Game.Server.Hotfix.Tests/HotfixBehaviorScannerTests.cs
git commit -m "test: validate required hotfix service implementations"
```

## Task 9: Remove GeneratedServiceEndpoints From Tool And Samples

**Files:**
- Modify: `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs`
- Modify: `src/Lakona.Tool/Rendering/Server/HotfixRenderer.cs`
- Modify: `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs`
- Modify: `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`
- Delete: `samples/Game.Godot.Chat/Server/App/Services/GeneratedServiceEndpoints.cs`
- Modify: `samples/Game.Godot.Chat/Server/App/Program.cs`
- Modify: `samples/Game.Godot.Chat/Server/App/Lifecycle/ChatPresenceLifecycleHandler.cs`
- Modify: `samples/Game.Godot.Chat/Server/Hotfix/Login/LoginService.cs`
- Modify: `samples/Game.Godot.Chat/Server/Hotfix/Chat/ChatService.cs`

- [ ] **Step 1: Stop rendering service endpoint marker file**

In `ServerAppRenderer.AddFiles`, remove:

```csharp
builder.AddFile("Server/App/Services/GeneratedServiceEndpoints.cs", RenderGeneratedServiceEndpoints(), FileWriteMode.Replace, GeneratedFileKind.Text);
```

Delete `RenderGeneratedServiceEndpoints`.

- [ ] **Step 2: Update generated Program.cs**

Remove:

```csharp
using Server.App.Services;
```

Change cleanup option:

```csharp
options.DisconnectedSessionRetention = TimeSpan.FromSeconds(30);
```

- [ ] **Step 3: Update generated lifecycle handler**

Replace endpoint hook methods with session hook methods:

```csharp
public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
{
    return default;
}

public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
{
    return default;
}

public async ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
{
    try
    {
        await _actors.AskAsync<ChatRoomActor, bool>(
            RoomId,
            async (room, ct) =>
            {
                await HotfixDispatch.Invoke<ChatRoomActor, ValueTask>(
                    "LeaveAsync",
                    room,
                    [typeof(string)],
                    [context.ConnectionId]);
                return true;
            });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Chat presence cleanup failed: {ex}");
    }
}
```

- [ ] **Step 4: Update generated hotfix services**

Generated `LoginService` must call:

```csharp
var session = await call.GameServer.StartSessionAsync(
    ownerKey,
    call.ConnectionId,
    call.Callback,
    cancellationToken);
```

Generated `ChatService` must call:

```csharp
await call.GameServer.BindSessionAsync(
    call.Request.Session,
    call.ConnectionId,
    call.Callback,
    cancellationToken);
```

No generated hotfix service may reference `call.EndpointName`.

- [ ] **Step 5: Apply the same edits to `samples/Game.Godot.Chat`**

Delete `samples/Game.Godot.Chat/Server/App/Services/GeneratedServiceEndpoints.cs`.

Update sample Program, lifecycle handler, login service, and chat service with the exact shapes from Steps 2 through 4.

- [ ] **Step 6: Run tool tests**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Lakona.Tool tests/Lakona.Tool.Tests samples/Game.Godot.Chat/Server
git commit -m "feat: generate game hotfix bindings without marker files"
```

## Review Follow-Up: a0c8c2b Template Closure

These issues were found during manual review of commit
`a0c8c2b31441d8cbc357c11fb4357160c6bbbf5c`. Treat them as required follow-up
work before considering the generated hotfix service lifecycle complete.

### Follow-Up 1: Make Generated Server RPC Properties Visible To Analyzers

**Problem:** `src/Lakona.Tool/Rendering/Server/ServerAppRenderer.cs` renders
`LakonaRpcGenerateServer` and `LakonaRpcServerGeneratedNamespace` as ordinary
MSBuild properties, but it does not render matching `CompilerVisibleProperty`
items. `Lakona.Rpc.Analyzers.SourceGeneration.LakonaRpcSourceGenerator` reads
these settings through analyzer config keys:

```txt
build_property.LakonaRpcGenerateServer
build_property.LakonaRpcServerGeneratedNamespace
```

Without `CompilerVisibleProperty`, a newly generated `Server/App` project can
fail to generate server RPC glue or can generate it under the fallback
`Server.Generated` namespace while `Program.cs` imports `Server.App.Generated`.
The manually updated `samples/Game.Godot.Chat/Server/App/Server.App.csproj`
already has the correct shape; the tool renderer must produce the same shape.

**Required fix:**

In the generated `Server/App/Server.App.csproj`, render:

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="LakonaRpcGenerateServer" />
  <CompilerVisibleProperty Include="LakonaRpcServerGeneratedNamespace" />
</ItemGroup>
```

Keep these entries close to the property group that declares the corresponding
properties.

**Required tests:**

- Update `tests/Lakona.Tool.Tests/Rendering/ServerAppRendererTests.cs` to assert
  that generated `Server/App/Server.App.csproj` contains both
  `CompilerVisibleProperty` entries.
- Keep the existing `LakonaRpcGenerateServer` and
  `LakonaRpcServerGeneratedNamespace` property assertions.

**Required validation:**

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore
```

Expected: PASS.

### Follow-Up 2: Carry GameSessionKey From Login To Chat Callback Binding

**Problem:** The generated chat template and `samples/Game.Godot.Chat` start a
game session during login, but they discard the returned `GameSessionKey`.
`LoginReply` does not expose the session key, `ChatBindRequest` does not carry
it back from the client, and `ChatService.BindAsync` only binds the chat
callback inside `ChatRoomActor`. It never calls `ILakonaGameServer.BindSessionAsync`.

That means the default project teaches a model where the session directory only
knows about `ILoginCallback`; it never records the `IChatCallback` binding for
the same `GameSessionKey`. Reliable push, resume, lifecycle snapshots, and
cleanup logic cannot observe the intended multi-callback-contract session
shape:

```txt
GameSessionKey
  -> ILoginCallback
  -> IChatCallback
```

**Required fix:**

Update both `src/Lakona.Tool` renderers and `samples/Game.Godot.Chat` with the
same contract and hotfix-service shape.

Shared contracts:

- Add a `GameSessionKey Session` property to `LoginReply`.
- Add a `GameSessionKey Session` property to `ChatBindRequest`.
- Add the required `using Lakona.Game.Abstractions;` and project/package
  reference so shared contracts compile for both server and client target
  frameworks.
- Preserve serializer attributes and ordering. For MemoryPack contracts, assign
  stable orders so the new session field does not collide with existing fields.

Generated and sample `LoginService`:

```csharp
var session = await call.GameServer.StartSessionAsync(
    playerName,
    call.ConnectionId,
    call.Callback);

reply.Session = session;
return reply;
```

Generated and sample `ChatService.BindAsync`:

```csharp
await call.GameServer.BindSessionAsync(
    call.Request.Session,
    call.ConnectionId,
    call.Callback);
```

The actor-level `BindChatCallback` may remain for the sample room presence
implementation, but it must not be the only binding. The framework-owned
session directory must also receive the `IChatCallback`.

Generated and sample clients:

- Store the `GameSessionKey` returned by `LoginAsync`.
- Send that key in `new ChatBindRequest { Session = session }`.
- Do not infer the session from the RPC connection id.

**Required tests:**

- Update `tests/Lakona.Tool.Tests/Rendering/HotfixRendererTests.cs` to assert
  that generated `ChatService.BindAsync` calls `call.GameServer.BindSessionAsync`.
- Update `tests/Lakona.Tool.Tests/Rendering/HotfixRendererTests.cs` or
  shared-contract renderer tests to assert that `LoginReply` and
  `ChatBindRequest` include `GameSessionKey Session`.
- Add or update sample/source-scan coverage so
  `samples/Game.Godot.Chat/Server/Hotfix/Chat/ChatService.cs` cannot regress to
  actor-only callback binding.

**Required validation:**

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore
dotnet build samples\Game.Godot.Chat\Server\Server.slnx --no-restore
```

Expected: PASS for both commands.

## Task 10: Migrate Unity Agar Session Usage

**Files:**
- Modify exact paths returned by source scan under `samples/Game.Unity.Agar/Server/Gateway/**`
- Modify tests that cover Unity Agar generated or sample server build

- [ ] **Step 1: Source scan Unity Agar endpoint session usage**

Run:

```powershell
rg -n "SessionEndpointKey|GameEndpointName|ControlEndpointName|RealtimeEndpointName|OnEndpoint|EndpointName" samples\Game.Unity.Agar src tests
```

Expected remaining hits:

- Cluster `EndpointName` under `src/Lakona.Game.Cluster/**` and `tests/Lakona.Game.Cluster.Tests/**`
- Documentation comments that explicitly describe removed API
- Unity Agar files that still need migration

- [ ] **Step 2: Replace Unity Agar gateway session directory keys**

Where Unity Agar currently creates:

```csharp
new SessionEndpointKey(registration.SessionKey, SessionRegistration.ControlEndpointName)
```

replace the framework key with:

```csharp
registration.ControlSessionKey
```

and where it creates:

```csharp
new SessionEndpointKey(registration.SessionKey, SessionRegistration.RealtimeEndpointName)
```

replace it with:

```csharp
registration.RealtimeSessionKey
```

If `SessionRegistration` currently stores one `SessionKey`, split it into two nullable fields owned by sample business state:

```csharp
public GameSessionKey? ControlSessionKey { get; set; }
public GameSessionKey? RealtimeSessionKey { get; set; }
```

Do not add a framework endpoint name to recover the old grouping.

- [ ] **Step 3: Build Unity Agar server project**

Run:

```powershell
dotnet build samples\Game.Unity.Agar\Server\Gateway\Gateway.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 4: Commit**

Run:

```powershell
git add samples/Game.Unity.Agar tests
git commit -m "refactor: make unity agar manage multiple game sessions explicitly"
```

## Task 11: Repository-Wide Source Scan And Cleanup

**Files:**
- Modify any source or test file reported by scans, except cluster endpoint configuration files.
- Delete: `src/Lakona.Game.Server.Hotfix.Abstractions/Attributes/HotfixRpcServiceAttribute.cs` after all source references are gone.
- Modify package README files when public names changed.

- [ ] **Step 1: Scan for removed game-session names**

Run:

```powershell
rg -n "GameEndpointName|SessionEndpointKey|GeneratedServiceEndpoints|HotfixRpcService\(|OnEndpointBound|OnEndpointDisconnected|OnEndpointExpired|DisconnectedEndpointRetention" src tests samples docs
```

Expected allowed hits:

- `docs/game/generated-hotfix-services-and-session-lifecycle.md`
- `docs/game/generated-hotfix-services-and-session-lifecycle-implementation-plan.md`
- Historical or migration-only docs that explicitly say the terms are removed
- No hits under `src/Lakona.Game.Abstractions`
- No hits under `src/Lakona.Game.Server`
- No hits under `src/Lakona.Game.Server.Hotfix.Generators`
- No hits under `src/Lakona.Tool`
- No hits under `samples/Game.Godot.Chat/Server`

- [ ] **Step 2: Delete obsolete hotfix service marker attribute**

After Step 1 shows no source references outside historical documentation, delete:

```txt
src/Lakona.Game.Server.Hotfix.Abstractions/Attributes/HotfixRpcServiceAttribute.cs
```

Run the same scan again. Expected: no `HotfixRpcServiceAttribute` or
`HotfixRpcService(` hits under `src`, `tests`, or `samples`.

- [ ] **Step 3: Scan generated project output for forbidden text**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore --filter FullyQualifiedName~NewProject_DoesNotGenerateManualHotfixServiceGlueOrEndpointSessionNames
```

Expected: PASS.

- [ ] **Step 4: Update READMEs**

Update these files if they still mention endpoint binding as session identity:

```txt
src/Lakona.Game.Abstractions/README.md
src/Lakona.Game.Server/README.md
src/Lakona.Game.Server.Hotfix/README.md
src/Lakona.Game.Server.Hotfix.Generators/README.md
src/Lakona.Tool/README.md
```

The correct wording is "session callback binding", "transport endpoint hosting", and "multiple sessions for one account are user-owned business state".

- [ ] **Step 5: Commit**

Run:

```powershell
git add src tests samples docs
git commit -m "docs: align session lifecycle public wording"
```

## Task 12: Version Bumps And Changelog

**Files:**
- Modify package `.csproj` files under `src/**` that changed shippable package content.
- Modify: `CHANGELOG.md`
- Modify generated template package version constants if `Lakona.Tool` embeds package versions.

- [ ] **Step 1: Identify changed packages**

Run:

```powershell
git diff --name-only HEAD~9..HEAD -- src
```

Every changed package directory under `src/<PackageName>/` with shippable content needs a `<Version>` bump.

At minimum, expect version bumps for:

```txt
src/Lakona.Game.Abstractions/Lakona.Game.Abstractions.csproj
src/Lakona.Game.Server/Lakona.Game.Server.csproj
src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj
src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj
src/Lakona.Game.Server.Hotfix.Generators/Lakona.Game.Server.Hotfix.Generators.csproj
src/Lakona.Tool/Lakona.Tool.csproj
```

- [ ] **Step 2: Update changelog**

Add an unreleased entry:

```markdown
## Unreleased

- Changed Lakona.Game session identity so `GameSessionKey` represents one game RPC session and endpoint names are no longer part of session APIs.
- Added generated hotfix service proxy discovery from shared `[RpcService]` contracts, removing generated-project marker files.
- Removed generated `Server/App/Services/GeneratedServiceEndpoints.cs` from new game projects and samples.
```

Merge with an existing `Unreleased` section if one exists.

- [ ] **Step 3: Commit**

Run:

```powershell
git add CHANGELOG.md src
git commit -m "chore: bump game session generator package versions"
```

## Task 13: Final Validation

**Files:**
- No planned edits.

- [ ] **Step 1: Build repository**

Run:

```powershell
dotnet build Lakona.slnx --no-restore
```

Expected: PASS.

- [ ] **Step 2: Run targeted test projects**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Hotfix.Generators.Tests\Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-restore
dotnet test tests\Lakona.Game.Server.Hotfix.Tests\Lakona.Game.Server.Hotfix.Tests.csproj --no-restore
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore
```

Expected: PASS for every command.

- [ ] **Step 3: Build samples**

Run:

```powershell
dotnet build samples\Game.Godot.Chat\Server\Server.slnx --no-restore
dotnet build samples\Game.Unity.Agar\Server\Gateway\Gateway.csproj --no-restore
```

Expected: PASS for both commands.

- [ ] **Step 4: Full test run**

Run:

```powershell
dotnet test Lakona.slnx --no-build
```

Expected: PASS. If the full run times out, run test projects sequentially with the loop from `CONTRIBUTING.md` and record the first failing project.

- [ ] **Step 5: Final source scan**

Run:

```powershell
rg -n "GameEndpointName|SessionEndpointKey|GeneratedServiceEndpoints|HotfixRpcService\(|OnEndpointBound|OnEndpointDisconnected|OnEndpointExpired|DisconnectedEndpointRetention" src tests samples
```

Expected allowed hits:

- `src/Lakona.Game.Cluster/**` and `tests/Lakona.Game.Cluster.Tests/**` only for cluster endpoint selection.
- No game session, hotfix service, tool renderer, generated project, or Godot Chat sample hits.

- [ ] **Step 6: Final diff check**

Run:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` has no output. `git status --short` contains only intentional implementation changes.

## Review Checklist

- [ ] `GameSessionKey` no longer has framework-owned endpoint children.
- [ ] Starting a new session for an existing owner does not invalidate existing sessions.
- [ ] Binding the same session to a new connection clears callbacks from the old connection.
- [ ] Binding a second active session to an already bound connection throws.
- [ ] Lifecycle hooks are named `OnSessionBoundAsync`, `OnSessionDisconnectedAsync`, and `OnSessionExpiredAsync`.
- [ ] Generated hotfix proxies use numeric `[RpcMethod]` ids.
- [ ] Generated hotfix proxies construct `HotfixServiceCall` without endpoint arguments.
- [ ] Generated projects do not contain endpoint marker files.
- [ ] Generated `Server/App/Server.App.csproj` exposes
      `LakonaRpcGenerateServer` and `LakonaRpcServerGeneratedNamespace` through
      `CompilerVisibleProperty`.
- [ ] Generated chat contracts carry `GameSessionKey` from `LoginReply` into
      `ChatBindRequest`.
- [ ] Generated and sample `ChatService.BindAsync` call
      `ILakonaGameServer.BindSessionAsync` for `IChatCallback`.
- [ ] Cluster endpoint names remain untouched.
- [ ] Package versions are bumped for every changed shippable package.
