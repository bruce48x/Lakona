---
name: lakona-implement-service
description: Implement or update Lakona RPC service handlers in Server/Hotfix from Shared interfaces marked with RpcService. Use when adding or completing a service, changing RPC methods, selecting the correct generated service call context, fixing Hotfix service binding, validating RPC-to-Hotfix wiring, or detecting that a service dependency such as a database or Redis client must be owned by a stable Server.App ILakonaModule.
---

# Implement a Lakona RPC Service

Implement the Shared contract through the project's existing generated Hotfix
binding. Keep ordinary service work focused on reloadable Hotfix business logic.
Keep long-lived in-memory state in actors or Game Sessions, durable state in an
application-owned Store, and stable process resources in `Server.App`, not in
service instances.

## Workflow

1. Read the repository instructions before editing. Start with `AGENTS.md` and
   the project root README, then follow every scoped instruction they name.
2. Locate the requested interface marked `[RpcService]`. Read all of its
   `[RpcMethod]` members, request and reply DTOs, notification contract, numeric
   IDs, and relevant tests.
3. Locate the stable App and Hotfix projects. Inspect their project properties,
   generated namespace, neighboring service implementations, actor access,
   dependency registration, application modules, and validation commands.
4. Search for an existing `[HotfixService(typeof(...))]` binding. Update it in
   place and preserve intentional behavior. Never create a second binding for
   the same contract.
5. Read [service-shapes.md](references/service-shapes.md) before choosing a call
   context, using callbacks, adding dependencies, or creating a new service.
   Apply its stable-resource check before adding any external dependency.
6. Place a new implementation in the Hotfix project's domain folder. Bind it
   with `[HotfixService(typeof(I...Service))]` and use the accessibility,
   namespace style, and constructor pattern established by the project.
7. Preserve each RPC method name and `ValueTask` return shape. Replace the
   Shared request parameter with the service call context required by the
   detected project version, then access the request through `call.Request`.
8. Implement behavior from the user's request, contracts, actors, stores,
   tests, and neighboring code. Ask for a product decision only when repository
   evidence cannot determine a material behavior.
9. When the behavior needs a database, Redis, queue, cache, external client, or
   process-owned background worker, inject the narrow stable App interface into
   Hotfix. Route missing lifecycle ownership to a public sealed `ILakonaModule`
   in `Server.App` with the resource-owning `[NodeRole]`; do not create,
   connect, register, or dispose the resource in the Hotfix service. Use
   `lakona-implement-module` when available to complete required lifecycle work
   within the current authorized implementation. Switching skills does not
   require a new task or renewed approval. Ask only when a material decision
   cannot be resolved from evidence or the work exceeds the authorized scope.
10. Reuse service or domain tests for affected behavior; add or update focused
    tests only where meaningful coverage is missing.
11. Build the discovered Hotfix project and run the focused tests. Report what
    was validated and distinguish compile-time binding from business behavior.

## Non-Negotiable Boundaries

- Treat Shared contracts as authoritative. Do not duplicate or silently
  redesign their DTOs, method IDs, or notification contracts.
- Do not hand-write stable service proxies, binder configuration, endpoint
  marker files, generated RPC glue, or `.UseGeneratedHotfixServices()` calls.
- Prefer the generated service-scoped call type in current generated projects.
  Retain generic `HotfixServiceCall<TRequest>` only when project evidence shows
  that the installed Lakona version uses it.
- Use constructor injection for dependencies. Do not resolve ordinary
  dependencies from a global service locator.
- Depend on stable business interfaces such as `IUserStore` or
  `ILeaderboardStore`, not `NpgsqlDataSource`, `ConnectionMultiplexer`,
  `IDatabase`, or an `ILakonaModule` implementation.
- Never let Hotfix create, connect, register, cache, close, or dispose a stable
  external resource. Do not make a Hotfix service publish Ready or NotReady.
- Treat a service instance as a concurrent generation-scoped coordinator. Do
  not store per-request data or unsynchronized mutable game state in instance
  or static fields.
- Do not return an empty DTO, fake success, or silent no-op merely to make the
  service compile.
- Follow the repository's `ValueTask` conventions. Do not introduce
  `ValueTask.CompletedTask` or `ValueTask.FromResult(...)` in Lakona projects
  that prohibit them.
- Do not suppress Lakona analyzer diagnostics to complete the implementation.

## Validation

Apply the validation steps to affected contracts, reusing existing coverage and
adding tests only where meaningful coverage is missing. For non-behavioral,
low-impact edits, use relevant static checks. A test command may also satisfy
the build when it covers the same graph and configuration. Once relevant checks
and required stage gates pass, stop unless new changes, failures, or a concrete
unresolved risk justify more verification. Report any unverified outcomes.

For implementation changes, validate the discovered Hotfix build directly or
through focused tests that build the same dependency graph and configuration:

```powershell
dotnet build Server/Hotfix/Server.Hotfix.csproj
```

Confirm that the build sees exactly one service implementation and accepts all
method names, request types, return types, call contexts, referenced actors, and
generated types. Run startup or integration coverage when constructor
availability or runtime publication cannot be proven statically. When service
work adds or changes an external-resource module, also validate real startup:
configured dependency failure must keep the node NotReady, successful
`StartAsync` must prove the resource usable before Ready. Nodes carrying the
module role must provide its configuration; nodes outside that role must not
construct the module or its resource graph.
