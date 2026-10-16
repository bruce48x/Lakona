# Lakona Service Shapes

Use project source as the final authority. This reference describes the current
generated-project model and the compatibility checks needed for older projects.

## Ownership

```text
Shared
  RPC interface, callback interface, DTOs, numeric contract IDs

Server.App
  generated stable binder, proxy, and service-scoped call context
  stable DTOs, resource types/dependencies, and optional ComponentState

Server.Hotfix
  user-authored Hotfix service, resource components/registration, business decisions
```

Adding a Shared service and a matching Hotfix implementation must not require a
hand-written stable proxy, endpoint marker, or binding configurator.

## Discover the Project Shape

Inspect the App and Hotfix project files for properties such as:

```xml
<!-- stable App -->
<LakonaProjectRole>ServerApp</LakonaProjectRole>

<!-- Hotfix -->
<LakonaProjectRole>Hotfix</LakonaProjectRole>
```

Find the configured generated namespace and inspect working services before
adding imports. Generated source usually does not belong in the repository.

Useful discovery commands include:

```powershell
rg -n "\[RpcService|\[HotfixService" Shared Server -g "*.cs"
rg -n "LakonaProjectRole|RootNamespace" Server -g "*.csproj"
```

Adapt roots and commands to the repository instead of assuming these names.

## Select the Call Context

Current generated projects author handlers with a service-scoped call type:

```csharp
[HotfixService(typeof(IStageService))]
internal sealed class StageService
{
    public ValueTask<GetStageProgressReply> GetProgressAsync(
        StageServiceCall<GetStageProgressRequest> call)
    {
        return new ValueTask<GetStageProgressReply>(LoadProgress(call.Request));
    }
}
```

The call type is emitted by the stable App assembly from the Shared service
contract. It exposes:

- `Request`
- `ConnectionId`
- `CurrentSession`
- `CurrentSessionItems`
- `Services`
- `Actors`
- `GameServer`

Older or differently configured projects may establish this shape instead:

```csharp
public ValueTask<GetStageProgressReply> GetProgressAsync(
    HotfixServiceCall<GetStageProgressRequest> call)
```

Do not infer the form from the newest Lakona repository alone. Prefer, in
order:

1. the installed package version and project generator properties
2. a neighboring compiling service in the same Hotfix project
3. generated compiler output or analyzer diagnostics
4. the current Lakona documentation

Do not migrate all neighboring services while implementing one contract unless
the user explicitly requests that migration.

## Preserve Method Shape

Transform only the request parameter into the call context:

```csharp
// Shared contract
ValueTask<Reply> QueryAsync(QueryRequest request);
ValueTask ExecuteAsync(CommandRequest request);

// Hotfix implementation
ValueTask<Reply> QueryAsync(QueryServiceCall<QueryRequest> call);
ValueTask ExecuteAsync(QueryServiceCall<CommandRequest> call);
```

Use the actual service name for the generated context. Preserve method names,
generic result shape, and cancellation behavior expected by the project.

## Dependencies And State

For container-owned services and helpers, use a public constructor, or the project's explicitly selected
`[ActivatorUtilitiesConstructor]`, for injected dependencies. Hotfix validation
rejects missing dependencies, open generic implementations, and ambiguous
public constructors before publishing a candidate generation.

Use `[HotfixComponent]` for tools and resource owners that the generation
container owns; they are automatically registered once per generation.
Components use concrete injection only; do not override or alias their generated
registration. Ordinary infrastructure may still use explicit DI registration.
Do not register `[HotfixService]` classes themselves.

Direct and indirect `System.Exception` subclasses are runtime error objects,
not container-owned helpers. Keep them beside Hotfix business logic and create
them with `new`; error codes and inner exceptions are constructor data, not
services to register. Remove a mistaken `[HotfixComponent]` marker when
`LAKONA20060` reports it. The exception exemption from `LAKONA20037` does not
exempt ordinary roleless classes or missing dependencies on real components.

Service instances live for one published Hotfix generation and may receive
concurrent calls. Keep request data in the call value. Put long-lived mutable
game state in actors or Game Sessions. Synchronize any genuinely shared mutable
coordinator field explicitly.

## Resource components

Place business resource operations and initialization in HotfixComponent.
Consumers inject the concrete component; they do not inject a component alias
or State. Stable handles and optional marked State stay in App. Use
`lakona-implement-component` for lifecycle work already required by the task.

Hooks run on first activation/final shutdown, not reload. No detached tasks or
timers belong in a component; use Actor + Behavior for scheduled work.

Prefer the component's `[ComponentConfigureServices]` for stable root-DI
resources; App `AddServices` remains valid for global host composition.
Registration is synchronous and performs no I/O. Start awaits initialization and
probes, cleans failed local candidates and fails startup on unavailable resources.
Optional NodeRole excludes components before construction; missing configuration
on an enabled node must fail. The same NodeRole filters component registration.
Dependencies start first, Stop runs in reverse after callers have stopped, and
root resources are disposed last. See the component Skill's
[resource patterns](../../lakona-implement-component/references/resource-patterns.md)
for complete examples with and without State.

## Callbacks And Sessions

Current generated call contexts do not expose the RPC connection's callback
proxy. Inject `IClientNotifications`, normally behind a domain notifier, select
the intended Game Session with `ForSession<TCallback>(session)`, and invoke the
source-generated notification method on that target. The returned
`ClientNotificationStatus` reports synchronous admission; accepted delivery is
framework-owned and may use local dispatch, cluster routing, or reliable push.

```csharp
var status = clientNotifications
    .ForSession<IStageNotifications>(session)
    .ProgressChanged(notification);
```

An RPC handler may use `call.CurrentSession` when the current Game Session is
the intended recipient. Before a Game Session exists, return information in the
RPC reply instead of treating the connection as a parallel notification
channel. Do not store notification targets, transport sessions,
connection-scoped proxies, or `RpcSession` in durable state.

Do not expose `GameSessionKey` through Shared DTOs. Start, terminate, and notify
sessions through server-side framework APIs already used by the project.

## Validation Evidence

For implementation changes, validate the Hotfix build directly or through
focused tests that build the same graph and configuration. Reuse coverage for
affected behavior such as validation, actor messages, persistence,
notifications, and error handling; add tests only where coverage is missing.
A successful build proves structural binding; it does not prove that an
untested default reply implements the product requirement.
