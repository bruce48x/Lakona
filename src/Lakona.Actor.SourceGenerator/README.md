# Lakona.Actor.SourceGenerator

`Lakona.Actor.SourceGenerator` generates typed `ActorSystem` spawn extension methods for public Lakona.Actor typed actors and actor client proxies for `[ActorClient]` interfaces.

It is intended to reduce repeated `SpawnAsync<TMessage>(...)` calls when your project defines actors that implement `IActor<TMessage>`.

The generator is a compile-time convenience layer. It must emit ordinary C# that calls the public Lakona.Actor runtime APIs, and it must not require runtime reflection, dynamic proxies, or dynamic method invocation.

Actors may optionally implement `IActorStarted<TMessage>` and `IActorStopping<TMessage>` lifecycle hooks. The spawn generator continues to target `IActor<TMessage>` and does not require lifecycle hooks.

This project is an internal compile-time project for the main `Lakona.Actor` package. It is not independently packable and should not be published as a standalone NuGet package.

## Install

Install the runtime package:

```xml
<ItemGroup>
  <PackageReference Include="Lakona.Actor" Version="0.5.1" />
</ItemGroup>
```

`Lakona.Actor` packages the generator as an analyzer asset, so applications do not need a separate source generator package reference and do not get extra runtime dependencies.

## Usage

Define a public typed actor:

```csharp
using Lakona.Actor;

public abstract record RoomMessage;

public sealed record JoinRoom(long PlayerId) : RoomMessage;

public sealed class RoomActor : IActor<RoomMessage>
{
    private readonly HashSet<long> players = new();

    public ValueTask OnMessage(ActorContext<RoomMessage> ctx, RoomMessage message)
    {
        if (message is JoinRoom join)
        {
            players.Add(join.PlayerId);
        }

        return ValueTask.CompletedTask;
    }
}
```

The generator creates a typed spawn extension method:

```csharp
using Lakona.Actor;

await using ActorSystem system = new();

ActorHandle<RoomMessage> room = await system.SpawnRoomActorAsync(new RoomActor());

await room.Ref.Send(new JoinRoom(10001));
```

You can also register the actor by name:

```csharp
ActorHandle<RoomMessage> room = await system.SpawnRoomActorAsync("room-1", new RoomActor());
```

## Generated Method Shape

For a public actor named `RoomActor` that implements `IActor<RoomMessage>`, the generator emits:

```csharp
ValueTask<ActorHandle<RoomMessage>> SpawnRoomActorAsync(this ActorSystem system, RoomActor actor, ActorSpawnOptions? options = null);

ValueTask<ActorHandle<RoomMessage>> SpawnRoomActorAsync(this ActorSystem system, string name, RoomActor actor, ActorSpawnOptions? options = null);
```

## Generation Rules

- The actor class must be `public`.
- The actor class must implement `Lakona.Actor.IActor<TMessage>`.
- Generic actor classes are ignored.
- The generated extension methods are emitted into the `Lakona.Actor` namespace.

## Actor Client Proxy

Mark a public interface with `[ActorClient]` to generate a small client wrapper:

```csharp
using Lakona.Actor;

[ActorClient]
public interface IRoomActorClient
{
    ValueTask Join(long playerId);

    ValueTask<int> GetPlayerCount();
}
```

The generator emits a public client message interface, public request records that implement it, and an `ActorRef<TMessage>` extension method:

```csharp
ActorHandle<RoomActorClientMessage> roomActor = await system.SpawnAsync<RoomActorClientMessage>(new RoomActor());
ActorCallOptions callOptions = new(
    QueueTimeout: TimeSpan.FromMilliseconds(50),
    ResponseTimeout: TimeSpan.FromSeconds(1));
IRoomActorClient room = roomActor.Ref.AsRoomActorClient(callOptions);

await room.Join(10001);
int count = await room.GetPlayerCount();
```

The generated wrapper lowers:

- `ValueTask` methods into `ActorRef<TMessage>.Send(...)`.
- `ValueTask<T>` methods into `ActorRef<TMessage>.Call<T>(..., callOptions)`.

The actor handles the generated request record types:

```csharp
internal sealed class RoomActor : IActor<RoomActorClientMessage>
{
    private readonly HashSet<long> players = new();

    public ValueTask OnMessage(ActorContext<RoomActorClientMessage> ctx, RoomActorClientMessage message)
    {
        switch (message)
        {
            case RoomActorClientJoinRequest join:
                players.Add(join.PlayerId);
                break;
            case RoomActorClientGetPlayerCountRequest:
                ctx.Respond(players.Count);
                break;
        }

        return ValueTask.CompletedTask;
    }
}
```

Current limitations:

- The interface must be `public`.
- Generic interfaces and generic methods are not supported.
- Method overloads are not supported.
- `ref`, `out`, and `in` parameters are not supported.
- Methods should return `ValueTask` or `ValueTask<T>`.

Unsupported shapes are reported as compile-time diagnostics instead of falling back to runtime reflection or dynamic dispatch:

| Rule | Severity | Description |
| --- | --- | --- |
| `ULA101` | Error | Reports non-public `[ActorClient]` interfaces. |
| `ULA102` | Error | Reports generic `[ActorClient]` interfaces. |
| `ULA103` | Error | Reports overloaded actor client methods. |
| `ULA104` | Error | Reports actor client methods that do not return `ValueTask` or `ValueTask<T>`. |
| `ULA105` | Error | Reports generic actor client methods. |
| `ULA106` | Error | Reports `ref`, `out`, or `in` parameters on actor client methods. |

## Diagnostics

The same analyzer asset also reports common unsafe actor usage:

| Rule | Severity | Description |
| --- | --- | --- |
| `ULA001` | Warning | Reports `ctx.Self.Call(...)` inside an actor because calling the current actor through its own mailbox can deadlock. |
| `ULA002` | Warning | Reports blocking waits such as `.Wait()`, `.Result`, `Task.WaitAll(...)`, `Task.WaitAny(...)`, `.GetAwaiter().GetResult()`, and `Thread.Sleep(...)` inside actor types because blocking can stall the mailbox. |
| `ULA003` | Warning | Reports discarded `ActorRef<TMessage>.Call<T>` results because request/response calls should be awaited, returned, or stored. |

## Design Direction

Source generation is the preferred path for making Lakona.Actor easier to use without increasing runtime cost. Future generated APIs should keep the same boundary:

- Generate static, IDE-visible C# source.
- Lower higher-level APIs into `ActorSystem`, `ActorRef<TMessage>`, `Send`, and `Call<T>`.
- Keep Roslyn references inside the generator project.
- Do not add runtime reflection to actor discovery, dispatch, generated proxy calls, or response binding.

The generator is distributed as a compile-time analyzer asset rather than as a runtime dependency.

## License

MIT
