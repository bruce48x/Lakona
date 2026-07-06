# Game Session Items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add server-side game session items so high-frequency game services can read validated session-local metadata without querying remote business actors per frame.

**Architecture:** Store scalar-only session items inside the framework session registry, expose them through `ILakonaGameServer`, and pass an immutable per-dispatch snapshot into `HotfixServiceCall`. Keep business authority in actors: session items are local cache, not durable state or actor placement authority.

**Tech Stack:** .NET 10, C#, xUnit, Roslyn source generator tests, Lakona.Game.Server sessions, Lakona.Game.Server.Hotfix.Generators, Agar sample business logic tests.

---

## File Structure

- Create `src/Lakona.Game.Server/Sessions/GameSessionItemValue.cs`
  - Immutable scalar value wrapper for `string`, `long`, and `bool`.
- Create `src/Lakona.Game.Server/Sessions/GameSessionItemKind.cs`
  - Public enum describing the active scalar kind.
- Create `src/Lakona.Game.Server/Sessions/GameSessionItems.cs`
  - Immutable read-only snapshot passed to hotfix calls.
- Modify `src/Lakona.Game.Server/Sessions/IGameSessionRegistry.cs`
  - Add item mutation/read/snapshot methods used by framework internals.
- Modify `src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs`
  - Store item dictionaries per `SessionState`, validate keys, clear items on termination/expiration, preserve items across disconnect/resume.
- Modify `src/Lakona.Game.Server/ILakonaGameServer.cs`
  - Add public session item API for hotfix/game services.
- Modify `src/Lakona.Game.Server/DefaultLakonaGameServer.cs`
  - Forward session item calls to `IGameSessionRegistry`.
- Modify `src/Lakona.Game.Server/Hotfix/HotfixServiceCall.cs`
  - Add `CurrentSessionItems` with backward-compatible constructors defaulting to `GameSessionItems.Empty`.
- Modify `src/Lakona.Game.Server/Hotfix/HotfixLifecycleCall.cs`
  - Keep lifecycle calls using an empty item snapshot.
- Modify `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`
  - Generate current-session item snapshot lookup and pass it into hotfix service calls.
- Modify `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs`
  - Add registry item lifecycle and validation tests.
- Modify `tests/Lakona.Game.Server.Tests/GameSessionLifecycleBridgeTests.cs`
  - Update `SnapshotGameServer` fake for new `ILakonaGameServer` methods.
- Modify `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs`
  - Add constructor-contract, generated-source, and call-level snapshot immutability assertions for `CurrentSessionItems`.
- Modify `tests/Lakona.Game.Server.Hotfix.Tests/TestHotfixServiceCall.cs`
  - Mirror the runtime test stub enough for existing tests to compile.
- Modify `samples/Game.Unity.Agar/Shared/State/RoomContracts.cs`
  - Add realtime session id/generation to `RoomInputSubmitRequest`.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs`
  - Reject stale realtime input when the request session id/generation does not match the room player record.
- Modify `samples/Game.Unity.Agar/Server/Hotfix/Services/BattleService.cs`
  - Populate session items after authoritative attach succeeds and read `roomId`, session id, and session generation from `call.CurrentSessionItems` in the frame path.
  - Before editing, inspect current worktree diff because this file already has a user change from `.Get(new RoomId(req.RoomId))` to `.Local(new RoomId(req.RoomId))`.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixTests.cs` or add `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarRealtimeSessionItemTests.cs`
  - Add focused tests or source scans proving frame-path input no longer queries `UserActor`.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixTests.cs`
  - Update `TestGameServer` fake for new `ILakonaGameServer` methods.
- Modify `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarSessionLifecycleTests.cs`
  - Update `TestGameServer` fake for new `ILakonaGameServer` methods.
- Modify `docs/session.md`
  - Move durable session item ownership and lifecycle rules from the temporary spec into the session authority.
- Modify `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
  - Bump package version because shippable package source changes.

## Task 1: Add Session Item Value Types

**Files:**
- Create: `src/Lakona.Game.Server/Sessions/GameSessionItemKind.cs`
- Create: `src/Lakona.Game.Server/Sessions/GameSessionItemValue.cs`
- Create: `src/Lakona.Game.Server/Sessions/GameSessionItems.cs`
- Test: `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs`

- [ ] **Step 1: Write failing value type tests**

Append these tests near the top-level session registry tests in `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs`:

```csharp
[Fact]
public void Session_item_value_preserves_scalar_kinds()
{
    var text = GameSessionItemValue.FromString("room-a");
    var number = GameSessionItemValue.FromInt64(42);
    var flag = GameSessionItemValue.FromBoolean(true);

    Assert.Equal(GameSessionItemKind.String, text.Kind);
    Assert.Equal("room-a", text.GetString());
    Assert.Equal(GameSessionItemKind.Int64, number.Kind);
    Assert.Equal(42, number.GetInt64());
    Assert.Equal(GameSessionItemKind.Boolean, flag.Kind);
    Assert.True(flag.GetBoolean());
}

[Fact]
public void Empty_session_items_snapshot_returns_missing_values()
{
    Assert.False(GameSessionItems.Empty.TryGetValue("roomId", out _));
    Assert.Null(GameSessionItems.Empty.GetString("roomId"));
    Assert.Null(GameSessionItems.Empty.GetInt64("generation"));
    Assert.Null(GameSessionItems.Empty.GetBoolean("ready"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Session_item_value_preserves_scalar_kinds|FullyQualifiedName~Empty_session_items_snapshot_returns_missing_values"
```

Expected: FAIL because `GameSessionItemValue`, `GameSessionItemKind`, and `GameSessionItems` do not exist.

- [ ] **Step 3: Create `GameSessionItemKind`**

Create `src/Lakona.Game.Server/Sessions/GameSessionItemKind.cs`:

```csharp
namespace Lakona.Game.Server.Sessions;

public enum GameSessionItemKind
{
    String = 1,
    Int64 = 2,
    Boolean = 3
}
```

