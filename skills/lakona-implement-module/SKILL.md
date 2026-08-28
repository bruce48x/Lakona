---
name: lakona-implement-module
description: Implement or revise ILakonaModule application-resource lifecycles in stable Server.App code. Use when adding database, Redis, cache, queue, background-worker, or other process-scoped resources; declaring the module NodeRole; deciding DI ownership for asynchronously created clients; gating readiness on initialization; or fixing business adapters that depend on lifecycle modules.
---

# Implement a Lakona Application Module

Keep process resources in stable App code, publish runtime dependencies through
the final root provider, and make module completion an honest readiness gate.

## Workflow

1. Read the repository instructions, root README, and current application-module
   documentation. Inspect neighboring modules and the installed Lakona version
   before choosing a pattern.
2. Locate every consumer of the resource. Separate lifecycle control from the
   runtime interface: business adapters should inject the client or the
   narrowest useful interface, never the `ILakonaModule` implementation.
3. Choose the one node role which owns the resource and mark the module with
   `[NodeRole("...")]`. Confirm that deployment nodes carrying that role also
   receive the required configuration. Lakona filters modules by role before
   construction; connection-string presence is not an enable switch.
4. Read [resource-patterns.md](references/resource-patterns.md) before
   implementing DI publication, asynchronous creation, optional node
   configuration, or Hotfix constructor compatibility.
5. In `ConfigureServices`, synchronously declare the complete stable object
   graph. Perform no network I/O, service resolution, temporary provider
   construction, migration, or background startup.
6. In `StartAsync`, create or resolve the resource, complete initialization,
   probe real availability, and return only when consumers can use it. Clean up
   every partially created resource before propagating failure.
7. In `StopAsync`, stop new work, close the resource gracefully, and support
   repeated calls and partial startup. Let the root provider perform final
   disposal for provider-owned singletons.
8. Add focused tests for registration, singleton identity, missing
   configuration, configured dependency failure, partial-start cleanup, and
   idempotent stop. Run the affected server tests and a real startup/E2E check
   when readiness or external connectivity changed.

## Lifecycle Contract

- Treat one discovered module instance as the process/root-provider lifecycle
  coordinator. The same instance receives `ConfigureServices`, `StartAsync`,
  and `StopAsync`.
- Register stable resources before the final provider is built. `StartAsync`
  may initialize registered objects; it cannot add registrations.
- Preserve explicit failure semantics: an enabled dependency that cannot
  connect, initialize, or pass its probe must fail startup and keep the node
  NotReady.
- A selected module with missing required configuration must fail startup.
  Nodes outside its `[NodeRole]` do not construct or register the module.
- Keep selection fixed for the process lifetime. Require restart after a
  configuration change that alters the DI graph.
- Keep resources with a strict startup relationship in one module. Do not rely
  on module name ordering to create dependencies between modules.
- Every module is public, sealed, parameterless, and declares exactly one
  `[NodeRole]`; applications do not register discovered modules manually.

## DI Ownership Rules

- Inject `NpgsqlDataSource`, `IConnectionMultiplexer`, `IDatabase`, HTTP
  clients, or application Store interfaces into consumers. Keep module types
  out of business constructors.
- For synchronously constructible clients, register the client singleton in
  `ConfigureServices`, resolve and probe it in `StartAsync`, and let the root
  provider dispose it.
- For asynchronously created clients, register a gated singleton factory in
  `ConfigureServices`; publish the connected instance in `StartAsync`, then
  resolve it from `context.Services` and verify reference identity so the root
  provider owns the same object.
- Close provider-owned asynchronous clients in `StopAsync`; leave final
  `Dispose` to root-provider shutdown. Dispose candidates directly only when
  startup fails before provider ownership is established.
- Prefer the narrowest runtime dependency. For example, a Redis Store that only
  issues database commands should inject `IDatabase`, while the module manages
  the multiplexer lifecycle.

## Validation

Use discovered project paths. At minimum:

```powershell
dotnet test <focused-server-or-application-test-project> --no-restore
```

For database, Redis, node-role, or readiness changes, run the repository's real
startup/E2E script with the external dependencies present. Confirm both sides
of the contract:

- nodes carrying the module role connect and stay NotReady on missing or
  unhealthy dependencies;
- nodes without the module role never construct it or register its clients;
- a misrouted business call fails explicitly instead of silently using an
  in-memory fallback;
- runtime consumers resolve the provider-owned resource and never the module.
