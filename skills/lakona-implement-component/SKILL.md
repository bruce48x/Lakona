---
name: lakona-implement-component
description: Implement HotfixComponent tools and resource lifecycles in Server.Hotfix, with optional stable ComponentState in Server.App. Use for resource initialization, constructor dependency ordering, NodeRole selection, State ownership, readiness failures, and migration of application resource logic.
---

# Implement a Lakona Hotfix Component

Keep business logic in Hotfix and stable data/resource handles in App. Reuse DI,
role discovery and generation ownership; do not add an Actor, anonymous State,
background-task framework or extra container.

## Workflow

1. Read repository instructions and the current Hotfix component authority.
   Inspect the installed framework APIs and neighboring components.
2. Identify consumers, resource ownership and node roles. Consumers inject the
   concrete component; State has one owner and must not escape it.
3. Declare a public sealed non-generic component with one public constructor.
   Keep injected fields private and readonly. If needed, add ComponentState in
   App: public, sealed, non-generic, with a public parameterless constructor
   initializing basic structures only. Do not register State manually or add
   empty State to stateless tools.
4. Read [resource-patterns.md](references/resource-patterns.md) for initialization
   and disposal. Put optional stable resource registration on the HotfixComponent:
   `[ComponentConfigureServices] public static void ConfigureServices(IServiceCollection
   services, IConfiguration configuration)`. Framework discovery invokes it before
   root Build. Do not add a State solely for registration. This entry accepts
   unkeyed singletons of closed stable types using types or factories; factories
   are created once through root DI and released before activation. No Hotfix
   objects/callbacks may survive in the resource. Constructors and registration do no I/O.
5. Add optional ComponentStart/ComponentStop methods: public instance non-generic
   ValueTask(CancellationToken), with one mandatory by-value token. Each kind
   occurs at most once; the attribute binds the method, not its name.
6. Declare component dependencies with required unkeyed concrete parameters.
   Component interfaces, collections, factories, overrides and service locators
   are unsupported. Stable infrastructure interfaces remain supported.
7. Add NodeRole for a role-specific component; no marker means all nodes. Its
   NodeRole filters registration before requiring data-only configuration.
   Registration has deterministic component-name ordering,
   not constructor dependency ordering. Normal DI and TryAdd semantics apply.
8. Verify changed contracts with focused tests. Resource/readiness changes need
   real dependency startup or the repository E2E, including explicit failures.

## Lifecycle and reload

Start runs once in dependency order before consumers. Final Stop uses the latest
generation in recorded reverse order. Reload replaces logic with the same State
and runs neither hook. Component full names, effective set and State binding are
fixed until restart; dependency changes must preserve the recorded order. Start
edits do not reinitialize a running node. State layout changes require restart.

Components allow concurrent ordinary calls without a mailbox or automatic call
drain. Use stable synchronization or immutable snapshots. Custom caller owners
stop and await them before shutdown. Managed generation execution and ActorStop
finish before component Stop; provider/root disposal follows. V1 Start and V3
Stop may run on different instances.

No detached tasks, timers, recurring loops or long-lived Hotfix callbacks belong
in components. Use Actor + Behavior and ActorTimer for scheduled work; await
finite component I/O. Never retain Hotfix objects, delegates, tasks, providers or
collectible metadata in State.

## Validation

Cover affected ordering, State identity across real DLL reload, role filtering,
illegal registration, failed-candidate cleanup and final disposal. Validate does
not run Start. Startup failure keeps readiness closed and preserves the original
error. Cleanup failures do not skip other owners; cancellation is not forced
interruption. Stop after relevant checks pass and report unverified outcomes.
