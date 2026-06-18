# Agar RPC Service Split Design

Date: 2026-06-18
Status: approved for implementation planning

## Goal

Split the Agar sample's single `IPlayerService` RPC contract into three real
protocol surfaces:

- `ILoginService` for account login only.
- `IPlayerService` for control-plane player actions and control-plane
  notifications.
- `IBattleService` for realtime battle attach, input, and battle
  notifications.

The split must remove the current service-name aliasing where `login`,
`player`, and `battle` all bind the same generated `PlayerService` proxy.

## Non-Goals

Do not perform the larger Feature initialization refactor in this change.
`Program.cs` still has temporary service registration logic after this work.

Do not add `BindControlAsync`, `OpenSessionAsync`, `AttachCallbackAsync`, or any
other framework/session-binding method to the shared frontend/backend RPC
contract. Control callback binding is a server implementation detail.

Do not introduce hand-written endpoint service aliases through
`LakonaRpcServiceBinder`. Service names must come from generated binders for
the three shared RPC contract names.

Do not keep compatibility with the previous one-service wire shape. This is a
sample protocol cleanup, not a backward-compatible protocol migration.

## Current Problem

`samples/Game.Unity.Agar/Shared/Interfaces/IPlayerService.cs` currently declares
one service id and one callback contract for login, matchmaking, leaderboard,
reliable push ack, realtime attach, realtime input, logout, matchmaking status,
world state, player death, and match end.

`samples/Game.Unity.Agar/Server/App/Hosting/AgarRpcServiceBinders.cs` then
exposes multiple endpoint service names by binding the same generated
`PlayerService` proxy under `login` and `battle`. This makes `RpcServices` look
like three separate protocol surfaces while all calls still share one service
contract.

The callback contract also crosses transport boundaries. Matchmaking status is
a control-plane notification, while world state and battle events are realtime
notifications.

## Target Shared Protocol

Keep all DTOs in `samples/Game.Unity.Agar/Shared/Interfaces/IPlayerService.cs`
for this change unless the implementation naturally creates smaller files.
The important contract is the public shape below.

### ILoginService

`ILoginService` is account authentication only. It has no notification
contract.

```csharp
[RpcService(1)]
public interface ILoginService
{
    [RpcMethod(1)]
    ValueTask<LoginReply> LoginAsync(LoginRequest req);
}
```

`LoginAsync` returns account/session credentials through the existing
`LoginReply` DTO. `LoginReply.SessionId` and
`LoginReply.SessionGeneration` remain allowed because they are already part of
the sample's login result and reliable push ack flow.

`LoginAsync` must not require, receive, or bind a callback contract.

### IPlayerService

`IPlayerService` owns the control plane and its callback contract.

```csharp
[RpcService(2, NotificationContract = typeof(IControlCallback))]
public interface IPlayerService
{
    [RpcMethod(1)]
    ValueTask StartMatchmakingAsync(MatchmakingRequest req);

    [RpcMethod(2)]
    ValueTask CancelMatchmakingAsync(CancelMatchmakingRequest req);

    [RpcMethod(3)]
    ValueTask<ReliablePushAckReply> AckReliablePushAsync(ReliablePushAckRequest req);

    [RpcMethod(4)]
    ValueTask<LeaderboardReply> GetLeaderboardAsync(LeaderboardRequest req);

    [RpcMethod(5)]
    ValueTask LogoutAsync(LogoutRequest req);
}

[RpcNotificationContract(typeof(IPlayerService))]
public interface IControlCallback
{
    [RpcNotification(1)]
    void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus);
}
```

`IControlCallback` belongs to `IPlayerService`, not `ILoginService`.

`IPlayerService` must not declare a `BindControlAsync` method. The server binds
the control callback as an internal side effect when a real player operation
enters the `PlayerService` hotfix implementation.

### IBattleService

`IBattleService` owns the realtime plane and its callback contract.