- [ ] **Step 4: Create `GameSessionItemValue`**

Create `src/Lakona.Game.Server/Sessions/GameSessionItemValue.cs`:

```csharp
namespace Lakona.Game.Server.Sessions;

public readonly struct GameSessionItemValue : IEquatable<GameSessionItemValue>
{
    private readonly string? _stringValue;
    private readonly long _int64Value;
    private readonly bool _booleanValue;

    private GameSessionItemValue(GameSessionItemKind kind, string? stringValue, long int64Value, bool booleanValue)
    {
        Kind = kind;
        _stringValue = stringValue;
        _int64Value = int64Value;
        _booleanValue = booleanValue;
    }

    public GameSessionItemKind Kind { get; }

    public bool IsDefined => Kind is GameSessionItemKind.String or GameSessionItemKind.Int64 or GameSessionItemKind.Boolean;

    public static GameSessionItemValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new GameSessionItemValue(GameSessionItemKind.String, value, 0, false);
    }

    public static GameSessionItemValue FromInt64(long value)
    {
        return new GameSessionItemValue(GameSessionItemKind.Int64, null, value, false);
    }

    public static GameSessionItemValue FromBoolean(bool value)
    {
        return new GameSessionItemValue(GameSessionItemKind.Boolean, null, 0, value);
    }

    public string? TryGetString()
    {
        return Kind == GameSessionItemKind.String ? _stringValue : null;
    }

    public string GetString()
    {
        return Kind == GameSessionItemKind.String
            ? _stringValue!
            : throw new InvalidOperationException($"Session item kind is {Kind}, not {GameSessionItemKind.String}.");
    }

    public long? TryGetInt64()
    {
        return Kind == GameSessionItemKind.Int64 ? _int64Value : null;
    }

    public long GetInt64()
    {
        return Kind == GameSessionItemKind.Int64
            ? _int64Value
            : throw new InvalidOperationException($"Session item kind is {Kind}, not {GameSessionItemKind.Int64}.");
    }

    public bool? TryGetBoolean()
    {
        return Kind == GameSessionItemKind.Boolean ? _booleanValue : null;
    }

    public bool GetBoolean()
    {
        return Kind == GameSessionItemKind.Boolean
            ? _booleanValue
            : throw new InvalidOperationException($"Session item kind is {Kind}, not {GameSessionItemKind.Boolean}.");
    }

    public bool Equals(GameSessionItemValue other)
    {
        return Kind == other.Kind &&
            string.Equals(_stringValue, other._stringValue, StringComparison.Ordinal) &&
            _int64Value == other._int64Value &&
            _booleanValue == other._booleanValue;
    }

    public override bool Equals(object? obj)
    {
        return obj is GameSessionItemValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, _stringValue, _int64Value, _booleanValue);
    }

    public override string ToString()
    {
        return Kind switch
        {
            GameSessionItemKind.String => _stringValue ?? string.Empty,
            GameSessionItemKind.Int64 => _int64Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            GameSessionItemKind.Boolean => _booleanValue ? "true" : "false",
            _ => string.Empty
        };
    }
}
```

- [ ] **Step 5: Create `GameSessionItems`**

Create `src/Lakona.Game.Server/Sessions/GameSessionItems.cs`:

```csharp
namespace Lakona.Game.Server.Sessions;

public sealed class GameSessionItems
{
    private readonly IReadOnlyDictionary<string, GameSessionItemValue> _items;

    public static GameSessionItems Empty { get; } = new(new Dictionary<string, GameSessionItemValue>(StringComparer.Ordinal));

    internal GameSessionItems(IReadOnlyDictionary<string, GameSessionItemValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = new Dictionary<string, GameSessionItemValue>(items, StringComparer.Ordinal);
    }

    public int Count => _items.Count;

    public bool TryGetValue(string key, out GameSessionItemValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _items.TryGetValue(key, out value);
    }

    public GameSessionItemValue? GetValueOrDefault(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _items.TryGetValue(key, out var value) ? value : null;
    }

    public string? GetString(string key)
    {
        return GetValueOrDefault(key)?.TryGetString();
    }

    public long? GetInt64(string key)
    {
        return GetValueOrDefault(key)?.TryGetInt64();
    }

    public bool? GetBoolean(string key)
    {
        return GetValueOrDefault(key)?.TryGetBoolean();
    }
}
```

- [ ] **Step 6: Run value tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Session_item_value_preserves_scalar_kinds|FullyQualifiedName~Empty_session_items_snapshot_returns_missing_values"
```

Expected: PASS.

- [ ] **Step 7: Commit value types**

```powershell
git add src/Lakona.Game.Server/Sessions/GameSessionItemKind.cs src/Lakona.Game.Server/Sessions/GameSessionItemValue.cs src/Lakona.Game.Server/Sessions/GameSessionItems.cs tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs
git commit -m "Add game session item value types"
```

## Task 2: Implement Registry And Server API

**Files:**
- Modify: `src/Lakona.Game.Server/Sessions/IGameSessionRegistry.cs`
- Modify: `src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs`
- Modify: `src/Lakona.Game.Server/ILakonaGameServer.cs`
- Modify: `src/Lakona.Game.Server/DefaultLakonaGameServer.cs`
- Test: `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs`

- [ ] **Step 1: Write failing registry lifecycle tests**

Append these tests to `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs`:

```csharp
[Fact]
public async Task Session_items_can_be_set_read_overwritten_and_removed()
{
    var directory = new InMemoryGameSessionRegistry();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

    await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);
    Assert.Equal("room-a", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());

    await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-b"), TestContext.Current.CancellationToken);
    Assert.Equal("room-b", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());

    await directory.RemoveSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken);
    Assert.Null(await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken));
}

