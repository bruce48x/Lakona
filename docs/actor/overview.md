# Lakona.Actor Overview

Lakona.Actor is a lightweight actor runtime for .NET game servers, inspired by skynet's service model.

The core idea is deliberately small:

```text
actor = mailbox + state + message handler
```

One actor owns one mailbox. Messages enter that mailbox and are processed sequentially. Actor state is therefore normally written without locks, because only one actor turn is active for that actor at a time.

Lakona.Actor is not a distributed actor platform. It is a process-local runtime for long-lived stateful services such as session, room, match, world, or chat actors.

Recommended layering:

```text
Lakona.Actor        Process-local actor/mailbox runtime
Lakona.Rpc          Networking and RPC
Lakona.Game         Game server infrastructure and gameplay concepts
```

## Quick Start

Define a message family and an actor:

```csharp
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

Start the actor system and send a message:

```csharp
await using ActorSystem system = new();

ActorHandle<RoomMessage> room = await system.SpawnAsync<RoomMessage>(new RoomActor());

await room.Ref.Send(new JoinRoom(10001));
```

Use `Call<T>` when a response is required:

```csharp
public sealed record GetPlayerCount : RoomMessage;

public sealed class RoomActor : IActor<RoomMessage>
{
    private readonly HashSet<long> players = new();

    public ValueTask OnMessage(ActorContext<RoomMessage> ctx, RoomMessage message)
    {
        switch (message)
        {
            case JoinRoom join:
                players.Add(join.PlayerId);
                break;
            case GetPlayerCount:
                ctx.Respond(players.Count);
                break;
        }

        return ValueTask.CompletedTask;
    }
}

ActorCallOptions callOptions = new(
    QueueTimeout: TimeSpan.FromMilliseconds(50),
    ResponseTimeout: TimeSpan.FromSeconds(1));

int count = await room.Ref.Call<int>(new GetPlayerCount(), callOptions);
```

## Runtime Model

### Typed Actors

Lakona.Actor's public actor API is typed-only. Actors implement `IActor<TMessage>`, callers hold `ActorRef<TMessage>`, timers accept `TMessage`, and named actor lookup requires the expected message type. `ActorRef<TMessage>` is message-only; lifecycle and diagnostics stay on the `ActorHandle<TMessage>` returned by spawn or on `ActorSystem`.

Actors that handle multiple commands should use a shared message base type or interface, such as `RoomMessage`, and derive individual command records from it.

### Send

`Send` is fire-and-forget messaging. It is useful for inputs, notifications, broadcasts, ticks, and completion messages.

Bounded mailboxes apply backpressure. `Send` waits until capacity is available or the supplied cancellation token is cancelled. Use `TrySend` when a caller must fail fast instead of waiting.

### Call<T>

`Call<T>` is request/response messaging. It is useful for querying actor state, requesting computed results, or waiting for actor-side work to complete.

Calls use `ActorCallOptions` so queue backpressure and actor response latency have separate budgets. `QueueTimeout` controls how long the caller waits for mailbox capacity. `ResponseTimeout` starts after the request is queued and controls how long the caller waits for `ctx.Respond(...)`.

Call timeouts publish structured diagnostics with the caller actor, target actor, request type, queue timeout, response timeout, elapsed time, timeout reason, and actor call chain when the call originated from another actor.

### Timers

Timers are delivered as mailbox messages. They do not bypass the actor's sequential execution rule.

### Lifecycle

Actors may optionally implement:

- `IActorStarted<TMessage>` for startup work.
- `IActorStopping<TMessage>` for graceful stop work.

These hooks are local runtime hooks. Actor state still belongs to the actor, and follow-up work should enter through the mailbox.

Actors can be stopped after draining queued work. During explicit stop, the runtime closes the actor to new application messages, drains already queued messages, then runs `IActorStopping<TMessage>` as the final mailbox turn before completing the mailbox. Timeout overloads return whether that stop sequence completed within the drain timeout; if the timeout elapses, the actor remains `Draining` until the current work and final stop hook complete. System disposal is a cleanup path and does not run graceful stop hooks.

### Named Actors

Actors can be registered by name inside a local `ActorSystem` and resolved later by name. Lookup validates the expected message type.

## Safe Long-Running Work

Actor handlers should not run blocking or long CPU-bound work while holding actor state. Move that work outside the actor turn, then post a completion message back to the actor so state is updated through the mailbox:

```csharp
public sealed record StartJob(int Value);
public sealed record JobCompleted(int Result);

public sealed class WorkerActor : IActor<object>
{
    private int result;

    public ValueTask OnMessage(ActorContext<object> ctx, object message)
    {
        switch (message)
        {
            case StartJob job:
                _ = Task.Run(async () =>
                {
                    int computed = await RunSlowWork(job.Value);
                    await ctx.Self.Send(new JobCompleted(computed));
                });
                break;
            case JobCompleted completed:
                result = completed.Result;
                break;
        }

        return ValueTask.CompletedTask;
    }
}
```

Avoid blocking inside `OnMessage`, for example `Thread.Sleep(...)`, `Task.WaitAll(...)`, or `task.GetAwaiter().GetResult()`. Those patterns stall the mailbox and prevent the actor from processing the message that would make progress.

## Observability

Lakona.Actor exposes diagnostics through standard .NET APIs:

- `ActivitySource` for message dispatch tracing.
- Activity context propagation through `Send`, `Call<T>`, timers, and generated actor clients.
- `Meter` counters/gauges for accepted messages, rejected messages, processed messages, calls, timeouts, dead letters, and mailbox queue length.
- Slow message detection.
- Dead-letter publication.
- Call-timeout root-cause diagnostics.
- Observer error publication for diagnostic handler and message interceptor failures.

Diagnostic events expose message and request type names rather than payload objects. The runtime does not bind to a specific logger, metrics backend, or APM platform.

## Source Generation

`Lakona.Actor.SourceGenerator` generates typed spawn extension methods for public `IActor<TMessage>` implementations and typed actor client proxies for `[ActorClient]` interfaces.

Generated code is ordinary C# that calls `ActorSystem`, `ActorRef<TMessage>`, `Send`, and `Call<T>`. It does not require runtime reflection, dynamic proxies, or `MethodInfo.Invoke`.

The analyzer also warns about common unsafe actor usage:

- Actor self-calls through `ctx.Self.Call(...)`.
- Blocking waits inside actor types.
- Discarded `Call<T>` request results.
- Unsupported `[ActorClient]` interface shapes.

See [Lakona.Actor.SourceGenerator README](./src/Lakona.Actor.SourceGenerator/README.md) for generated API details.

## Scope

Lakona.Actor Core is a process-local actor/mailbox runtime. The following are outside scope:

- Cluster
- Remote Actor
- Virtual Actor
- Actor Persistence
- Event Sourcing
- Supervisor Tree
- MMO templates
- Gate / Realm / Map / AOI
- Unity integration
- Database abstractions
- ORM
- Network protocols
- Transport
- RPC
- Runtime reflection driven actor dispatch or proxy generation

These concerns belong in [Lakona.Game](https://github.com/bruce48x/Lakona.Game), [Lakona.Rpc](https://github.com/bruce48x/Lakona.Rpc), or application code.

## Documentation

- [Design Philosophy](docs/design-philosophy.md) — principles, tradeoffs, and rationale behind Lakona.Actor's design
- [Contributing](CONTRIBUTING.md) — project structure, conventions, and development guide for maintainers

## Contributing

Project structure, design constraints, test coverage, and development conventions for framework contributors are documented in [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

MIT
