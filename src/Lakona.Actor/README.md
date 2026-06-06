# Lakona.Actor

`Lakona.Actor` is a lightweight actor/mailbox runtime for .NET game servers.

It focuses on single-process service runtime scenarios where long-lived state objects should process messages sequentially without locks.

## Install

```xml
<ItemGroup>
  <PackageReference Include="Lakona.Actor" Version="0.5.1" />
</ItemGroup>
```

## Quick Start

Define a message family and an actor:

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

Spawn the actor and send messages:

```csharp
using Lakona.Actor;

await using ActorSystem system = new();

ActorHandle<RoomMessage> room = await system.SpawnAsync<RoomMessage>(new RoomActor());

await room.Ref.Send(new JoinRoom(10001));
```

## Request / Response

Use `Call<T>` when the sender needs a response:

```csharp
public sealed record GetPlayerCount : RoomMessage;

public sealed class RoomActor : IActor<RoomMessage>
{
    private readonly HashSet<long> players = new();

    public ValueTask OnMessage(ActorContext<RoomMessage> ctx, RoomMessage message)
    {
        if (message is GetPlayerCount)
        {
            ctx.Respond(players.Count);
        }

        return ValueTask.CompletedTask;
    }
}

ActorCallOptions callOptions = new(
    QueueTimeout: TimeSpan.FromMilliseconds(50),
    ResponseTimeout: TimeSpan.FromSeconds(1));

int count = await room.Ref.Call<int>(new GetPlayerCount(), callOptions);
```

Generated typed spawn extension methods are included with the `Lakona.Actor` package as compile-time source generator output.

Interfaces marked with `[ActorClient]` can also get generated client proxies that lower `ValueTask` methods into `Send` and `ValueTask<T>` methods into `Call<T>`.

The package also includes compile-time analyzer warnings for actor self-calls, blocking waits inside actor types, common blocking APIs such as `Thread.Sleep`, and discarded `Call<T>` request results.

## Core Features

- One actor owns one mailbox.
- Messages are processed sequentially inside each actor.
- `Send` supports fire-and-forget messaging.
- `Call<T>` supports request/response workflows with distinct queue and response timeouts through `ActorCallOptions`.
- `ActorRef<TMessage>` is message-only; `ActorHandle<TMessage>` owns lifecycle and diagnostics.
- Timer messages enter the actor mailbox and follow the same sequential execution rule.
- Optional `IActorStarted<TMessage>` and `IActorStopping<TMessage>` hooks support local startup and graceful stop work. During explicit stop, `IActorStopping<TMessage>` runs as the final mailbox turn after queued messages drain.
- Bounded mailbox capacity provides backpressure.
- The runtime does not preempt a running actor turn. Slow-message diagnostics report long handlers without allowing the mailbox to advance concurrently.
- `ActorState` enum (`Active` / `Draining` / `Dead`) exposed via `ActorHandle.GetState()` and `ActorSystem.GetActorState()`.
- `IActorMessageInterceptor` hooks before and after every message dispatch. Interceptor failures are reported through observer-error diagnostics and do not change actor message results.
- Circular actor call chains throw `InvalidOperationException` synchronously.
- Named actors can be registered and resolved inside an `ActorSystem`; lookup validates the expected message type.
- Mailbox metrics, slow message detection, dead letters, observer errors, and .NET `ActivitySource` tracing are available for diagnostics. Diagnostic events expose message/request type names rather than payload objects.

## Non-Goals

`Lakona.Actor` is not a distributed actor platform. It does not provide cluster, remote actor, virtual actor, persistence, event sourcing, supervisor tree, networking, RPC, database, ORM, Unity integration, or MMO template features.

Those concerns should live in higher-level infrastructure or application code.

## License

MIT