[Fact]
public async Task Session_items_use_ordinal_case_sensitive_keys()
{
    var directory = new InMemoryGameSessionRegistry();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

    await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("lower"), TestContext.Current.CancellationToken);
    await directory.SetSessionItemAsync(session, "RoomId", GameSessionItemValue.FromString("upper"), TestContext.Current.CancellationToken);

    Assert.Equal("lower", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());
    Assert.Equal("upper", (await directory.GetSessionItemAsync(session, "RoomId", TestContext.Current.CancellationToken))?.GetString());
}

[Theory]
[InlineData("")]
[InlineData(" ")]
[InlineData("\t")]
public async Task Session_item_keys_reject_empty_or_whitespace_values(string key)
{
    var directory = new InMemoryGameSessionRegistry();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

    await Assert.ThrowsAsync<ArgumentException>(() => directory
        .SetSessionItemAsync(session, key, GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken)
        .AsTask());
}

[Fact]
public async Task Default_session_item_value_is_rejected()
{
    var directory = new InMemoryGameSessionRegistry();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => directory
        .SetSessionItemAsync(session, "roomId", default, TestContext.Current.CancellationToken)
        .AsTask());
}

[Fact]
public async Task Session_item_snapshots_are_immutable_after_later_mutation()
{
    var directory = new InMemoryGameSessionRegistry();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);

    var snapshot = await directory.GetSessionItemsAsync(session, TestContext.Current.CancellationToken);
    await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-b"), TestContext.Current.CancellationToken);

    Assert.Equal("room-a", snapshot.GetString("roomId"));
    Assert.Equal("room-b", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());
}

[Fact]
public async Task Session_item_keys_reject_overlong_values()
{
    var directory = new InMemoryGameSessionRegistry();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    var key = new string('k', 129);

    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => directory
        .SetSessionItemAsync(session, key, GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken)
        .AsTask());
}

[Fact]
public async Task Session_items_survive_disconnect_and_resume()
{
    var directory = new InMemoryGameSessionRegistry();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    await directory.BindSessionAsync(session, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);
    await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);

    await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);
    var decision = await directory.TryResumeAsync(session, TestContext.Current.CancellationToken);

    Assert.Equal(SessionResumeStatus.Resumed, decision.Status);
    Assert.Equal("room-a", (await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());
}

[Fact]
public async Task Session_items_are_inaccessible_after_termination_even_when_terminal_resume_state_is_retained()
{
    var directory = new InMemoryGameSessionRegistry();
    var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    await directory.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);

    await directory.MarkSessionTerminatedAsync(
        session,
        new SessionTerminationNotice(SessionTerminationReason.Policy, "removed"),
        keepForResume: true,
        TestContext.Current.CancellationToken);

    Assert.Null(await directory.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken));
    Assert.Equal(0, (await directory.GetSessionItemsAsync(session, TestContext.Current.CancellationToken)).Count);
}
```

- [ ] **Step 2: Run registry tests to verify they fail**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Session_items_"
```

Expected: FAIL because registry item methods do not exist.

- [ ] **Step 3: Add methods to `IGameSessionRegistry`**

Add after `GetCurrentSessionAsync` in `src/Lakona.Game.Server/Sessions/IGameSessionRegistry.cs`:

```csharp
ValueTask SetSessionItemAsync(
    GameSessionKey session,
    string key,
    GameSessionItemValue value,
    CancellationToken cancellationToken = default);

ValueTask<GameSessionItemValue?> GetSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default);

ValueTask<GameSessionItems> GetSessionItemsAsync(
    GameSessionKey session,
    CancellationToken cancellationToken = default);

ValueTask RemoveSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement registry storage and validation**

In `src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs`:

1. Add a key-length constant near fields:

```csharp
private const int MaxSessionItemKeyLength = 128;
```

2. Add public methods after `GetCurrentSessionAsync`:

```csharp
public ValueTask SetSessionItemAsync(
    GameSessionKey session,
    string key,
    GameSessionItemValue value,
    CancellationToken cancellationToken = default)
{
    ValidateSession(session);
    ValidateSessionItemKey(key);
    ValidateSessionItemValue(value);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
        var state = GetMutableActiveState(session);
        state.Items[key] = value;
    }

    return default;
}

public ValueTask<GameSessionItemValue?> GetSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default)
{
    ValidateSession(session);
    ValidateSessionItemKey(key);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
        if (!_sessions.TryGetValue(session, out var state) || state.Termination is not null)
        {
            return new ValueTask<GameSessionItemValue?>((GameSessionItemValue?)null);
        }

        return new ValueTask<GameSessionItemValue?>(
            state.Items.TryGetValue(key, out var value)
                ? value
                : (GameSessionItemValue?)null);
    }
}

public ValueTask<GameSessionItems> GetSessionItemsAsync(
    GameSessionKey session,
    CancellationToken cancellationToken = default)
{
    ValidateSession(session);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
        if (!_sessions.TryGetValue(session, out var state) || state.Termination is not null || state.Items.Count == 0)
        {
            return new ValueTask<GameSessionItems>(GameSessionItems.Empty);
        }

        return new ValueTask<GameSessionItems>(new GameSessionItems(state.Items));
    }
}

public ValueTask RemoveSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default)
{
    ValidateSession(session);
    ValidateSessionItemKey(key);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
        var state = GetMutableActiveState(session);
        state.Items.Remove(key);
    }

    return default;
}
```

3. Add helpers before `ValidateSession`:

```csharp
private SessionState GetMutableActiveState(GameSessionKey session)
{
    if (!_sessions.TryGetValue(session, out var state))
    {
        throw new InvalidOperationException($"Game session '{session}' does not exist.");
    }

    if (state.Termination is not null)
    {
        throw new InvalidOperationException($"Game session '{session}' is terminated.");
    }

    return state;
}

private static void ValidateSessionItemKey(string key)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    if (key.Length > MaxSessionItemKeyLength)
    {
        throw new ArgumentOutOfRangeException(nameof(key), $"Session item key length must be {MaxSessionItemKeyLength} characters or fewer.");
    }
}