```csharp
[RpcService(3, NotificationContract = typeof(IBattleCallback))]
public interface IBattleService
{
    [RpcMethod(1)]
    ValueTask<RealtimeAttachReply> AttachRealtimeAsync(RealtimeAttachRequest req);

    [RpcMethod(2)]
    ValueTask SubmitInputAsync(InputMessage req);
}

[RpcNotificationContract(typeof(IBattleService))]
public interface IBattleCallback
{
    [RpcNotification(1)]
    void OnWorldState(WorldState worldState);

    [RpcNotification(2)]
    void OnPlayerDead(PlayerDead deadEvent);

    [RpcNotification(3)]
    void OnMatchEnd(MatchEnd matchEnd);
}
```

`IBattleCallback` must not be used for control-plane matchmaking status.
`IControlCallback` must not be used for world state, player death, or match end.

## Service Names And Endpoint Configuration

Generated service names must be:

| Contract | Generated service name | Expected endpoint |
| --- | --- | --- |
| `ILoginService` | `login` | WebSocket control endpoint |
| `IPlayerService` | `player` | WebSocket control endpoint |
| `IBattleService` | `battle` | KCP realtime endpoint |

`samples/Game.Unity.Agar/Server/App/appsettings.gateway-1.json` should continue
to expose `RpcServices: [ "login", "player" ]`.

`samples/Game.Unity.Agar/Server/App/appsettings.battle-1.json` should continue
to expose `RpcServices: [ "battle" ]`.

`samples/Game.Unity.Agar/Server/App/appsettings.json` must expose `battle` on
the KCP endpoint instead of exposing `player` on both WebSocket and KCP.

Do not set `ApiName` to force these names. The names should come from the
contract type names.

## Server App Binding

Delete `samples/Game.Unity.Agar/Server/App/Hosting/AgarRpcServiceBinders.cs`.

`Program.cs` must use generated hotfix service registration:

```csharp
return await LakonaGameServer.RunAsync(args, server => server
    .AddServices((services, configuration) =>
    {
        // existing temporary registrations stay here for this change
    })
    .UseGeneratedHotfixServices());
```

Remove the manual registration of `GeneratedHotfixRequiredServiceContracts`
from `Program.cs`; `UseGeneratedHotfixServices()` owns that generated support
registration.

Do not add a new `HasRpcService(runtimeOptions, "battle")` branch as part of
this protocol split. Moving service registration into Features is a later
architecture task.

## Hotfix Services

Replace the single hotfix `PlayerService` implementation with one implementation
per contract:

- `LoginService` with `[HotfixService(typeof(ILoginService))]`.
- `PlayerService` with `[HotfixService(typeof(IPlayerService))]`.
- `BattleService` with `[HotfixService(typeof(IBattleService))]`.

The services may share private helper code, but each hotfix class must contain
only the methods declared by its contract.

### LoginService Behavior

`LoginService.LoginAsync` keeps the account login logic from the old
`PlayerService.LoginAsync`.

It must create or resume the server-side control session registration without a
callback. It must store enough server-local state for later `IPlayerService`
calls to resolve the current player from the RPC connection id.

`LoginService.LoginAsync` must not replay pending reliable pushes, because it
does not own `IControlCallback`.

### PlayerService Behavior

`PlayerService` owns control callback binding. It should use a private helper
with behavior equivalent to:

```csharp
private static async ValueTask<string?> EnsureControlCallbackBoundAsync<TRequest>(
    HotfixServiceCall<TRequest, IControlCallback> call)
```

The helper resolves the player id from `call.ConnectionId`, binds
`call.Callback` to the existing control session for that player when the
session exists, and returns the player id. If the connection is not associated
with a logged-in player, methods that require a player return without work or
return their existing invalid-request reply shape.

Pending reliable matchmaking pushes must be replayed after a successful new or
reconnected control callback binding. They must not be replayed repeatedly on
every player method when the same callback is already bound.

`GetLeaderboardAsync` may still return leaderboard data even when the
connection is not associated with a logged-in player. When the connection is
associated with a player, it should bind the control callback before returning.

### BattleService Behavior

`BattleService.AttachRealtimeAsync` keeps the realtime attach validation from
the old service but uses `HotfixServiceCall<RealtimeAttachRequest,
IBattleCallback>`.

