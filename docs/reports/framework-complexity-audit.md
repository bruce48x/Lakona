# Lakona Framework Complexity Audit

Date: 2026-07-04

This document records a maintainer-facing audit of Lakona framework complexity.
It was written after removing `HotfixActorContract`, because that issue exposed
a larger risk: the framework can compile, pass integration tests, and still ask
new users to understand duplicated concepts that are unnecessary for the product
model.

The goal is not to prove that no similar issue remains. The goal is to keep a
durable list of places where Lakona may be carrying more framework surface,
runtime indirection, or maintenance burden than the current product model needs.

## Why This Audit Exists

`HotfixActorContract` looked reasonable in isolation: stable App code declared
which actor methods existed, and hotfix behavior implemented the actual methods.
That split gave the generator enough information to produce actor refs and
dispatch tables.

From a new user's point of view, the model was still wrong. Users had to author
two concepts for one business surface:

- a stable `HotfixActorContract`
- a reloadable `HotfixBehavior`

The framework then had to keep those two concepts matched. That added:

- more authoring work
- more analyzer and generator rules
- more generated-code cases
- more room for mismatch bugs
- more explanation in docs and samples

Deleting `HotfixActorContract` and deriving actor APIs from `HotfixBehavior`
reduced both the user model and the framework maintenance model. That is the
class of simplification this audit tracks.

## Why The Problem Was Not Caught Earlier

The previous tests were mostly correctness tests. They proved that generated
code compiled, dispatch worked, samples ran, and hotfix loading succeeded. They
did not prove that the authoring model was minimal.

`HotfixActorContract` was not primarily a runtime bug. It was an architecture
smell:

- the same business method list existed in two places
- the more stable project (`Server.App`) had to know more about reloadable
  behavior than it should
- the generator had to validate a pairing that should not exist
- sample code looked more complex than the mental model Lakona wants to teach

This kind of issue is easy to miss when development focuses on "can the
feature work?" instead of "does the feature reduce the user's model?" The fix
is to treat new-user review and complexity budgeting as separate checks from
runtime correctness.

## Audit Scope

This audit looked at:

- `src/**` framework packages
- `tests/**` guardrails around public surface and generated behavior
- `docs/**` architecture and design documents
- generated-project implications for `Lakona.Tool`
- the recent sample review work for `Game.Godot.Chat` and `Game.Unity.Agar`

The scan used source inspection and targeted searches for signs of accidental
complexity:

- compatibility aliases or obsolete options
- hidden fallback providers
- global service replacement
- ambient scopes
- runtime string lookup
- public generated-support APIs that expose low-level runtime types
- large modules that generate or own several product boundaries
- docs that explain complexity as a workaround rather than as a product concept

This was not a security audit, performance audit, or proof that every complex
module is wrong.

## Evaluation Criteria

### Delete

Delete or remove public surface when it has no active runtime behavior, exists
only as compatibility scaffolding, duplicates another concept, or makes users
believe a configuration value still matters when it no longer does.

This category should be acted on immediately when tests can prove the surface
is gone and the product model still holds.

### Simplify

Simplify when a module still provides real behavior but does so through hidden
indirection, duplicated concepts, broad public commitments, or implementation
coupling that makes future changes risky.

This category usually needs a design note and a staged migration, especially
when generators, analyzers, runtime packages, and docs must move together.

### Keep And Revisit

Keep when the complexity protects a real product guarantee, such as hotfix
reload safety, cluster routing, actor serialization, or generated-project
simplicity. Still record the tradeoff if the current API shape is stringly,
ambient, hard to explain, or likely to leak into user code.

## Findings Already Removed

### `LakonaGameRuntimeOptions.ClusterEndpoint`

Status: removed.

The runtime already reads cluster configuration from the `Lakona:Cluster`
section. `ClusterEndpoint` on `LakonaGameRuntimeOptions` was a compatibility
field that no longer drove the real cluster path.

Why it was a problem:

- it suggested that `Lakona:Game:Runtime:ClusterEndpoint` was meaningful
- it duplicated the real `Lakona:Cluster:Endpoint` path
- it expanded the public options object without active behavior
- it could mislead users debugging cluster startup

The cleanup removed the field and added a public-surface regression test so the
old name cannot quietly return.

Related docs:

- [configuration.md](../configuration.md)
- [cluster.md](../cluster.md)

### `LakonaDiagnosticsObservabilityOptions.SummaryEnabled`

Status: removed.

The observability summary behavior is tied to the local admin endpoints and
runtime wiring. `SummaryEnabled` had become a passive option that implied there
was still a separate summary toggle.

Why it was a problem:

- it made the option model look larger than the actual behavior
- it created a "set this and expect something to change" trap
- it required docs and tests to carry an old concept

The cleanup removed the field and added a public-surface regression test.

Related docs:

- [configuration.md](../configuration.md)

## High-Priority Simplification Candidates

These items still provide real behavior. They should not be deleted in the same
way as stale options, but they are strong candidates for planned simplification.

### Generated Server API Leaks `RpcSession`

Status: design already documented; implementation still pending.

The public API boundary document already says `RpcSession` should not be a
normal user extension point. Generated server binders still expose it through
factory signatures such as `Func<RpcSession, TService>`, and notification
proxies still wrap `RpcSession` internally to send notifications.

Why this is complex:

- `RpcSession` owns receive loops, scoped service caches, keepalive, request
  gates, dispatch, notifications, and shutdown behavior
- exposing it in generated signatures makes the low-level runtime look like an
  application authoring API
- once users depend on it directly, changing session internals becomes harder
- tests and extension points can accidentally normalize direct session usage

Analysis:

This is similar to `HotfixActorContract` in one important way: the framework
surface shows users an implementation mechanism instead of the product concept.
The product concept is "implement a service and optionally receive a narrow
request/session context." The mechanism is `RpcSession`.

Target direction:

1. Introduce a narrow server-side context such as `IRpcServiceContext`.
2. Change generated binders to accept that context only when service creation
   needs metadata.
3. Keep notification support exposed through generated notification contract
   interfaces, not through direct `RpcSession` calls.
4. After generated code no longer exposes `RpcSession`, move constructors and
   low-level APIs behind runtime-internal or generated-support boundaries.

Related docs:

- [api-stability/public-api-boundaries.md](../api-stability/public-api-boundaries.md)
- [source-generation.md](../source-generation.md)
- [session.md](../session.md)

Related code:

- [RpcSession.cs](../../src/Lakona.Rpc.Server/Dispatching/RpcSession.cs)
- [RpcServiceRegistry.cs](../../src/Lakona.Rpc.Server/Dispatching/RpcServiceRegistry.cs)

### `HotfixGenerator.cs` Is Too Broad Internally

Status: behavior-first actor model is fixed; generator structure still needs
internal simplification.

`HotfixGenerator.cs` is about 2,557 lines and currently owns several different
products:

- stable RPC service proxies
- behavior-derived actor ref APIs
- actor wrapper generation
- state accessor generation
- service and notification contract discovery
- diagnostics and naming helpers
- hotfix method key emission

Why this is complex:

- unrelated generated products can become coupled through shared helper state
- small behavior changes require reading a very large file
- diagnostics, discovery, and emission are harder to test independently
- removing one model, such as `HotfixActorContract`, still leaves a large
  generator surface where old assumptions can survive

Analysis:

Large source generators are especially risky because generator output becomes
part of the user's experience. A generator can hide complexity from the user's
source files while accumulating complexity in the framework. That is acceptable
only when the generator is internally organized by product boundary.

Target direction:

1. Keep the current generated output stable while refactoring internals.
2. Split discovery from emission.
3. Split emitters by product boundary:
   - state accessors
   - stable RPC service proxies
   - behavior-derived actor refs and wrappers
   - diagnostics
   - shared naming/key helpers
4. Add focused tests around each product boundary so later simplifications do
   not require broad snapshot reasoning.

Related docs:

- [source-generation.md](../source-generation.md)
- [hotfix/architecture.md](../hotfix/architecture.md)

Related code:

- [HotfixGenerator.cs](../../src/Lakona.Game.Server.Hotfix.Generators/HotfixGenerator.cs)

### Hotfix DI Fallback Can Hide Boundary Mistakes

Status: keep for now; needs an explicit replacement design.

`HotfixManager` builds a hotfix service provider and falls back to root services
for activation. The relevant implementation includes:

- `BuildHotfixProvider`
- `CreateFallbackActivationDescriptor`
- `ActivationFallbackServiceProvider`
- `FallbackServiceProvider`
- `TryGetCombinedEnumerable`

Why this is complex:

- hotfix dependencies can appear to work because they are found in the root
  provider rather than because the hotfix boundary declared them clearly
- missing explicit bridge contracts may be discovered late
- behavior reload and dependency lifetime reasoning become harder
- the code has to merge `IEnumerable<T>` services from two providers

Analysis:

Some bridge is necessary: hotfix behavior must call stable services. The smell
is not "hotfix can use stable services." The smell is that the boundary is
implicit. A new maintainer has to understand provider fallback rules rather
than reading a clear list of allowed stable dependencies.

Target direction:

1. Design an explicit stable-dependency bridge for hotfix activation.
2. Make required stable services visible through generated contracts or a
   declared bridge object.