private static void ValidateSessionItemValue(GameSessionItemValue value)
{
    if (!value.IsDefined)
    {
        throw new ArgumentOutOfRangeException(nameof(value), "Session item value must be a supported scalar value.");
    }
}
```

4. Clear items in `MarkSessionTerminatedAsync` after `state.Callbacks.Clear();`:

```csharp
state.Items.Clear();
```

5. Add item storage to `SessionState`:

```csharp
public Dictionary<string, GameSessionItemValue> Items { get; } = new(StringComparer.Ordinal);
```

- [ ] **Step 5: Add high-level server API methods**

In `src/Lakona.Game.Server/ILakonaGameServer.cs`, add public methods after `BindCurrentSessionAsync`:

```csharp
ValueTask SetSessionItemAsync(
    GameSessionKey session,
    string key,
    GameSessionItemValue value,
    CancellationToken cancellationToken = default);

ValueTask<GameSessionItemValue?> GetSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default);

ValueTask<GameSessionItems> GetSessionItemsAsync(
    GameSessionKey session,
    CancellationToken cancellationToken = default);

ValueTask RemoveSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default);
```

Add XML comments that state values are server-side local metadata, not durable business state, and are cleared on termination/expiration.

In `src/Lakona.Game.Server/DefaultLakonaGameServer.cs`, add forwarding methods after `BindCurrentSessionAsync`:

```csharp
public ValueTask SetSessionItemAsync(
    GameSessionKey session,
    string key,
    GameSessionItemValue value,
    CancellationToken cancellationToken = default)
{
    return _sessions.SetSessionItemAsync(session, key, value, cancellationToken);
}

public ValueTask<GameSessionItemValue?> GetSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default)
{
    return _sessions.GetSessionItemAsync(session, key, cancellationToken);
}

public ValueTask<GameSessionItems> GetSessionItemsAsync(
    GameSessionKey session,
    CancellationToken cancellationToken = default)
{
    return _sessions.GetSessionItemsAsync(session, cancellationToken);
}

public ValueTask RemoveSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default)
{
    return _sessions.RemoveSessionItemAsync(session, key, cancellationToken);
}
```

- [ ] **Step 6: Run registry tests**

Before running tests, update these `ILakonaGameServer` fakes to compile with the new interface methods:

- `tests/Lakona.Game.Server.Tests/GameSessionLifecycleBridgeTests.cs`, nested `SnapshotGameServer`
- `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixTests.cs`, nested `TestGameServer`
- `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarSessionLifecycleTests.cs`, nested `TestGameServer`

Use this implementation shape in fakes that do not need real item storage:

```csharp
public ValueTask SetSessionItemAsync(
    GameSessionKey session,
    string key,
    GameSessionItemValue value,
    CancellationToken cancellationToken = default)
{
    return default;
}

public ValueTask<GameSessionItemValue?> GetSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default)
{
    return new ValueTask<GameSessionItemValue?>((GameSessionItemValue?)null);
}

public ValueTask<GameSessionItems> GetSessionItemsAsync(
    GameSessionKey session,
    CancellationToken cancellationToken = default)
{
    return new ValueTask<GameSessionItems>(GameSessionItems.Empty);
}

public ValueTask RemoveSessionItemAsync(
    GameSessionKey session,
    string key,
    CancellationToken cancellationToken = default)
{
    return default;
}
```

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~GameSessionRegistryTests"
```

Expected: PASS.

- [ ] **Step 7: Commit registry API**

```powershell
git add src/Lakona.Game.Server/Sessions/IGameSessionRegistry.cs src/Lakona.Game.Server/Sessions/InMemoryGameSessionRegistry.cs src/Lakona.Game.Server/ILakonaGameServer.cs src/Lakona.Game.Server/DefaultLakonaGameServer.cs tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs
git commit -m "Add game session item registry API"
```

## Task 3: Expose Immutable Items In Hotfix Calls

**Files:**
- Modify: `src/Lakona.Game.Server/Hotfix/HotfixServiceCall.cs`
- Modify: `src/Lakona.Game.Server/Hotfix/HotfixLifecycleCall.cs`
- Modify: `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs`
- Modify: `tests/Lakona.Game.Server.Hotfix.Tests/TestHotfixServiceCall.cs`

- [ ] **Step 1: Write failing hotfix call contract tests**

Update `Hotfix_service_call_exposes_current_session_constructor_contract` in `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs` to assert the new property and constructor signatures:

```csharp
var currentSessionItemsProperty = typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<>)
    .GetProperty("CurrentSessionItems");

Assert.NotNull(currentSessionItemsProperty);
Assert.Equal(
    typeof(Lakona.Game.Server.Sessions.GameSessionItems),
    currentSessionItemsProperty.PropertyType);
Assert.NotNull(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object>).GetConstructor([
    typeof(object),
    typeof(string),
    typeof(Lakona.Game.Server.Sessions.GameSessionKey?),
    typeof(Lakona.Game.Server.Sessions.GameSessionItems),
    typeof(IServiceProvider),
    typeof(Lakona.Game.Server.Actors.IActorRuntime),
    typeof(Lakona.Game.Server.ILakonaGameServer)
]));
Assert.NotNull(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object, object>).GetConstructor([
    typeof(object),
    typeof(string),
    typeof(object),
    typeof(Lakona.Game.Server.Sessions.GameSessionKey?),
    typeof(Lakona.Game.Server.Sessions.GameSessionItems),
    typeof(IServiceProvider),
    typeof(Lakona.Game.Server.Actors.IActorRuntime),
    typeof(Lakona.Game.Server.ILakonaGameServer)
]));
```

Keep the existing assertions for old constructors so direct callers remain source-compatible.

Add this call-level behavior test in the same file. It proves the dispatch snapshot stays immutable even when the same call mutates the registry through `call.GameServer`:

```csharp
[Fact]
public async Task Hotfix_service_call_current_session_items_are_immutable_for_one_call()
{
    var sessions = new InMemoryGameSessionRegistry();
    var session = await sessions.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
    await sessions.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);
    var snapshot = await sessions.GetSessionItemsAsync(session, TestContext.Current.CancellationToken);
    var services = new ServiceCollection().AddLakonaGameServerActors().BuildServiceProvider();
    var call = new HotfixServiceCall<object>(
        new object(),
        "connection-a",
        session,
        snapshot,
        services,
        services.GetRequiredService<IActorRuntime>(),
        new RegistryBackedGameServer(sessions));

    await call.GameServer.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-b"), TestContext.Current.CancellationToken);

    Assert.Equal("room-a", call.CurrentSessionItems.GetString("roomId"));
    Assert.Equal("room-b", (await call.GameServer.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());
}
```

Add these usings if the file does not already contain them:

```csharp
using Lakona.Game.Abstractions;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
```

Add a nested `RegistryBackedGameServer` helper to `HotfixGeneratorTests`. The four session-item methods must delegate to `IGameSessionRegistry`; every other `ILakonaGameServer` member should throw `NotSupportedException` so the test cannot accidentally depend on unrelated behavior:

```csharp
private sealed class RegistryBackedGameServer : ILakonaGameServer
{
    private readonly IGameSessionRegistry _sessions;

    public RegistryBackedGameServer(IGameSessionRegistry sessions)
    {
        _sessions = sessions;
    }

    public ValueTask SetSessionItemAsync(GameSessionKey session, string key, GameSessionItemValue value, CancellationToken cancellationToken = default)
    {
        return _sessions.SetSessionItemAsync(session, key, value, cancellationToken);
    }

    public ValueTask<GameSessionItemValue?> GetSessionItemAsync(GameSessionKey session, string key, CancellationToken cancellationToken = default)
    {
        return _sessions.GetSessionItemAsync(session, key, cancellationToken);
    }

    public ValueTask<GameSessionItems> GetSessionItemsAsync(GameSessionKey session, CancellationToken cancellationToken = default)
    {
        return _sessions.GetSessionItemsAsync(session, cancellationToken);
    }

    public ValueTask RemoveSessionItemAsync(GameSessionKey session, string key, CancellationToken cancellationToken = default)
    {
        return _sessions.RemoveSessionItemAsync(session, key, cancellationToken);
    }

    public ValueTask<GameSessionKey> StartSessionAsync(string ownerKey, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public ValueTask<GameSessionKey> StartSessionAsync<TCallback>(string ownerKey, string connectionId, TCallback callback, CancellationToken cancellationToken = default)
        where TCallback : class
    {
        throw new NotSupportedException();
    }

    public ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(GameSessionResumeRequest request, string connectionId, TCallback callback, CancellationToken cancellationToken = default)
        where TCallback : class
    {
        throw new NotSupportedException();
    }

    public ValueTask BindSessionAsync<TCallback>(GameSessionKey session, string connectionId, TCallback callback, CancellationToken cancellationToken = default)
        where TCallback : class
    {
        throw new NotSupportedException();
    }

    public ValueTask BindCurrentSessionAsync<TCallback>(string connectionId, TCallback callback, CancellationToken cancellationToken = default)
        where TCallback : class
    {
        throw new NotSupportedException();
    }

    public ValueTask MarkSessionDisconnectedAsync(GameSessionKey session, string? connectionId = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public ValueTask<TCallback?> GetCallbackAsync<TCallback>(GameSessionKey session, CancellationToken cancellationToken = default)
        where TCallback : class
    {
        throw new NotSupportedException();
    }

    public ValueTask TerminateSessionAsync(GameSessionKey session, SessionTerminationReason reason, string? message = null, SessionTerminationOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Write failing generator output assertions**

In `Generator_emits_hotfix_rpc_service_proxy_for_callback_contract` and `Generator_emits_required_contracts_without_manual_builder_extension`, add assertions:

```csharp
Assert.Contains("var currentSessionItems = currentSession is { } sessionKey", result.GeneratedSource);
Assert.Contains("GetSessionItemsAsync(sessionKey, global::System.Threading.CancellationToken.None)", result.GeneratedSource);
Assert.Contains("currentSessionItems,", result.GeneratedSource);
```

- [ ] **Step 3: Run generator tests to verify failure**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-restore --filter "FullyQualifiedName~Hotfix_service_call_exposes_current_session_constructor_contract|FullyQualifiedName~Generator_emits"
```

Expected: FAIL because `CurrentSessionItems` and generated snapshot lookup do not exist.

- [ ] **Step 4: Update `HotfixServiceCall` constructors**

In `src/Lakona.Game.Server/Hotfix/HotfixServiceCall.cs`, update constructors so the existing overload delegates to a new overload with `GameSessionItems.Empty`, and add the property:

```csharp
public HotfixServiceCall(
    TRequest request,
    string connectionId,
    GameSessionKey? currentSession,
    IServiceProvider services,
    IActorRuntime actors,
    ILakonaGameServer gameServer)
    : this(request, connectionId, currentSession, GameSessionItems.Empty, services, actors, gameServer)
{
}

public HotfixServiceCall(
    TRequest request,
    string connectionId,
    GameSessionKey? currentSession,
    GameSessionItems currentSessionItems,
    IServiceProvider services,
    IActorRuntime actors,
    ILakonaGameServer gameServer)
{
    Request = request;
    ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
    CurrentSession = currentSession;
    CurrentSessionItems = currentSessionItems ?? throw new ArgumentNullException(nameof(currentSessionItems));
    Services = services ?? throw new ArgumentNullException(nameof(services));
    Actors = actors ?? throw new ArgumentNullException(nameof(actors));
    GameServer = gameServer ?? throw new ArgumentNullException(nameof(gameServer));
}

public GameSessionItems CurrentSessionItems { get; }
```

For `HotfixServiceCall<TRequest, TCallback>`, add the matching overload:

```csharp
public HotfixServiceCall(
    TRequest request,
    string connectionId,
    TCallback callback,
    GameSessionKey? currentSession,
    GameSessionItems currentSessionItems,
    IServiceProvider services,
    IActorRuntime actors,
    ILakonaGameServer gameServer)
    : base(request, connectionId, currentSession, currentSessionItems, services, actors, gameServer)
{
    Callback = callback ?? throw new ArgumentNullException(nameof(callback));
}
```