`BattleService.SubmitInputAsync` replaces old `SubmitInput` and uses
`HotfixServiceCall<InputMessage, IBattleCallback>`. It must submit input only
through the realtime/battle service. The client must not fall back to
submitting gameplay input through `IPlayerService`.

## Server App Session Types

Replace old `IPlayerCallback` usage in stable server app code:

- `ReliableMatchmakingPublisher` uses `IControlCallback`.
- `RoomRuntime` uses `IBattleCallback`.
- `SessionRegistration.ControlCallback` is `IControlCallback?`.
- `SessionRegistration.RealtimeCallback` is `IBattleCallback?`.
- `SessionDirectory.GetControlCallbackAsync` returns `IControlCallback?`.
- Realtime callback lookup returns `IBattleCallback?`.

Do not keep a fallback that delivers battle notifications through
`IControlCallback`. Battle notifications require a realtime callback.

`SessionDirectory` must expose server-local APIs that separate login from
callback binding. These APIs are not shared RPC methods:

```csharp
public ValueTask<GameSessionKey> RegisterNewControlAsync(
    string playerId,
    string sessionToken,
    string connectionId,
    CancellationToken cancellationToken = default);

public ValueTask<SessionResumeDecision> ResumeControlAsync(
    string playerId,
    string sessionToken,
    string connectionId,
    CancellationToken cancellationToken = default);

public ValueTask<bool> BindControlCallbackAsync(
    string playerId,
    string connectionId,
    IControlCallback callback,
    CancellationToken cancellationToken = default);
```

`RegisterNewControlAsync` and `ResumeControlAsync` create or restore the
control session registration without storing a callback. They must keep the
connection-id lookup used by `PlayerSessionLifecycleObserver`.

`BindControlCallbackAsync` binds `IControlCallback` to the existing control
session through `IGameSessionDirectory.BindSessionAsync`. It returns `true`
only when the callback became newly available for the current control
connection. It returns `false` when the same control connection already has a
control callback bound. It does not create a new player session.

`PlayerService` uses the `true` result to decide whether to replay pending
reliable matchmaking pushes.

## Unity Client Behavior

`DotArenaGame` must implement both callback contracts:

```csharp
public sealed partial class DotArenaGame : MonoBehaviour, IControlCallback, IBattleCallback
```

The control connection must register only the control callback:

```csharp
var callbacks = new RpcClient.RpcNotificationBindings();
callbacks.Add((IControlCallback)callback);
```

The realtime connection must register only the battle callback:

```csharp
var callbacks = new RpcClient.RpcNotificationBindings();
callbacks.Add((IBattleCallback)callback);
```

`DotArenaNetworkSession` should store:

- `ILoginService? _loginService`
- `IPlayerService? _controlPlayerService`
- `IBattleService? _battleService`

Control login uses `Api.Shared.Login.LoginAsync`.
Control player actions use `Api.Shared.Player`.
Realtime attach and input use `Api.Shared.Battle`.

`SubmitInputAsync` must return without work when `_battleService` is null. It
must not fall back to `_controlPlayerService`.

`DisposeControlAsync(logout: true)` must call
`_controlPlayerService.LogoutAsync(new LogoutRequest())` when connected.

## Tests And Validation

The implementation should update or add focused tests that assert:

- `appsettings.gateway-1.json` exposes exactly `login` and `player`.
- `appsettings.battle-1.json` exposes exactly `battle`.
- `appsettings.json` exposes `login` and `player` on WebSocket and `battle` on
  KCP.
- Shared contracts contain `ILoginService`, `IPlayerService`,
  `IBattleService`, `IControlCallback`, and `IBattleCallback`.
- Shared contracts do not contain `IPlayerCallback`.
- Shared contracts do not contain `BindControlAsync`.
- `Server/App/Hosting/AgarRpcServiceBinders.cs` no longer exists.
- The server solution builds.

Recommended validation commands:

```powershell
dotnet build samples\Game.Unity.Agar\Server\Server.slnx
dotnet test samples\Game.Unity.Agar\tests\BusinessLogic.Tests\BusinessLogic.Tests.csproj --no-build
```

If the full solution is already built, the second command may use `--no-build`.
Otherwise run the test command without `--no-build`.
