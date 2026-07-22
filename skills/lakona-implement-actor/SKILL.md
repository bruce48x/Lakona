---
name: lakona-implement-actor
description: Create or update Lakona game actors across stable Server.App state shells and reloadable Server.Hotfix behavior. Use when modeling long-lived mutable game state, adding actor messages or lifecycle hooks, choosing actor keys and Local Route or Startup access, implementing cross-actor calls, or fixing Lakona actor and Hotfix analyzer errors.
---

# Implement a Lakona Actor

Model one long-lived mutable game entity as a stable actor state shell plus one
reloadable Hotfix behavior. Keep the actor boundary explicit and preserve
sequential turn execution.

## Workflow

1. Read `AGENTS.md`, the project README, and scoped repository instructions.
2. Identify the business identity, state ownership, commands, queries,
   lifecycle, placement, persistence, and callers. Confirm that an actor is the
   right concurrency boundary rather than an RPC DTO, transient service, or
   database record.
3. Inspect existing actors, App-side message DTOs, Hotfix behavior classes,
   startup declarations, serializer conventions, and tests.
4. Search for an existing actor and `[HotfixBehaviorOf]` binding. Extend the
   existing pair rather than creating duplicate state or a second behavior
   class.
5. Read [actor-shapes.md](references/actor-shapes.md) before choosing the key,
   creating App contracts, selecting Local versus Route versus Startup, or
   adding lifecycle hooks.
6. Define stable identity, state, and cross-boundary message DTOs in
   `Server.App`. Keep the actor class focused on state; place game decisions in
   `Server.Hotfix`.
7. Implement public behavior entry methods on the unique class marked
   `[HotfixBehaviorOf(typeof(...))]`. Use the target actor as the first
   parameter, a stable request DTO as the second, and the project cancellation
   convention.
8. Call other actors through generated selectors with a direct static lambda.
   Use the selector whose placement semantics match known ownership.
9. Add `[ActorStart]` and `[ActorStop]` hooks only for real lifecycle work.
   Release timers and other long-lived handles during cleanup.
10. Add or update focused tests for state transitions, replies, lifecycle,
    placement assumptions, and failure behavior.
11. Build the Hotfix project and run the focused actor tests. Treat analyzer
    diagnostics as boundary violations to fix, not warnings to suppress.

## Non-Negotiable Boundaries

- Use a stable business key. Do not encode a node, endpoint, transport,
  callback, RPC Session, or incidental connection in an actor ID.
- Keep durable mutable state on the stable App actor. Do not store it in Hotfix
  instance fields, static fields, callbacks, or background tasks.
- Do not put business methods on the stable actor merely to bypass Hotfix.
- Mutate non-public actor state only from the actor itself or its unique Hotfix
  behavior. Do not make fields public just to evade `LKNHOTFIX031`.
- Do not self-call through `ActorAccess`. Continue the current actor turn
  directly when operating on the same actor.
- Use `Route<TActor>(key)` for the normal distributed business path. Use
  `Local<TActor>(key)` only after proving current-node ownership. Use
  `Startup(key)` only for a declared startup actor group.
- Use `ActorHosting` for dynamic creation or destruction. Normal calls and
  timer callbacks must not implicitly create missing actors.
- Use `CallAsync` when the caller needs completion or a reply. Use `PostAsync`
  only when mailbox acceptance is sufficient.
- Do not block on actor work, discard required completion, or hand-write string
  dispatch and raw route plumbing.

## Validation

Build the discovered Hotfix project; it references the stable App project and
therefore validates both halves in the normal project shape:

```powershell
dotnet build Server/Hotfix/Server.Hotfix.csproj
```

Run existing actor or domain tests. Verify state transitions and lifecycle
behavior, not only source shape. If routed calls are introduced, validate the
project's serializer and cluster test surface as well.