Keep existing constructors delegating into the new overload with `GameSessionItems.Empty`.

- [ ] **Step 5: Keep lifecycle calls empty**

In `src/Lakona.Game.Server/Hotfix/HotfixLifecycleCall.cs`, no behavior change is needed if the base constructor without items still delegates to empty. If compilation requires an explicit call, use:

```csharp
: base(request, connectionId, currentSession: null, GameSessionItems.Empty, services, actors, gameServer)
```

- [ ] **Step 6: Update hotfix test stub**

In `tests/Lakona.Game.Server.Hotfix.Tests/TestHotfixServiceCall.cs`, add a simple property to the stub:

```csharp
public object CurrentSessionItems { get; } = new();
```

If tests reference the real `GameSessionItems` type from this project, prefer:

```csharp
public Lakona.Game.Server.Sessions.GameSessionItems CurrentSessionItems { get; } =
    Lakona.Game.Server.Sessions.GameSessionItems.Empty;
```

- [ ] **Step 7: Update generator snapshot lookup**

In `src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs`, replace the direct registry call block inside `AppendRpcProxyMethod` with a local `sessions` variable and a current item snapshot:

```csharp
builder.AppendLine("        var sessions = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions");
builder.AppendLine("            .GetRequiredService<global::Lakona.Game.Server.Sessions.IGameSessionRegistry>(snapshot.Services);");
builder.AppendLine("        var currentSession = await sessions");
builder.AppendLine("            .GetCurrentSessionAsync(_connectionId, global::System.Threading.CancellationToken.None)");
builder.AppendLine("            .ConfigureAwait(false);");
builder.AppendLine("        var currentSessionItems = currentSession is { } sessionKey");
builder.AppendLine("            ? await sessions.GetSessionItemsAsync(sessionKey, global::System.Threading.CancellationToken.None).ConfigureAwait(false)");
builder.AppendLine("            : global::Lakona.Game.Server.Sessions.GameSessionItems.Empty;");
```

Then pass `currentSessionItems` immediately after `currentSession` in the generated constructor call:

```csharp
builder.AppendLine("                currentSession,");
builder.AppendLine("                currentSessionItems,");
```

- [ ] **Step 8: Run hotfix generator and hotfix tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-restore --filter "FullyQualifiedName~Hotfix_service_call_exposes_current_session_constructor_contract|FullyQualifiedName~Generator_emits"
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore --filter "FullyQualifiedName~HotfixDispatchTests"
```

Expected: PASS.

- [ ] **Step 9: Commit hotfix dispatch integration**

```powershell
git add src/Lakona.Game.Server/Hotfix/HotfixServiceCall.cs src/Lakona.Game.Server/Hotfix/HotfixLifecycleCall.cs src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixGeneratorTests.cs tests/Lakona.Game.Server.Hotfix.Tests/TestHotfixServiceCall.cs
git commit -m "Pass session item snapshots to hotfix services"
```

## Task 4: Migrate Agar Realtime Input Path

**Files:**
- Modify: `samples/Game.Unity.Agar/Server/Hotfix/Services/BattleService.cs`
- Test: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixTests.cs` or new `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarRealtimeSessionItemTests.cs`

- [ ] **Step 1: Inspect current user diff before editing**

Run:

```powershell
git diff -- samples/Game.Unity.Agar/Server/Hotfix/Services/BattleService.cs
```

Expected: the existing user change from `.Get(new RoomId(req.RoomId))` to `.Local(new RoomId(req.RoomId))` remains. Preserve it.

- [ ] **Step 2: Add a source-scan test for the frame path**

Create `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarRealtimeSessionItemTests.cs`:

```csharp
using Xunit;

namespace BusinessLogic.Tests;

public sealed class AgarRealtimeSessionItemTests
{
    [Fact]
    public void Battle_input_path_uses_session_items_instead_of_user_actor_snapshot()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Hotfix", "Services", "BattleService.cs");
        var text = File.ReadAllText(path);
        var methodStart = text.IndexOf("public async ValueTask SubmitInputAsync", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodEnd = text.IndexOf("private bool IsLocalRuntimeOwner", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);
        var body = text[methodStart..methodEnd];

        Assert.Contains("call.CurrentSessionItems.GetString(RoomIdSessionItemKey)", body, StringComparison.Ordinal);
        Assert.Contains("call.CurrentSessionItems.GetString(RealtimeSessionIdSessionItemKey)", body, StringComparison.Ordinal);
        Assert.Contains("call.CurrentSessionItems.GetInt64(RealtimeSessionGenerationSessionItemKey)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSnapshotAsync(new PlayerSessionSnapshotRequest())", body, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "Lakona.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
```

- [ ] **Step 3: Run source-scan test to verify failure**

Run:

```powershell
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore --filter "FullyQualifiedName~Battle_input_path_uses_session_items"
```

Expected: FAIL because `SubmitInputAsync` still queries `UserActor`.

- [ ] **Step 4: Update `BattleService` constants and attach path**

In `samples/Game.Unity.Agar/Server/Hotfix/Services/BattleService.cs`, add constants inside the class:

Add this using at the top of the file because the migration uses `GameSessionItemValue`:

```csharp
using Lakona.Game.Server.Sessions;
```

```csharp
private const string RoomIdSessionItemKey = "roomId";
private const string MatchIdSessionItemKey = "matchId";
private const string RealtimeSessionIdSessionItemKey = "realtimeSessionId";
private const string RealtimeSessionGenerationSessionItemKey = "realtimeSessionGeneration";
```

Capture the `_rooms.Local(new RoomId(req.RoomId)).SetReadyAsync(...)` result and reject failures before writing items:

```csharp
var ready = await _rooms
    .Local(new RoomId(req.RoomId))
    .SetReadyAsync(new RoomPlayerReadyRequest
    {
        UserId = req.PlayerId,
        RoomId = req.RoomId,
        IsReady = true,
        RealtimeSessionId = realtimeSession.SessionId,
        RealtimeSessionGeneration = realtimeSession.Generation,
        UpdatedAtUtc = DateTime.UtcNow
    }).ConfigureAwait(false);

if (!ready.Succeeded)
{
    await call.GameServer
        .TerminateSessionAsync(
            realtimeSession,
            SessionTerminationReason.Policy,
            "Realtime room attach rejected.")
        .ConfigureAwait(false);
    return new RealtimeAttachReply
    {
        Code = 4,
        Message = ready.Message
    };
}
```

After that success check, write the items:

```csharp
await call.GameServer
    .SetSessionItemAsync(realtimeSession, RoomIdSessionItemKey, GameSessionItemValue.FromString(req.RoomId))
    .ConfigureAwait(false);
await call.GameServer
    .SetSessionItemAsync(realtimeSession, MatchIdSessionItemKey, GameSessionItemValue.FromString(req.MatchId))
    .ConfigureAwait(false);
await call.GameServer
    .SetSessionItemAsync(realtimeSession, RealtimeSessionIdSessionItemKey, GameSessionItemValue.FromString(realtimeSession.SessionId))
    .ConfigureAwait(false);
await call.GameServer
    .SetSessionItemAsync(realtimeSession, RealtimeSessionGenerationSessionItemKey, GameSessionItemValue.FromInt64(realtimeSession.Generation))
    .ConfigureAwait(false);
```

The writes must stay after the room-ready actor call. If a future edit adds any failing attach step after these writes, it must remove these items or move the writes later.

- [ ] **Step 5: Update `SubmitInputAsync` to read session items**

Replace the user actor snapshot block in `SubmitInputAsync`:

```csharp
var roomId = call.CurrentSessionItems.GetString(RoomIdSessionItemKey);
var realtimeSessionId = call.CurrentSessionItems.GetString(RealtimeSessionIdSessionItemKey);
var realtimeSessionGeneration = call.CurrentSessionItems.GetInt64(RealtimeSessionGenerationSessionItemKey);
if (string.IsNullOrWhiteSpace(roomId) ||
    string.IsNullOrWhiteSpace(realtimeSessionId) ||
    realtimeSessionGeneration is null)
{
    return;
}

await _rooms
    .Get(new RoomId(roomId))
    .SubmitInputAsync(new RoomInputSubmitRequest
    {
        RoomId = roomId,
        UserId = playerId,
        RealtimeSessionId = realtimeSessionId,
        RealtimeSessionGeneration = realtimeSessionGeneration.Value,
        Input = req,
        SubmittedAtUtc = DateTime.UtcNow
    })
    .ConfigureAwait(false);
```

Do not use cached node ids. Keep generated actor route lookup through `_rooms.Get(new RoomId(roomId))`.

- [ ] **Step 6: Update room input contract and stale-session validation**

In `samples/Game.Unity.Agar/Shared/State/RoomContracts.cs`, add fields to `RoomInputSubmitRequest`:

```csharp
public string RealtimeSessionId { get; set; } = "";

public long RealtimeSessionGeneration { get; set; }
```

In `samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs`, update `SubmitInputAsync` after the player lookup and connected checks to reject stale input:

```csharp
if (!string.Equals(player.RealtimeSessionId, request.RealtimeSessionId, StringComparison.Ordinal) ||
    player.RealtimeSessionGeneration != request.RealtimeSessionGeneration)
{
    return default;
}
```

- [ ] **Step 7: Add stale input source scans**

Add these tests to `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarRealtimeSessionItemTests.cs`:

```csharp
[Fact]
public void Room_input_contract_carries_realtime_session_identity()
{
    var root = FindRepositoryRoot();
    var path = Path.Combine(root, "samples", "Game.Unity.Agar", "Shared", "State", "RoomContracts.cs");
    var text = File.ReadAllText(path);
    var typeStart = text.IndexOf("public sealed class RoomInputSubmitRequest", StringComparison.Ordinal);
    Assert.True(typeStart >= 0);
    var typeEnd = text.IndexOf("public sealed class RoomSettlementEntry", typeStart, StringComparison.Ordinal);
    Assert.True(typeEnd > typeStart);
    var typeBody = text[typeStart..typeEnd];

    Assert.Contains("public string RealtimeSessionId { get; set; }", typeBody, StringComparison.Ordinal);
    Assert.Contains("public long RealtimeSessionGeneration { get; set; }", typeBody, StringComparison.Ordinal);
}

[Fact]
public void Room_input_path_rejects_stale_realtime_session_identity()
{
    var root = FindRepositoryRoot();
    var path = Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Hotfix", "State", "Rooms", "RoomBehavior.cs");
    var text = File.ReadAllText(path);
    var methodStart = text.IndexOf("public static ValueTask SubmitInputAsync", StringComparison.Ordinal);
    Assert.True(methodStart >= 0);
    var methodEnd = text.IndexOf("public static async ValueTask RunTickAsync", methodStart, StringComparison.Ordinal);
    Assert.True(methodEnd > methodStart);
    var body = text[methodStart..methodEnd];

    Assert.Contains("player.RealtimeSessionId, request.RealtimeSessionId, StringComparison.Ordinal", body, StringComparison.Ordinal);
    Assert.Contains("player.RealtimeSessionGeneration != request.RealtimeSessionGeneration", body, StringComparison.Ordinal);
    Assert.Contains("return default;", body, StringComparison.Ordinal);
}
```

- [ ] **Step 8: Run Agar focused test**

Run:

```powershell
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore --filter "FullyQualifiedName~Battle_input_path_uses_session_items"
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore --filter "FullyQualifiedName~Room_input_contract_carries_realtime_session_identity|FullyQualifiedName~Room_input_path_rejects_stale_realtime_session_identity"
```

Expected: PASS.