3. Keep fallback behavior only as a documented internal compromise until the
   explicit bridge is available.
4. Add diagnostics for missing bridge declarations instead of relying on
   provider fallback behavior.

Related code:

- [HotfixManager.cs](../../src/Lakona.Game.Server.Hotfix/HotfixManager.cs)

## Design Tradeoffs To Revisit

These areas are not obvious deletion candidates. Each one protects a real
runtime goal, but each one also carries complexity that should not spread.

### `LakonaTimer`

Status: useful product concept; API shape is still heavier than ideal.

`LakonaTimer` supports hotfix-safe callbacks. Timer callbacks resolve against
the newest loaded hotfix generation, and feature lifecycle timer creation can
be staged, committed, or rolled back when reload succeeds or fails.

Why the complexity exists:

- callback behavior must survive hotfix reloads
- timers created during feature startup must not leak if startup fails
- timer callbacks should run against the active hotfix runtime
- actor and feature timers need a shared runtime model

Why it still deserves scrutiny:

- callbacks use `string methodName`
- the public API tells users to use `nameof(...)`, which is better than raw
  string literals but still not fully typed
- `LakonaTimerExecutionScope` is ambient
- timer args serialization is custom framework machinery
- lifecycle staging adds a second hidden state path to reason about

Analysis:

The rollback semantics are probably real product value. The string-based
method binding is the part that looks closest to accidental complexity. It is
similar to old actor contracts in that the framework asks users to express a
method identity separately from the method itself.

Target direction:

1. Explore generated typed timer callback descriptors.
2. Prefer compiler-checked callback references over `string methodName`.
3. Keep startup rollback semantics unless a simpler lifecycle model can provide
   the same safety.
4. Revisit whether timer args need a custom serializer or can use a stricter
   subset of the configured serializer model.

Related docs:

- [actor.md](../actor.md)
- [hotfix/architecture.md](../hotfix/architecture.md)

Related code:

- [LakonaTimer.cs](../../src/Lakona.Game.Server.Hotfix.Abstractions/Timers/LakonaTimer.cs)
- [LakonaTimerExecutionScope.cs](../../src/Lakona.Game.Server.Hotfix.Abstractions/Timers/LakonaTimerExecutionScope.cs)
- [HotfixFeatureContext.cs](../../src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureContext.cs)

### `HotfixFeatureContext.HandleCommand<TRequest, TReply>(string methodName)`

Status: usable, but should not become the pattern for more APIs.

Feature command registration currently accepts a method name string, defaulting
to `HandleAsync`.

Why this is complex:

- registration and implementation can drift
- errors are discovered through analyzer/runtime validation instead of normal
  navigation and compile-time method references
- the user model is weaker than behavior methods, where calling `self.Method`
  has natural IDE navigation

Target direction:

Consider generated or analyzer-backed command handler discovery so feature
commands follow the behavior-first model: users write the handler, and the
framework derives the binding.

Related code:

- [HotfixFeatureContext.cs](../../src/Lakona.Game.Server.Hotfix.Abstractions/Features/HotfixFeatureContext.cs)

### `LakonaGameServer.RunAsync` Owns Many Startup Concerns

Status: keep the public one-command entry point; simplify internal composition.

The generated-project experience benefits from a single call:

```csharp
return await LakonaGameServer.RunAsync(args);
```

Internally, that entry point currently coordinates many concerns:

- liveness and readiness commands
- configuration binding
- logging
- service catalog registration
- cluster option conversion
- cluster endpoint wiring
- hotfix admin options
- hotfix required service discovery
- gateway registration
- host validation
- initial hotfix loading

Why this is complex:

- a single method can become the de facto architecture map
- startup responsibilities are harder to test in isolation
- future features may keep adding branches to the same entry point

Analysis:

The public API is probably right. The generated sample should stay simple. The
implementation should be factored behind named internal steps so the one-command
experience does not turn into a god method.

Target direction:

1. Keep `LakonaGameServer.RunAsync` as the public generated-project entry.
2. Extract internal startup steps with names that match product boundaries.
3. Keep tests around the composed behavior, but test extracted steps directly
   where possible.

Related code:

- [LakonaGameServer.cs](../../src/Lakona.Game.Server/Hosting/LakonaGameServer.cs)

### Cluster Serializer Registration Replaces Global `IRpcSerializer`

Status: works today; long-term boundary should be more explicit.

`AddLakonaGameClusterEndpoint` removes existing `IRpcSerializer` registrations
and installs a cluster serializer wrapper used by feature messages and remote
actor payloads.

Why the complexity exists:

