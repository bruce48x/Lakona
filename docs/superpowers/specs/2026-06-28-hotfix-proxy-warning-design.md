# Hotfix Proxy Warning Design

Date: 2026-06-28

## Context

The LocalFeed E2E smoke test passed for the generated Godot WebSocket
MemoryPack project, but the generated `Server.Hotfix` build reported two
`CS0436` warnings:

```txt
ChatServiceProxy generated in Server.Hotfix conflicts with ChatServiceProxy
imported from Server.App.

LoginServiceProxy generated in Server.Hotfix conflicts with LoginServiceProxy
imported from Server.App.
```

Runtime verification still succeeded: the client connected, called
`LoginAsync`, and received the expected response. The warning is still a real
generated-code boundary problem because it means the hotfix compilation is
emitting stable service proxy types that belong to the app compilation.

The current architecture documents define this boundary:

```txt
Shared service contract
  -> Server.App stable service proxy and RPC binder
  -> current Server.Hotfix service implementation
```

`Server.Hotfix` intentionally references `Server.App` and `Shared`. Hotfix
services and behaviors compile against stable actor types, stable actor refs,
and shared DTO contracts from those assemblies. The fix must preserve that
reference direction.

## Goals

- Remove `CS0436` service proxy conflicts from generated `Server.Hotfix`
  builds.
- Keep stable RPC service proxies, endpoint binders, and required-service
  contract providers owned by `Server.App`.
- Keep `Server.Hotfix` able to reference `Server.App` normally for actor state,
  generated actor selectors, internal refs, and shared stable boundary types.
- Keep the hotfix generator package usable in both project roles because it
  owns app-side stable hotfix support and hotfix-side behavior wrappers.
- Add focused tests that catch accidental stable proxy generation in
  `Server.Hotfix`.
- Preserve the generated project model where users do not hand-write service
  proxies, endpoint marker files, or `.UseGeneratedHotfixServices()` calls.

## Non-Goals

- Do not remove the `Server.Hotfix -> Server.App` project reference.
- Do not use `ReferenceOutputAssembly="false"` for the `Server.App` reference
  as the primary fix.
- Do not move actor state, actor mailboxes, or stable RPC binding into
  `Server.Hotfix`.
- Do not add user-authored proxy files, service endpoint marker files, or host
  builder calls to generated projects.
- Do not split `Lakona.Game.Server.Hotfix.Generators` into multiple NuGet
  packages for this warning fix.
- Do not change runtime dispatch semantics for already-connected RPC sessions.

## Verified Root Cause

`Lakona.Game.Server.Hotfix.Generators.HotfixGenerator` currently discovers
shared `[RpcService]` contracts in every compilation where the analyzer runs.
It hardcodes the generated service namespace to `Server.App.Generated` and
emits:

- `*ServiceProxy`
- `*ServiceEndpointBinder`
- `GeneratedHotfixRequiredServiceContracts`

The generated tool projects include `Lakona.Game.Server.Hotfix.Generators` as
an analyzer in both `Server.App` and `Server.Hotfix`.

That is correct for actor generation:

- `Server.App` needs stable actor selector services, ref types, cluster
  handlers, and service registration.
- `Server.Hotfix` needs behavior-owned actor ref wrapper extensions that are
  emitted into the matching hotfix behavior boundary.

It is not correct for stable RPC service proxy generation. App-side
`ChatServiceProxy` and `LoginServiceProxy` are intentionally visible to
`Server.Hotfix` through the normal project reference and
`InternalsVisibleTo("Server.Hotfix")`. When `Server.Hotfix` generates the same
internal type names in the same namespace, the compiler reports `CS0436`.

## Considered Approaches

### Approach A: Suppress or Ignore `CS0436`

This keeps the current generated output and suppresses the warning.

Trade-offs:

- Minimal code churn.
- Leaves `Server.Hotfix` generating stable app-owned service proxies.
- Hides a boundary regression that future generated code may depend on by
  accident.
- Does not improve tests or documentation.

Decision: reject.

### Approach B: Set `ReferenceOutputAssembly="false"` on `Server.App`

This keeps the hotfix generator behavior unchanged but narrows the project
reference so imported app proxy types disappear from the hotfix compilation.

Trade-offs:

- Likely removes the warning.
- Breaks or complicates legitimate hotfix references to `Server.App.Chat`
  actor types, generated refs, and stable boundary helpers.