- [ ] **Step 9: Commit Agar migration**

Use patch staging for `BattleService.cs` so the existing user-owned `.Get` to `.Local` change is not accidentally claimed unless the user explicitly wants it in this commit:

```powershell
git add -p samples/Game.Unity.Agar/Server/Hotfix/Services/BattleService.cs
git add samples/Game.Unity.Agar/Shared/State/RoomContracts.cs samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarRealtimeSessionItemTests.cs
git commit -m "Use session items for Agar realtime input routing"
```

## Task 5: Durable Documentation, Version Bump, And Guard Scans

**Files:**
- Modify: `docs/session.md`
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Test: `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs` or source-scan test file if added

- [ ] **Step 1: Move durable session item rules into `docs/session.md`**

In `docs/session.md`, add a subsection under `Business Session State` titled `Session Items` with this content:

```markdown
### Session Items

Game sessions may carry server-side session items for latency-sensitive cached
metadata. Items are framework session metadata, not durable business state.
They may store only scalar values supported by `GameSessionItemValue`: string,
Int64, and Boolean.

Session items are valid for values already validated by authoritative
business state, such as `roomId`, `matchId`, `sessionKind`, or membership
generation. They must not store callbacks, transport objects, DI services,
actor instances or refs, hotfix-defined class instances, mutable collections,
durable player data, or room membership authority.

Session items are created empty with a `GameSessionKey`, preserved across
disconnect and resume for the same session generation, cleared and inaccessible
on termination including terminal-state retention for resume, and removed when
disconnected sessions expire. They are never serialized to clients or shared
RPC DTOs.

Hotfix service calls receive `CurrentSessionItems` as an immutable per-dispatch
snapshot captured before the hotfix method runs. Mutating items through
`ILakonaGameServer` does not update that snapshot; code that needs a fresh value
in the same call must explicitly call `GetSessionItemAsync`.

Cached items must not bypass route freshness. A cached room id may choose the
actor key, but generated actor selectors should still resolve placement unless
a separate node lease or epoch design exists.
```

- [ ] **Step 2: Add source scan for forbidden exposure**

Add this test to `tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs` or a new source scan test file:

```csharp
[Fact]
public void Session_item_types_do_not_leak_to_client_or_shared_contract_projects()
{
    var root = FindRepositoryRoot();
    var forbiddenRoots = new[]
    {
        Path.Combine(root, "src", "Lakona.Game.Abstractions"),
        Path.Combine(root, "src", "Lakona.Game.Client"),
        Path.Combine(root, "src", "Lakona.Rpc.Client")
    };

    var offenders = forbiddenRoots
        .Where(Directory.Exists)
        .SelectMany(static path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
        .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Where(path => File.ReadAllText(path).Contains("GameSessionItem", StringComparison.Ordinal))
        .Select(path => Path.GetRelativePath(root, path))
        .Order(StringComparer.Ordinal)
        .ToArray();

    Assert.Empty(offenders);
}
```

If `FindRepositoryRoot` does not exist in the test file, add the helper from Task 4.

- [ ] **Step 3: Bump `Lakona.Game.Server` package version**

In `src/Lakona.Game.Server/Lakona.Game.Server.csproj`, change:

```xml
<Version>0.9.4</Version>
```

to:

```xml
<Version>0.9.5</Version>
```

- [ ] **Step 4: Run documentation and guard checks**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~Session_item_types_do_not_leak"
rg -n "GameSessionItem" src/Lakona.Game.Abstractions src/Lakona.Game.Client src/Lakona.Rpc.Client samples/Game.Unity.Agar/Shared samples/Game.Unity.Agar/Client -g "*.cs"
rg -n "roomId|matchId|sessionKind|GameSessionItem" docs/session.md docs/superpowers/specs/2026-07-06-session-items-design.md
```

Expected:
- Source-scan test passes.
- `rg` finds no `GameSessionItem` in client/shared runtime packages.
- `docs/session.md` contains durable session item rules.

- [ ] **Step 5: Commit docs and version**

```powershell
git add docs/session.md src/Lakona.Game.Server/Lakona.Game.Server.csproj tests/Lakona.Game.Server.Tests/GameSessionRegistryTests.cs
git commit -m "Document session item lifecycle"
```

## Task 6: Final Validation

**Files:**
- No new files. Validate the complete integrated change.

- [ ] **Step 1: Run affected test projects**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Hotfix.Tests/Lakona.Game.Server.Hotfix.Tests.csproj --no-restore
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore
```

Expected: all pass.

- [ ] **Step 2: Run final hygiene checks**

Run:

```powershell
git diff --check
git status --short
```

Expected:
- `git diff --check` prints no whitespace errors.
- `git status --short` shows only intentional tracked changes if the task is not yet committed, or a clean tree after commits.

- [ ] **Step 3: Inspect final diff**

Run:

```powershell
git diff --stat HEAD~5..HEAD
git diff -- src/Lakona.Game.Server src/Lakona.Game.Server.Hotfix.Generators tests/Lakona.Game.Server.Tests tests/Lakona.Game.Server.Hotfix.Generators.Tests samples/Game.Unity.Agar docs/session.md
```

Expected: diff is scoped to session items, hotfix call context, Agar migration, tests, docs, and package version.

## Self-Review

- Spec coverage:
  - Scalar-only values: Task 1.
  - Registry lifecycle and termination clearing: Task 2.
  - Immutable hotfix dispatch snapshot: Task 3.
  - Agar high-frequency frame path: Task 4.
  - Durable `docs/session.md` rules and package version: Task 5.
  - Full validation: Task 6.
- Placeholder scan:
  - The plan uses exact file paths, concrete code snippets, and exact validation commands.
- Type consistency:
  - Uses `GameSessionItemKind`, `GameSessionItemValue`, `GameSessionItems`, `SetSessionItemAsync`, `GetSessionItemAsync`, `GetSessionItemsAsync`, and `RemoveSessionItemAsync` consistently across runtime, generator, and sample tasks.