- cluster feature messages need a serializer that knows cluster contracts
- remote actor payloads need the same cluster-aware serializer
- generated distributed actor accessors need consistent payload handling

Why it is risky:

- global `IRpcSerializer` replacement makes registration order meaningful
- later user registrations can create surprising behavior
- channel-specific serializer needs are expressed through service collection
  mutation rather than through explicit channel services

Target direction:

Move toward explicit serializer services for cluster channels and remote actor
payloads instead of replacing the global RPC serializer as the extension model.

Related docs:

- [cluster.md](../cluster.md)

Related code:

- [LakonaClusterEndpointServiceCollectionExtensions.cs](../../src/Lakona.Game.Server/Hosting/LakonaClusterEndpointServiceCollectionExtensions.cs)

### Notification Command Capture Uses `DispatchProxy`

Status: ergonomic, but runtime capture has hidden failure modes.

`ClientNotificationCommandFactory` lets server code write callback expressions
such as `callback => callback.SomeNotification(...)`, then captures the selected
notification method through `DispatchProxy`.

Why the complexity exists:

- the API is pleasant at the call site
- users do not need to know service ids or method ids
- notification command creation can stay tied to generated notification
  contracts

Why it is risky:

- method selection is enforced dynamically
- callback expressions must follow capture rules
- sync overloads block on async capture
- failures are farther from normal compile-time method binding

Target direction:

Prefer generated typed notification command helpers, or generated capture logic
with stricter compile-time checked method identities.

Related docs:

- [session.md](../session.md)

Related code:

- [ClientNotificationCommandFactory.cs](../../src/Lakona.Game.Server/Sessions/ClientNotificationCommandFactory.cs)

## Not Classified As Same-Class Problems For Now

These modules are complex, but the current complexity appears tied to clear
framework responsibilities rather than accidental product surface.

### `LakonaActorRuntime`

The actor runtime is large because it owns mailbox scheduling, lifecycle, actor
identity, and turn execution. The boundary is relatively clear: game code sees
actors and actor refs, while the mailbox kernel stays internal.

Keep watching for public leakage, but this is not currently the same kind of
duplicated authoring model as `HotfixActorContract`.

### Cluster SQL Directory And MemoryPack Formatter Generation

Cluster directory and formatter generation code is specialized, but it serves
explicit package boundaries: cluster routing and serializer support. The cost is
more acceptable because it is isolated behind cluster packages and helper APIs.

### `Lakona.Tool` Renderers And Templates

Tool renderers can be long because they emit project text. That is not
automatically a runtime architecture smell. The risk appears when template code
teaches obsolete concepts or requires users to author redundant files. The
important guardrail is to keep generated projects aligned with the simplified
framework model.

### Legacy Root Config Tests

Tests that assert old root sections are ignored are not a compatibility burden
by themselves. They are guardrails that prevent accidental resurrection of old
config paths.

## Suggested Future Order

1. Replace generated `RpcSession` binder exposure with a narrow service context.
2. Split `HotfixGenerator.cs` internals by generated product boundary without
   changing generated output shape.
3. Design an explicit hotfix stable-dependency bridge to replace provider
   fallback as the main mental model.
4. Design typed timer callbacks and typed feature command handlers together,
   because both are currently method-name based.
5. Refactor cluster serializer registration toward explicit channel serializer
   services.
6. Replace notification `DispatchProxy` capture with generated typed helpers if
   the ergonomics can stay good.
7. Extract `LakonaGameServer.RunAsync` internals into named startup composition
   steps while preserving the one-line generated-project entry point.

## Open Questions

- Is `LakonaTimer` a core framework feature, or should some games own runtime
  loops directly and use actors for scheduling?
- How much dependency injection convenience is acceptable across the hotfix/root
  boundary before it hides too much?
- Should generated projects hide all low-level runtime APIs, or keep documented
  escape hatches for advanced teams?
- Should `Lakona.Tool` templates optimize for a complete vertical slice, or a
  smaller minimal core that teaches fewer concepts first?
- Which APIs should be treated as generated-support only and hidden from normal
  IntelliSense before the public API freeze?

## Maintenance Rules Learned

- Do not keep compatibility fields without active behavior.
- Do not make users author the same business surface in two places.
- Prefer behavior-first source generation: users write the real behavior, and
  the framework derives binding, refs, and dispatch metadata.
- Runtime correctness tests are not enough; new-user mental-model review is a
  separate quality gate.
- If a framework boundary requires fallback, ambient state, global service
  replacement, or string lookup, document the compromise and the target
  replacement.
- Add public-surface regression tests when deleting obsolete names.
- Keep generated-project templates as the strictest user-experience test: if a
  generated sample teaches a concept, that concept should be worth carrying.

