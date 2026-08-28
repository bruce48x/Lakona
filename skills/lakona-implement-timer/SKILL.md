---
name: lakona-implement-timer
description: Create or update framework-owned Lakona Hotfix timers, stable timer arguments, callbacks, and actor lifecycle integration. Use when adding one-shot or periodic jobs, storing or destroying TimerId values, routing timer ticks into actors or services, repairing timer cleanup, or fixing Lakona timer callback and serialization errors.
---

# Implement a Lakona Timer

Implement scheduled work with `LakonaTimer` so callbacks resolve against the
active Hotfix generation and long-lived ownership remains in stable state.

## Workflow

1. Read `AGENTS.md`, the project README, and scoped repository instructions.
2. Define the schedule, owner, first due time, repeat period, callback action,
   cancellation behavior, and cleanup condition. Decide whether the operation
   is one-shot or periodic. Lakona timers are process-memory scheduling: if the
   schedule must survive restart, use an application-selected persistent
   scheduler or Store instead of treating `LakonaTimer` as durable.
3. Inspect existing timer argument DTOs, `[HotfixTimer]` callback modules,
   `TimerId` fields, actor lifecycle hooks, and timer-focused tests.
4. Search for an existing timer with the same responsibility. Update its
   complete lifecycle instead of creating a competing schedule.
5. Read [timer-shapes.md](references/timer-shapes.md) before defining arguments,
   choosing callback placement, creating the timer, or writing cleanup.
6. Put timer argument types and any stored `TimerId` in the stable App
   assembly. Keep callback implementations and scheduling decisions in
   `Server.Hotfix`.
7. Define a public instance callback method on a class marked `[HotfixTimer]`.
   Accept exactly the project-compatible `TimerTick<TArgs>` shape and return
   `ValueTask`.
8. Create the timer from an active Hotfix execution scope with a direct static
   typed callback selector. Store the returned ID when later destruction or
   duplicate prevention is required.
9. Make periodic creation idempotent for its owner. Destroy owned timers during
   actor or component cleanup and clear stable ownership before awaiting
   destruction.
10. Route each tick into an actor or application service that owns the mutable
    state. Keep the callback thin and propagate its cancellation token.
11. Build the Hotfix project and run timer, actor lifecycle, and domain tests
    that cover creation, dispatch, repetition, and cleanup.

## Non-Negotiable Boundaries

- Use `LakonaTimer`. Do not introduce `System.Threading.Timer`,
  `PeriodicTimer`, fire-and-forget delay loops, or `Task.Run` schedulers for
  Hotfix business work.
- Create and destroy timers only inside an active Hotfix execution scope. Do
  not call `LakonaTimer` from stable App code, constructors, or work that
  escaped the scope.
- Use a static typed selector such as
  `static (RoomTimerCallbacks callbacks) => callbacks.TickAsync`. Do not retain
  a Hotfix delegate or dispatch by a hand-written method-name string.
- Keep timer arguments stable, concrete, bounded, and serializable. Do not put
  actors, services, callbacks, cancellation tokens, or arbitrary object graphs
  in timer arguments.
- Store cancellable timer ownership as `TimerId` in stable state. Do not
  manufacture IDs.
- Do not keep mutable game state in the timer callback class. Enter an actor
  turn or invoke an application service.
- Do not retain transport callbacks, session callback objects, or old Hotfix
  generation objects across ticks.
- Use `CancellationToken.None` for mandatory destruction during shutdown when
  an already-canceled lifecycle token would skip cleanup.
- Lakona does not persist timers or rebuild them after process loss. Integrate
  a persistent scheduler as an application resource when the product requires
  durable calendar jobs; do not add persistence semantics to `LakonaTimer`.

## Validation

Build the discovered Hotfix project:

```powershell
dotnet build Server/Hotfix/Server.Hotfix.csproj
```

Run focused tests that prove the timer fires the intended callback, periodic
creation is not duplicated, one-shot versus periodic behavior is correct, the
argument round trip succeeds, and cleanup removes the timer. A successful build
alone does not prove scheduling semantics.