- Fights the documented reference direction:

```txt
Reference direction: Server.Hotfix -> Server.App and Shared
```

Decision: reject as the primary fix. A narrower reference shape can be
revisited only if the hotfix project no longer needs compile-time access to
stable app types, which is not the current architecture.

### Approach C: Generate Hotfix RPC Proxies in a Different Namespace

This avoids same-name conflicts by moving hotfix-emitted proxies out of
`Server.App.Generated`.

Trade-offs:

- Removes the direct type-name conflict.
- Still emits stable service proxy and endpoint binder code in `Server.Hotfix`.
- Risks creating duplicate service binding artifacts in the hotfix assembly.
- Does not match the architecture: stable RPC service proxies belong to
  `Server.App`, not `Server.Hotfix`.

Decision: reject.

### Approach D: Add Explicit Generator Role Settings

The generator reads MSBuild compiler-visible properties that describe which
hotfix outputs are enabled for the current compilation.

Recommended role split:

```txt
Server.App:
  generate stable RPC service proxies
  generate endpoint binders
  generate required-service contract provider
  generate stable actor selectors and refs
  generate generated-service registration

Server.Hotfix:
  generate behavior-owned actor ref wrapper extensions
  generate hotfix state accessors when hotfix state appears
  do not generate stable RPC service proxies
  do not generate endpoint binders
  do not generate required-service contract provider
```

Trade-offs:

- Matches the documented architecture.
- Removes the duplicate generated service proxy source from `Server.Hotfix`.
- Keeps the normal `Server.App` reference available to hotfix code.
- Requires generator option plumbing and template/sample updates.
- Requires test-host support for analyzer config properties.

Decision: use this approach.

## Design

Introduce explicit generation-role options for
`Lakona.Game.Server.Hotfix.Generators`.

The first implementation should use two narrowly named booleans:

```xml
<LakonaHotfixGenerateStableRpcServices>true</LakonaHotfixGenerateStableRpcServices>
<LakonaHotfixGenerateStableActorRefs>true</LakonaHotfixGenerateStableActorRefs>
```

The exact internal option type can evolve during implementation, but the public
MSBuild property names should preserve these meanings:

- `LakonaHotfixGenerateStableRpcServices`: controls app-side stable RPC
  service proxy, endpoint binder, and `IHotfixRequiredServiceContracts`
  provider generation.
- `LakonaHotfixGenerateStableActorRefs`: controls app-side stable actor
  selector, ref, cluster handler, and generated-service registration output.

Behavior-owned hotfix wrapper generation should stay enabled whenever the
current compilation contains `[HotfixBehaviorOf]` types that target actors from
referenced assemblies. It should not depend on stable RPC service generation.

`Server.App.csproj` generated by `Lakona.Tool` should set both properties to
`true` and expose them through `CompilerVisibleProperty`.

`Server.Hotfix.csproj` generated by `Lakona.Tool` should set:

```xml
<LakonaHotfixGenerateStableRpcServices>false</LakonaHotfixGenerateStableRpcServices>
<LakonaHotfixGenerateStableActorRefs>false</LakonaHotfixGenerateStableActorRefs>
```

and expose both properties through `CompilerVisibleProperty`.

The hotfix project should continue to reference `Server.App.csproj` normally:

```xml
<ProjectReference Include="..\App\Server.App.csproj" />
```

This keeps actor state and app-generated stable refs visible to hotfix code.

## Generator Behavior

`HotfixGenerator.Initialize` should stop unconditionally registering stable RPC
service generation from `context.CompilationProvider`.

Instead, create a small options model from
`AnalyzerConfigOptionsProvider.GlobalOptions`:

```txt
build_property.LakonaHotfixGenerateStableRpcServices
build_property.LakonaHotfixGenerateStableActorRefs
```

Recommended defaults:

- `LakonaHotfixGenerateStableRpcServices`: enabled by default for compatibility
  with existing tests and manually-authored app projects that already rely on
  analyzer presence.
- `LakonaHotfixGenerateStableActorRefs`: enabled by default for compatibility
  with existing app-side actor contract generation.

Generated tool projects should still write explicit properties so their role is
unambiguous.

When `LakonaHotfixGenerateStableRpcServices` is false:

- Do not call `GenerateRpcServices`.
- Do not emit `*.HotfixRpcService.g.cs`.
- Do not emit `GeneratedHotfixServices.g.cs`.
- Do not report RPC service shape diagnostics for shared service contracts.

