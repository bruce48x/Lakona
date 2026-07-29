# Hotfix Architecture

Lakona hotfix lets game behavior reload without replacing the stable server
host. Stable `Server.App` owns actor state types, RPC contracts, host wiring,
and runtime integration. Reloadable `Server.Hotfix` owns services, actor
behavior methods, actor lifecycle hooks, timer callbacks, complete Application
HTTP declarations and handlers, and business rules.

## Boundaries

| Layer | Owns |
| --- | --- |
| `Shared` | RPC contracts, callback contracts, DTOs, named RPC contract ids |
| `Server.App` | actor state shells, host configuration, stable runtime services, actor/timer DTOs, generated RPC binders |
| `Server.Hotfix` | RPC implementations, complete HTTP services, `[HotfixComponent]` helpers, `[HotfixBehaviorOf]` actor methods, `[ActorStart]`, `[ActorStop]`, timer callbacks |

Generated `Server.App` grants internal access only to its paired
`Server.Hotfix` assembly. This is the deliberate application-level exception
to the normal no-friend rule: reloadable behavior must operate on internal
stable Actor state without publishing mutable state as application API. The
grant must not include framework packages, tests, or additional application
assemblies.

Hotfix code is loaded through `HotfixManager`. Reload validation builds a
dispatch table, verifies required contracts, creates a candidate service
provider, and activates publication participants inside a candidate generation
scope. The candidate becomes current only after every activation succeeds; a
failed activation rolls back candidate-created actors without exposing the
candidate to ordinary requests.

Stable-service access is intentionally layered. The current generation
provider owns reloadable dependencies and resolves first; the stable root
provider supplies unshadowed process-lifetime application and framework
services. Hotfix classes declare those dependencies through constructors, and
candidate activation rejects missing dependencies before publication. This
two-provider shape reflects two real lifetimes and must not be replaced by a
duplicate stable-service allow-list.

The generation runtime scope is a separate concern. It pins service resolution
and dispatch-table identity to the acquired generation so an in-flight call
does not switch generations during reload. Its ambient implementation does not
make stable root resolution accidental and is not removed by changing how
stable services are exposed.

`HotfixManager` owns every published generation through shutdown. Root-provider
disposal closes reload admission, waits for an in-progress reload and active
generation leases, retires the current dispatch table and service provider,
unloads its collectible load context, and releases the process dispatch
provider. The debug file watcher cancels and awaits reloads it has already
started before host shutdown continues.

`Server.Hotfix` is a closed code assembly. Every user-defined class declares a
framework role; dependency-only helpers use `[HotfixComponent]` and are
automatically registered once per generation. DTOs, timer arguments, and
mutable state stay in stable assemblies. Pure static policy classes may remain
in Hotfix, but they may not own static fields, auto-properties, or events.
It is the paired behavior assembly of `Server.App`, not an untrusted plugin or
capability-security boundary. Revisit explicit service export only if that
trust or isolation model changes.

The non-packable Hotfix abstractions assembly exposes a hidden
`ILakonaTimerBackend` and `LakonaTimerRuntime` cooperation interface because
their type identity must be shared with collectible Hotfix load contexts.
Game.Server implements that interface without receiving friend access to the
abstractions assembly. These types are framework integration support, not
application extension points.

The stable host and collectible Hotfix load context share framework assemblies,
the entry assembly, and the assemblies that own generated required service
contracts. Assembly identity comes from the discovered contract `Type` objects;
the runtime must not guess project names such as `Shared`, `Server.App`, or
`State.Contracts`. This keeps custom contract assembly names valid while
preventing duplicate type identities across load contexts.

## Application HTTP

Application HTTP uses the same generation publication and lease model as RPC
binding without requiring a stable application interface:

```text
initial Hotfix HTTP manifest
  -> stable ASP.NET endpoint slot
  -> current Hotfix generation lease
  -> readonly HTTP call
  -> Hotfix handler
  -> stable response value
```

Stable code owns Kestrel, listener exposure, bounded request capture,
admission, tracing, and response writing. Hotfix owns product validation,
authorization decisions, idempotency policy, Actor calls, persistence
orchestration, route declarations, response DTOs, and response semantics.

The initial generation freezes the process-local HTTP route manifest. The
stable host assigns internal endpoint slots and later candidates bind their
typed handlers to those slots. Application code does not declare numeric HTTP
method ids. A candidate whose service names, HTTP methods, or route patterns
differ from the initial manifest is rejected and requires a process restart.

One request stays on one generation. Candidate validation rejects duplicate,
signature-mismatched, or manifest-incompatible HTTP handlers before
publication, and the previous generation remains alive until its in-flight
requests drain.
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

Declare Startup Actor groups in `HotfixStartup.ConfigureActors` with
`RegisterStartup<TActor, TKey>()` or
`RegisterStartup<TActor, TKey>(selector)`. `Lakona:ActorHosts` chooses which
nodes are capable of hosting each Actor kind; Startup selection and placement
policy remain in code.

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
