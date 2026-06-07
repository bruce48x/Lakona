# Merge Lakona.Actor Into Lakona.Game.Server Design

## Purpose

Lakona no longer treats `Lakona.Actor` as an independent product or NuGet
package. The project is still early, compatibility is not a constraint, and the
best product shape is one game-server actor model exposed by
`Lakona.Game.Server`.

The final state is:

- no `Lakona.Actor` NuGet package
- no `Lakona.Actor.SourceGenerator` project
- no public `Lakona.Actor.ActorSystem`, `ActorRef<T>`, `ActorHandle<T>`, or
  `IActor<TMessage>` API for users
- one public actor API under `Lakona.Game.Server.Actors`
- an internal execution kernel inside `Lakona.Game.Server`

The mailbox/runtime work in the former `Lakona.Actor` remains valuable. It
should be absorbed as the private execution engine for game-server actors.

## Current State

The current repository has two actor layers:

```txt
src/Lakona.Actor
  Public process-local actor/mailbox runtime.
  Exposes ActorSystem, ActorRef<T>, ActorHandle<T>, IActor<TMessage>,
  ActorContext<TMessage>, timers, mailbox metrics, diagnostics, and generated
  typed actor clients.

src/Lakona.Actor.SourceGenerator
  Source generator and analyzer for the public Lakona.Actor API.
  Packed into the Lakona.Actor package today.

src/Lakona.Game.Server/Actors
  Game-facing actor API.
  Exposes Actor, ActorContext, IActorRuntime, string ActorId, typed actor
  dispatch attributes, remote actor calls, actor directory, cluster dispatch,
  and message recording integration.
```

`Lakona.Game.Server` currently references `Lakona.Actor`:

```xml
<ProjectReference Include="..\Lakona.Actor\Lakona.Actor.csproj" />
```

`LakonaActorRuntime` wraps `Lakona.Actor.ActorSystem` by converting game actor
callbacks into `ActorRuntimeEnvelope` messages. In practice this means
`Lakona.Game.Server.Actors` is already the product API, while `Lakona.Actor` is
an implementation kernel with an accidentally public surface.

## Decision

Delete the independent `Lakona.Actor` product boundary.

Move the runtime pieces needed by `LakonaActorRuntime` into
`src/Lakona.Game.Server/Internal/ActorKernel` and make them internal to
`Lakona.Game.Server`.

Delete `src/Lakona.Actor` and `src/Lakona.Actor.SourceGenerator` after the
kernel is in place and tests are migrated.

Keep `Lakona.Game.Server.Actors` as the only public actor API.

## Public Boundary

The public API for game code is:

```txt
Lakona.Game.Server.Actors.Actor
Lakona.Game.Server.Actors.Actor<TKey>
Lakona.Game.Server.Actors.ActorContext
Lakona.Game.Server.Actors.IActor
Lakona.Game.Server.Actors.IActorRuntime
Lakona.Game.Server.Actors.ActorId
Lakona.Game.Server.Actors.ActorState
Lakona.Game.Server.Actors.ActorTellResult
Lakona.Game.Server.Actors.ActorStopOutcome
Lakona.Game.Server.Actors.ActorMailboxMetrics
Lakona.Game.Server.Actors.Actor*Attribute
Lakona.Game.Server.Actors.RemoteActor*
Lakona.Game.Server.Actors.IActorDirectory*
```

Game code should not reference:

```txt
ActorSystem
ActorRef<T>
ActorHandle<T>
IActor<TMessage>
ActorContext<TMessage>
ActorClientAttribute
Lakona.Actor.SourceGenerator
```

Typed actor generation for game-facing actors belongs to
`Lakona.Game.Server.Generators`, not to the old `Lakona.Actor.SourceGenerator`.

## Internal Boundary

Create an internal kernel namespace:

```csharp
namespace Lakona.Game.Server.Internal.ActorKernel;
```

The kernel answers exactly one question:

> How does a single in-process actor mailbox execute safely and predictably?

It owns:

- mailbox queueing
- sequential dispatch
- backpressure
- `Tell` / `Call` mechanics
- response slots
- call timeout root-cause diagnostics
- timer delivery
- stop/drain lifecycle
- dead-letter diagnostics
- slow-message diagnostics
- observer error diagnostics
- activity and meter instrumentation

It does not own:

- game actor identity
- DI activation
- game actor base classes
- cluster routing
- remote actor serialization
- route directories
- session lifecycle
- reliable push
- hotfix
- message log storage
- generated game actor APIs

Those remain in `Lakona.Game.Server`.

## Recommended File Layout

Move the former actor runtime into:

```txt
src/Lakona.Game.Server/Internal/ActorKernel/
  Abstractions/
  Core/
  Core/Diagnostics/
  Core/Dispatch/
  Core/Lifecycle/
  Core/Registry/
  Diagnostics/
  Lifecycle/
  Mailbox/
  Messaging/
  Timers/
```

Use internal names that make the boundary clear. Prefer either:

```txt
KernelActorSystem
KernelActorRef<TMessage>
KernelActorHandle<TMessage>
KernelActorContext<TMessage>
KernelActorId
KernelActorCallOptions
```

or keep the old type names but make them internal and hide them under
`Lakona.Game.Server.Internal.ActorKernel`. The safer first pass is to rename the
public-looking types with a `Kernel` prefix so later readers do not mistake
them for user APIs.

Examples:

```csharp
internal sealed class KernelActorSystem : IAsyncDisposable
internal sealed class KernelActorRef<TMessage>
internal sealed class KernelActorHandle<TMessage>
internal readonly record struct KernelActorId(long Value)
internal sealed class KernelActorContext<TMessage>
internal interface IKernelActor<TMessage>
```

If the implementing agent chooses to keep old type names temporarily, every
moved type must still be `internal`.

## LakonaActorRuntime Changes

`LakonaActorRuntime` should use the internal kernel directly:

```csharp
using Lakona.Game.Server.Internal.ActorKernel;
```

Replace references like:

```csharp
global::Lakona.Actor.ActorSystem
global::Lakona.Actor.ActorHandle<ActorRuntimeEnvelope>
global::Lakona.Actor.ActorContext<ActorRuntimeEnvelope>
global::Lakona.Actor.ActorCallOptions
```

with internal kernel types:

```csharp
KernelActorSystem
KernelActorHandle<ActorRuntimeEnvelope>
KernelActorContext<ActorRuntimeEnvelope>
KernelActorCallOptions
```

`LakonaActorRuntime` should remain the only bridge between the public
game-facing actor API and the internal kernel.

`MessageRecordingInterceptor` and `ActorRuntimeOptions` must stop exposing
`Lakona.Actor.IActorMessageInterceptor`. Replace it with a Game.Server-owned
interface:

```csharp
namespace Lakona.Game.Server.Actors;

public interface IActorMessageInterceptor
{
    ValueTask OnBeforeMessage(
        ActorId actorId,
        string messageType,
        object? message,
        CancellationToken cancellationToken);

    ValueTask OnAfterMessage(
        ActorId actorId,
        string messageType,
        object? message,
        Exception? exception,
        CancellationToken cancellationToken);
}
```

The internal kernel can have its own `IKernelActorMessageInterceptor` using
kernel IDs. `LakonaActorRuntime` should adapt game IDs to kernel IDs.

## Source Generator Policy

Delete `src/Lakona.Actor.SourceGenerator`.

The old generator targets the old native `Lakona.Actor` API and generates
clients around `ActorRef<TMessage>` and `ActorSystem`. That surface should not
survive.

Keep `src/Lakona.Game.Server.Generators`; it is the correct home for
game-facing actor generation. If features from the old generator are still
useful, port the behavior into `Lakona.Game.Server.Generators` in a later,
separate task. Do not keep the old generator as a hidden dependency.

## Tests

Do not throw away runtime behavior tests.

Move useful tests from:

```txt
tests/Lakona.Actor.Tests
```

into:

```txt
tests/Lakona.Game.Server.Tests/ActorKernel/
```