When `LakonaHotfixGenerateStableActorRefs` is false:

- Do not emit app-side stable actor selectors, refs, cluster handlers, or app
  generated-service registration from actor contracts in the current
  compilation.

Hotfix-side behavior wrapper generation should continue to run from
`[HotfixBehaviorOf]` types and actor contracts imported from referenced
assemblies. This is what allows hotfix code to call behavior-owned generated
actor APIs without re-emitting app-owned stable refs.

## Template And Sample Updates

Update `Lakona.Tool` renderers:

- `ServerAppRenderer` emits explicit generator role properties and
  `CompilerVisibleProperty` entries.
- `HotfixRenderer` emits explicit false values for app-owned generation roles
  and matching `CompilerVisibleProperty` entries.

Update maintained sample projects that include the hotfix generator analyzer
directly through project references:

- `samples/Game.Godot.Chat/Server/App/Server.App.csproj`
- `samples/Game.Godot.Chat/Server/Hotfix/Server.Hotfix.csproj`
- `samples/Game.Unity.Agar/Server/App/Server.App.csproj`
- `samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj`

App projects should opt in to stable RPC and actor generation. Hotfix projects
should opt out of stable RPC and app-side actor contract generation while
keeping behavior wrapper generation available.

## Documentation Updates

Update the hotfix service-binding documentation to state that stable RPC
service proxy generation is app-side only. The hotfix assembly provides service
implementations and lifecycle handlers; it does not emit another copy of the
stable service proxy.

Update source-generation documentation if it describes the hotfix generator as
role-agnostic. The new rule is:

```txt
The same generator package runs in both app and hotfix projects, but generated
outputs are role-gated by explicit compiler-visible MSBuild properties.
```

The existing review note can remain in `docs/superpowers/reviews/` as the
historical trigger for this fix.

## Test Plan

Generator tests:

- Add test-host support for analyzer config global options.
- Add a test where stable RPC service generation is enabled and a shared
  `IChatService` contract produces `ChatServiceProxy`,
  `ChatServiceEndpointBinder`, and `GeneratedHotfixRequiredServiceContracts`.
- Add a test where stable RPC service generation is disabled and the same
  shared service contract produces none of those app-owned stable service
  types.
- Add a two-phase app/hotfix test where the hotfix compilation references the
  generated app assembly and verifies the hotfix generated source does not
  contain `ChatServiceProxy` or `LoginServiceProxy`.
- Keep existing unsupported service-shape diagnostics covered under the enabled
  stable RPC generation role.

Tool renderer tests:

- Update `ServerAppRendererTests` to assert app role properties and
  `CompilerVisibleProperty` entries.
- Update `HotfixRendererTests` to assert hotfix opt-out role properties and
  `CompilerVisibleProperty` entries.
- Keep assertions that `Server.Hotfix` references `Server.App` normally.

Generated project validation:

- Run the focused generator tests.
- Run tool rendering tests.
- Run a LocalFeed E2E smoke test after implementation to prove the generated
  `Server.Hotfix` build no longer reports the proxy `CS0436` warnings.

Recommended commands:

```powershell
dotnet test tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj
pwsh -NoProfile -File .\.agents\skills\lakona-e2e-testing\scripts\run-e2e.ps1 -Feed LocalFeed
```

## Rollout

This change modifies shippable source under `src/**`, so implementation must
follow repository version-bump rules for affected packages before publishing or
merging.

The likely affected packages are:

- `Lakona.Game.Server.Hotfix.Generators`
- `Lakona.Tool`

If implementation changes only tests or docs during design review, no package
version bump is required. Once generator or tool source changes, bump the
affected package versions and update package-version constants or generated
template references required by repository policy.

## Acceptance Criteria

- `Server.Hotfix` generated source no longer contains app-owned stable service
  proxy classes for shared RPC services.
- `Server.App` generated source still contains the stable service proxies,
  endpoint binders, and required-service contract provider.
- `Server.Hotfix.csproj` still references `Server.App.csproj` without
  `ReferenceOutputAssembly="false"`.
- Focused generator and tool renderer tests pass.
- LocalFeed E2E generated server build no longer reports the `ChatServiceProxy`
  or `LoginServiceProxy` `CS0436` warnings.
- Existing runtime RPC behavior remains unchanged.
