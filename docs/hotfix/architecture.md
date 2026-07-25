# Hotfix Architecture

Lakona hotfix lets game behavior reload without replacing the stable server
host. Stable `Server.App` owns actor state types, contracts, host wiring, and
runtime integration. Reloadable `Server.Hotfix` owns services, actor behavior
methods, actor lifecycle hooks, timer callbacks, Application HTTP handlers, and
business rules.

## Boundaries

| Layer | Owns |
| --- | --- |
| `Shared` | RPC contracts, callback contracts, stable HTTP contracts, DTOs, named contract ids |
| `Server.App` | actor state shells, host configuration, stable runtime services, actor/timer DTOs, generated RPC and HTTP binders |
| `Server.Hotfix` | RPC and HTTP implementations, `[HotfixComponent]` helpers, `[HotfixBehaviorOf]` actor methods, `[ActorStart]`, `[ActorStop]`, timer callbacks |

Hotfix code is loaded through `HotfixManager`. Reload validation builds a
dispatch table, verifies required contracts, creates a candidate service
provider, and rolls back candidate-created actors if activation fails.

`Server.Hotfix` is a closed code assembly. Every user-defined class declares a
framework role; dependency-only helpers use `[HotfixComponent]` and are
automatically registered once per generation. DTOs, timer arguments, and
mutable state stay in stable assemblies. Pure static policy classes may remain
in Hotfix, but they may not own static fields, auto-properties, or events.

The stable host and collectible Hotfix load context share framework assemblies,
the entry assembly, and the assemblies that own generated required service
contracts. Assembly identity comes from the discovered contract `Type` objects;
the runtime must not guess project names such as `Shared`, `Server.App`, or
`State.Contracts`. This keeps custom contract assembly names valid while
preventing duplicate type identities across load contexts.

## Application HTTP

Application HTTP uses the same generation publication and lease model as
generated RPC binding:

```text
stable HTTP contract
  -> generated stable ASP.NET endpoint binder
  -> current Hotfix generation lease
  -> readonly generated HTTP call
  -> Hotfix handler
  -> stable response value
```

Stable code owns Kestrel, listener exposure, bounded request capture,
admission, tracing, and response writing. Hotfix owns product validation,
authorization decisions, idempotency policy, Actor calls, persistence
orchestration, and response semantics.

One request stays on one generation. Candidate validation rejects missing,
duplicate, and signature-mismatched HTTP handlers before publication, and the
previous generation remains alive until its in-flight requests drain.
`HttpContext`, request streams, response writers, and Hotfix-defined lazy
results must not escape into Hotfix behavior. Request deadlines cancel the
stable call token cooperatively; Hotfix handlers must observe cancellation
because executing .NET code cannot be aborted safely during generation unload.

Activation is process-local. Adjacent generations may coexist across nodes
during a rolling update, so stable cross-node contracts remain compatible and
the state-owning Actor makes authoritative mutation decisions. See
[Application HTTP](../http.md).

## Actor Lifecycle

Use explicit actor lifecycle attributes:

```csharp
[ActorStart]
public ValueTask StartAsync(MatchmakingActor self, ActorStartCall call)
{
    return self.StartTimerAsync(new MatchmakingTimerStartRequest(), call.CancellationToken);
}

[ActorStop]
public ValueTask StopAsync(MatchmakingActor self, ActorStopCall call)
{
    return self.StopTimerAsync(new MatchmakingTimerStopRequest(), call.CleanupCancellationToken);
}
```

`Lakona:StartupActors` selects which named actor startup declarations are
activated on a node. Node placement and route choice belong in code and actor
route policy, not in a separate component model.

## Timers

Hotfix timers use `LakonaTimer` from an active hotfix execution scope:

```csharp
await LakonaTimer.CreatePeriodicTimerAsync(
    static (MatchmakingTimerCallbacks callbacks) => callbacks.TickAsync,
    TimeSpan.Zero,
    TimeSpan.FromSeconds(1),
    new MatchmakingTimerArgs(),
    call.CancellationToken);
```

Timer callbacks should enter generated actor selectors or application services.
They should not hold transport callbacks, session callback objects, or mutable
global game state.