Runtime tests should exercise the internal kernel directly using
`InternalsVisibleTo("Lakona.Game.Server.Tests")`.

Keep or port coverage for:

- sequential message ordering
- mailbox capacity and backpressure
- rejected sends
- call response success
- call queue timeout
- call response timeout
- circular/self call prevention if the kernel still supports it
- timers
- stop/drain lifecycle
- mailbox metrics
- dead letters
- slow message diagnostics
- activity propagation
- observer/interceptor errors
- async-only disposal

Delete tests that only validate the removed public `Lakona.Actor` API shape,
such as public `ActorRef<T>` surface tests and `ActorClientGenerator` tests.

## Solution And Packaging

Update `Lakona.slnx`:

- remove `src/Lakona.Actor/Lakona.Actor.csproj`
- remove `src/Lakona.Actor.SourceGenerator/Lakona.Actor.SourceGenerator.csproj`
- remove `tests/Lakona.Actor.Tests/Lakona.Actor.Tests.csproj`

Delete:

```txt
src/Lakona.Actor
src/Lakona.Actor.SourceGenerator
tests/Lakona.Actor.Tests
docs/actor
```

The publish workflow currently packs every `src/*/*.csproj`. Deleting these
projects is enough to stop publishing `Lakona.Actor`.

`src/Lakona.Game.Server/Lakona.Game.Server.csproj` must remove:

```xml
<ProjectReference Include="..\Lakona.Actor\Lakona.Actor.csproj" />
```

No replacement package reference should be added.

## Documentation

Current documentation must stop presenting `Lakona.Actor` as a product. Update:

- `README.md`
- `CONTRIBUTING.md`
- `docs/lakona-monorepo.md`
- `docs/game/lakona-actor-boundary.md`
- `src/Lakona.Game.Server/README.md`
- package READMEs generated by templates if they mention standalone
  `Lakona.Actor`

Delete or archive:

- `docs/actor/overview.md`
- `docs/actor/design-philosophy.md`
- `src/Lakona.Actor/README.md`
- `src/Lakona.Actor.SourceGenerator/README.md`

Permanent docs should describe:

```txt
Lakona.Game.Server.Actors is the only game-facing actor API.
The internal ActorKernel is an implementation detail of Lakona.Game.Server.
```

Historical docs may still mention former `Lakona.Actor` in:

- `CHANGELOG.md`
- `docs/maintenance/imported-contributing-notes.md`

## Migration Order

The implementation should proceed in this order:

1. Move runtime code into `Lakona.Game.Server.Internal.ActorKernel` while old
   projects still exist.
2. Convert `LakonaActorRuntime` to the internal kernel.
3. Move and adapt runtime tests.
4. Delete old actor projects and generator.
5. Update solution, docs, and publishing assumptions.

Do not delete `src/Lakona.Actor` first. That makes the change harder to debug.

## Validation

The change is complete when:

- `src/Lakona.Actor` does not exist.
- `src/Lakona.Actor.SourceGenerator` does not exist.
- `tests/Lakona.Actor.Tests` does not exist.
- `Lakona.slnx` has no `Lakona.Actor` project entries.
- `src/Lakona.Game.Server/Lakona.Game.Server.csproj` has no `Lakona.Actor`
  reference.
- `dotnet build Lakona.slnx --no-restore` passes.
- `dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj`
  passes.
- The full sequential test run passes.
- `pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1` passes.
- Current docs do not recommend installing or referencing `Lakona.Actor`.
- Current source outside `CHANGELOG.md` and historical imported docs has no
  `Lakona.Actor` namespace references.

## Non-Goals

Do not redesign remote actor routing in this change.

Do not redesign `Lakona.Game.Server.Actors` public APIs unless a type directly
leaks the old `Lakona.Actor` namespace.

Do not port old `ActorClientGenerator` features into
`Lakona.Game.Server.Generators` in this change. Delete the old generator and
keep the existing game generator path.

Do not preserve public `ActorSystem`, `ActorRef<T>`, or `ActorHandle<T>` as
compatibility aliases.

Do not create a new `Lakona.ActorKernel` package.
