# Merge Lakona.Actor Into Lakona.Game.Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the standalone `Lakona.Actor` product boundary and move its useful runtime into `Lakona.Game.Server` as an internal actor execution kernel.

**Architecture:** `Lakona.Game.Server.Actors` remains the only public game-facing actor API. The old `Lakona.Actor` runtime code moves into `src/Lakona.Game.Server/Internal/ActorKernel` as internal implementation. The old `Lakona.Actor.SourceGenerator` and public native actor API are deleted.

**Tech Stack:** .NET 10, C#, MSBuild project references, xUnit v3 tests, internal namespaces, `InternalsVisibleTo`, GitHub Actions publish-by-project scan.

---

## Required Reading

Read these before editing:

- `docs/superpowers/specs/2026-06-07-merge-actor-into-game-server-design.md`
- `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`
- `src/Lakona.Game.Server/Actors/ActorRuntimeOptions.cs`
- `src/Lakona.Game.Server/Diagnostics/MessageRecordingInterceptor.cs`
- `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- `src/Lakona.Actor/Core/ActorSystem.cs`
- `src/Lakona.Actor/Messaging/ActorRef.cs`
- `src/Lakona.Actor/Messaging/ActorHandle.cs`
- `src/Lakona.Actor/Mailbox/Mailbox.cs`
- `tests/Lakona.Actor.Tests/ActorSystemTests.cs`
- `tests/Lakona.Game.Server.Tests/ActorRuntimeTests.cs`

## Hard Rules

- Do not keep `Lakona.Actor` as a published package.
- Do not keep `Lakona.Actor.SourceGenerator`.
- Do not preserve `ActorSystem`, `ActorRef<T>`, or `ActorHandle<T>` as public compatibility aliases.
- Do not expose the internal kernel namespace from `Lakona.Game.Server`.
- Do not redesign remote actor routing in this change.
- Do not delete tests first. Migrate useful behavior tests before deleting old test projects.
- Commit after each task.

## Task 1: Add Internal ActorKernel Skeleton And Friend Access

**Files:**

- Create: `src/Lakona.Game.Server/Internal/ActorKernel/.gitkeep` only if empty directories are needed
- Modify: `src/Lakona.Game.Server/Properties/AssemblyInfo.cs` or create it
- Test: no behavior test yet

- [ ] **Step 1: Check for existing AssemblyInfo**

Run:

```powershell
Test-Path src\Lakona.Game.Server\Properties\AssemblyInfo.cs
```

If it returns `False`, create the directory:

```powershell
New-Item -ItemType Directory -Force -Path src\Lakona.Game.Server\Properties
```

- [ ] **Step 2: Add InternalsVisibleTo**

If `src/Lakona.Game.Server/Properties/AssemblyInfo.cs` does not exist, create it:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Lakona.Game.Server.Tests")]
```

If it already exists, add only this line:

```csharp
[assembly: InternalsVisibleTo("Lakona.Game.Server.Tests")]
```

Do not remove existing assembly attributes.

- [ ] **Step 3: Commit**

Run:

```powershell
git add src/Lakona.Game.Server/Properties/AssemblyInfo.cs
git commit -m "Expose Game.Server internals to tests"
```

## Task 2: Move Runtime Files Into Internal ActorKernel

**Files:**

- Create: `src/Lakona.Game.Server/Internal/ActorKernel/**`
- Keep temporarily: `src/Lakona.Actor/**`

- [ ] **Step 1: Create target directory**

Run:

```powershell
New-Item -ItemType Directory -Force -Path `
  src\Lakona.Game.Server\Internal\ActorKernel\Abstractions, `
  src\Lakona.Game.Server\Internal\ActorKernel\Core, `
  src\Lakona.Game.Server\Internal\ActorKernel\Core\Diagnostics, `
  src\Lakona.Game.Server\Internal\ActorKernel\Core\Dispatch, `
  src\Lakona.Game.Server\Internal\ActorKernel\Core\Lifecycle, `
  src\Lakona.Game.Server\Internal\ActorKernel\Core\Registry, `
  src\Lakona.Game.Server\Internal\ActorKernel\Diagnostics, `
  src\Lakona.Game.Server\Internal\ActorKernel\Lifecycle, `
  src\Lakona.Game.Server\Internal\ActorKernel\Mailbox, `
  src\Lakona.Game.Server\Internal\ActorKernel\Messaging, `
  src\Lakona.Game.Server\Internal\ActorKernel\Timers
```

- [ ] **Step 2: Copy runtime files**

Copy these folders and files from `src/Lakona.Actor` into the matching
`Internal/ActorKernel` directories:

```txt
src/Lakona.Actor/Abstractions/IActor.cs
src/Lakona.Actor/Abstractions/IActorCore.cs
src/Lakona.Actor/Core/**
src/Lakona.Actor/Diagnostics/**
src/Lakona.Actor/IActorMessageInterceptor.cs
src/Lakona.Actor/Lifecycle/**
src/Lakona.Actor/Mailbox/**
src/Lakona.Actor/Messaging/**
src/Lakona.Actor/Timers/**
src/Lakona.Actor/ActorState.cs
```

Do not copy:

```txt
src/Lakona.Actor/Abstractions/ActorClientAttribute.cs
src/Lakona.Actor/Lakona.Actor.csproj
src/Lakona.Actor/README.md
```

`ActorClientAttribute` belongs to the deleted source-generator surface.

- [ ] **Step 3: Change namespaces**

In every copied `.cs` file under
`src/Lakona.Game.Server/Internal/ActorKernel`, replace namespace declarations:

```csharp
namespace Lakona.Actor;
namespace Lakona.Actor.Abstractions;
namespace Lakona.Actor.Core;
namespace Lakona.Actor.Lifecycle;
namespace Lakona.Actor.Mailbox;
namespace Lakona.Actor.Messaging;
namespace Lakona.Actor.Timers;
```

with:

```csharp
namespace Lakona.Game.Server.Internal.ActorKernel;
namespace Lakona.Game.Server.Internal.ActorKernel.Abstractions;
namespace Lakona.Game.Server.Internal.ActorKernel.Core;
namespace Lakona.Game.Server.Internal.ActorKernel.Lifecycle;
namespace Lakona.Game.Server.Internal.ActorKernel.Mailbox;
namespace Lakona.Game.Server.Internal.ActorKernel.Messaging;
namespace Lakona.Game.Server.Internal.ActorKernel.Timers;
```

Update all `using Lakona.Actor...` in copied files to the new internal
namespaces.

- [ ] **Step 4: Make copied public types internal**

In copied files, change public kernel-facing types to `internal`.

At minimum change:

```csharp
public sealed class ActorSystem
public sealed class ActorRef<TMessage>
public sealed class ActorHandle<TMessage>
public sealed class ActorContext<TMessage>
public readonly record struct ActorId
public sealed class ActorSystemOptions
public sealed class ActorSpawnOptions
public sealed class ActorCallOptions
public sealed class ActorCallTimeout
public sealed class DeadLetter
public sealed class SlowMessage
public sealed class ActorObserverError
public readonly record struct MailboxMetrics
public enum ActorState
public enum ActorSendResult
public enum ActorStopResult
public interface IActor<TMessage>
public interface IActorCore
public interface IActorMessageInterceptor
```

to internal equivalents:

```csharp
internal sealed class ActorSystem
internal sealed class ActorRef<TMessage>
internal sealed class ActorHandle<TMessage>
internal sealed class ActorContext<TMessage>
internal readonly record struct ActorId
internal sealed class ActorSystemOptions
internal sealed class ActorSpawnOptions
internal sealed class ActorCallOptions
internal sealed class ActorCallTimeout
internal sealed class DeadLetter
internal sealed class SlowMessage
internal sealed class ActorObserverError
internal readonly record struct MailboxMetrics
internal enum ActorState
internal enum ActorSendResult
internal enum ActorStopResult
internal interface IActor<TMessage>
internal interface IActorCore
internal interface IActorMessageInterceptor
```

Do not rename types yet. First make the moved code compile internally.

- [ ] **Step 5: Build Game.Server and observe compile errors**

Run:

```powershell
dotnet build src\Lakona.Game.Server\Lakona.Game.Server.csproj
```

Expected result: there may be compile errors in copied kernel files from
namespace or accessibility mismatches. Fix only copied kernel files in this
task. Do not change `LakonaActorRuntime` yet.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/Lakona.Game.Server/Internal/ActorKernel src/Lakona.Game.Server/Properties/AssemblyInfo.cs
git commit -m "Move actor runtime into Game.Server kernel"
```

## Task 3: Convert LakonaActorRuntime To The Internal Kernel

**Files:**

- Modify: `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`
- Modify: `src/Lakona.Game.Server/Actors/ActorRuntimeOptions.cs`
- Modify: `src/Lakona.Game.Server/Diagnostics/MessageRecordingInterceptor.cs`
- Modify if needed: internal kernel interceptor files
- Test: `tests/Lakona.Game.Server.Tests/ActorRuntimeTests.cs`

- [ ] **Step 1: Add a game-facing interceptor interface**

Create `src/Lakona.Game.Server/Actors/IActorMessageInterceptor.cs`:

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

- [ ] **Step 2: Update ActorRuntimeOptions**

In `src/Lakona.Game.Server/Actors/ActorRuntimeOptions.cs`, replace:

```csharp
public global::Lakona.Actor.IActorMessageInterceptor? MessageInterceptor { get; set; }
```

with:

```csharp
public IActorMessageInterceptor? MessageInterceptor { get; set; }
```

- [ ] **Step 3: Update MessageRecordingInterceptor**

In `src/Lakona.Game.Server/Diagnostics/MessageRecordingInterceptor.cs`, replace
the implemented interface:

```csharp
public sealed class MessageRecordingInterceptor : global::Lakona.Actor.IActorMessageInterceptor
```

with:

```csharp
public sealed class MessageRecordingInterceptor : IActorMessageInterceptor
```

Replace method signatures that use `global::Lakona.Actor.ActorId` with
`Lakona.Game.Server.Actors.ActorId`.

Remove the `_idMap` constructor dependency if it only exists to translate
kernel actor IDs into game actor IDs. The game-facing interceptor should receive
game actor IDs directly.

- [ ] **Step 4: Add an adapter inside LakonaActorRuntime**

In `src/Lakona.Game.Server/Actors/LakonaActorRuntime.cs`, add:

```csharp
using Lakona.Game.Server.Internal.ActorKernel;
```

Keep existing Game.Server actor namespace as is.

Inside `LakonaActorRuntime`, add a private adapter class if the internal kernel
still expects its own `IActorMessageInterceptor`:

```csharp
private sealed class KernelMessageInterceptorAdapter : Internal.ActorKernel.IActorMessageInterceptor
{
    private readonly LakonaActorRuntime _runtime;
    private readonly IActorMessageInterceptor _inner;

    public KernelMessageInterceptorAdapter(LakonaActorRuntime runtime, IActorMessageInterceptor inner)
    {
        _runtime = runtime;
        _inner = inner;
    }

    public ValueTask OnBeforeMessage(
        Internal.ActorKernel.ActorId actorId,
        string messageType,
        object? message,
        CancellationToken cancellationToken)
    {
        return _inner.OnBeforeMessage(_runtime.MapActorId(actorId), messageType, message, cancellationToken);
    }

    public ValueTask OnAfterMessage(
        Internal.ActorKernel.ActorId actorId,
        string messageType,
        object? message,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        return _inner.OnAfterMessage(_runtime.MapActorId(actorId), messageType, message, exception, cancellationToken);
    }
}
```

Adjust namespace qualification to match the actual internal kernel namespace.

- [ ] **Step 5: Replace global Lakona.Actor references**

In `LakonaActorRuntime.cs`, replace all `global::Lakona.Actor.*` references with
internal kernel types.

Use this mapping:

```txt
global::Lakona.Actor.ActorSystem              -> ActorSystem
global::Lakona.Actor.ActorSystemOptions       -> ActorSystemOptions
global::Lakona.Actor.ActorSpawnOptions        -> ActorSpawnOptions
global::Lakona.Actor.ActorHandle<T>           -> ActorHandle<T>
global::Lakona.Actor.ActorContext<T>          -> Internal.ActorKernel.ActorContext<T>
global::Lakona.Actor.ActorId                  -> Internal.ActorKernel.ActorId
global::Lakona.Actor.ActorCallOptions         -> ActorCallOptions
global::Lakona.Actor.ActorCallTimeout         -> ActorCallTimeout
global::Lakona.Actor.ActorCallTimeoutReason   -> ActorCallTimeoutReason
global::Lakona.Actor.ActorStopResult          -> ActorStopResult
global::Lakona.Actor.MailboxMetrics           -> MailboxMetrics
global::Lakona.Actor.ActorState               -> Internal.ActorKernel.ActorState
global::Lakona.Actor.ActorSendResult          -> ActorSendResult
global::Lakona.Actor.IActor<T>                -> Internal.ActorKernel.IActor<T>
global::Lakona.Actor.DeadLetter               -> DeadLetter
global::Lakona.Actor.SlowMessage              -> SlowMessage
```

If names conflict with public `Lakona.Game.Server.Actors.ActorState`, fully
qualify the internal kernel enum.

- [ ] **Step 6: Configure kernel interceptor**

Change `ActorSystemOptions` creation from:

```csharp
MessageInterceptor = options.MessageInterceptor
```

to:

```csharp
MessageInterceptor = options.MessageInterceptor is null
    ? null
    : new KernelMessageInterceptorAdapter(this, options.MessageInterceptor)
```

- [ ] **Step 7: Build Game.Server**

Run:

```powershell
dotnet build src\Lakona.Game.Server\Lakona.Game.Server.csproj
```

Expected result: pass. If it fails, fix only namespace/type mapping errors.

- [ ] **Step 8: Run existing Game.Server actor tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~Actor"
```

Expected result: pass.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src/Lakona.Game.Server tests/Lakona.Game.Server.Tests
git commit -m "Use internal actor kernel from Game.Server"
```

## Task 4: Migrate Runtime Behavior Tests

**Files:**

- Create: `tests/Lakona.Game.Server.Tests/ActorKernel/ActorKernelTests.cs`
- Create optional helpers: `tests/Lakona.Game.Server.Tests/ActorKernel/ActorKernelTestActors.cs`
- Keep temporarily: `tests/Lakona.Actor.Tests/**`

- [ ] **Step 1: Create ActorKernel test directory**

Run:

```powershell
New-Item -ItemType Directory -Force -Path tests\Lakona.Game.Server.Tests\ActorKernel
```

- [ ] **Step 2: Copy the old runtime test file**

Copy:

```txt
tests/Lakona.Actor.Tests/ActorSystemTests.cs
```

to:

```txt
tests/Lakona.Game.Server.Tests/ActorKernel/ActorKernelTests.cs
```

- [ ] **Step 3: Update namespace and usings**

At the top of `ActorKernelTests.cs`, use:

```csharp
using Lakona.Game.Server.Internal.ActorKernel;
using Xunit;

namespace Lakona.Game.Server.Tests.ActorKernel;
```

Remove `namespace Lakona.Actor.Tests;`.

- [ ] **Step 4: Remove public API shape tests**

Delete tests from `ActorKernelTests.cs` that only assert the old public
`Lakona.Actor` API shape.

Remove tests with names or assertions about:

```txt
PublicApi
ActorRef public members
ActorSystem public members
ActorHandle implicit conversion
ActorClientAttribute
ActorClientGenerator
Analyzer
source generator
package API
```

Keep behavior tests about:

```txt
Send
TrySend
Call
Call timeout
Mailbox full
Message ordering
Timers
Stop/drain
DeadLetter
SlowMessage
ObserverError
Activity propagation
Metrics
Interceptor before/after
```

- [ ] **Step 5: Update expected diagnostic names only if necessary**

If copied tests assert activity or meter names like:

```txt
Lakona.Actor
Lakona.Actor.Actor.Dispatch
```

decide whether to keep them for continuity or change them to Game.Server names.
Recommended first pass: keep diagnostic source names unchanged to reduce
runtime behavior churn. A later observability rename can be separate.

- [ ] **Step 6: Run kernel tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter "FullyQualifiedName~ActorKernel"
```

Expected first result: compile errors are likely. Fix copied tests by updating
type names and namespaces. Do not reference `Lakona.Actor`.

- [ ] **Step 7: Commit**

Run:

```powershell
git add tests/Lakona.Game.Server.Tests/ActorKernel
git commit -m "Move actor kernel behavior tests into Game.Server"
```

## Task 5: Delete Actor Source Generator Surface

**Files:**

- Delete: `src/Lakona.Actor.SourceGenerator/**`
- Delete old generator tests from: `tests/Lakona.Actor.Tests/**`
- Modify: `Lakona.slnx`
- Modify: any project references

- [ ] **Step 1: Confirm Game.Server generator does not depend on old generator**

Run:

```powershell
rg -n "Lakona\\.Actor\\.SourceGenerator|ActorClientGenerator|ActorUsageAnalyzer|ActorClientAttribute" src\Lakona.Game.Server.Generators tests\Lakona.Game.Server.Generators.Tests src\Lakona.Game.Server -g '*.cs' -g '*.csproj'
```

Expected result: no required references. If there are references, remove or
replace them with `Lakona.Game.Server.Generators` concepts.

- [ ] **Step 2: Delete old generator project**

Run after checking the resolved path:

```powershell
$generator = Resolve-Path src\Lakona.Actor.SourceGenerator
Remove-Item -LiteralPath $generator.Path -Recurse -Force
```

- [ ] **Step 3: Remove generator project from solution**

In `Lakona.slnx`, delete:

```xml
<Project Path="src/Lakona.Actor.SourceGenerator/Lakona.Actor.SourceGenerator.csproj" />
```

- [ ] **Step 4: Commit**

Run:

```powershell
git add Lakona.slnx src/Lakona.Actor.SourceGenerator
git commit -m "Remove standalone actor source generator"
```

## Task 6: Remove Old Actor Project And References

**Files:**

- Delete: `src/Lakona.Actor/**`
- Delete: `tests/Lakona.Actor.Tests/**`
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Modify: `Lakona.slnx`

- [ ] **Step 1: Remove Game.Server project reference**

In `src/Lakona.Game.Server/Lakona.Game.Server.csproj`, delete:

```xml
<ProjectReference Include="..\Lakona.Actor\Lakona.Actor.csproj" />
```

Do not add a package reference.

- [ ] **Step 2: Remove old actor project and tests from solution**

In `Lakona.slnx`, delete:

```xml
<Project Path="src/Lakona.Actor/Lakona.Actor.csproj" />
<Project Path="tests/Lakona.Actor.Tests/Lakona.Actor.Tests.csproj" />
```

- [ ] **Step 3: Delete old actor directories**

Run after checking paths:

```powershell
$actor = Resolve-Path src\Lakona.Actor
$actorTests = Resolve-Path tests\Lakona.Actor.Tests
Remove-Item -LiteralPath $actor.Path -Recurse -Force
Remove-Item -LiteralPath $actorTests.Path -Recurse -Force
```

- [ ] **Step 4: Scan for source references**

Run:

```powershell
rg -n "Lakona\\.Actor|src/Lakona\\.Actor|tests/Lakona\\.Actor|ActorClientAttribute|ActorSystem|ActorRef<|ActorHandle<" src tests Lakona.slnx -g '!**/bin/**' -g '!**/obj/**'
```

Expected result:

- no `src/Lakona.Actor` or `tests/Lakona.Actor` paths
- no `Lakona.Actor` namespace references
- `ActorSystem` / `ActorRef<` may appear only inside
  `src/Lakona.Game.Server/Internal/ActorKernel` and its tests if the internal
  type names were not renamed

- [ ] **Step 5: Build Game.Server**

Run:

```powershell
dotnet build src\Lakona.Game.Server\Lakona.Game.Server.csproj
```

Expected result: pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/Lakona.Game.Server/Lakona.Game.Server.csproj Lakona.slnx src/Lakona.Actor tests/Lakona.Actor.Tests
git commit -m "Remove standalone Lakona.Actor project"
```

## Task 7: Update Docs And Package README Files

**Files:**

- Modify: `README.md`
- Modify: `CONTRIBUTING.md`
- Modify: `docs/lakona-monorepo.md`
- Modify: `docs/game/lakona-actor-boundary.md`
- Modify: `src/Lakona.Game.Server/README.md`
- Delete: `docs/actor/**`
- Historical exceptions: `CHANGELOG.md`, `docs/maintenance/imported-contributing-notes.md`

- [ ] **Step 1: Delete actor docs directory**

Delete:

```powershell
$actorDocs = Resolve-Path docs\actor
Remove-Item -LiteralPath $actorDocs.Path -Recurse -Force
```

- [ ] **Step 2: Update README**

In `README.md`, remove list items that present `Lakona.Actor` as a package.

Replace text like:

```md
- `Lakona.Actor` for process-local actor/mailbox execution.
```

with:

```md
- `Lakona.Game.Server.Actors` for game-facing actor execution backed by an internal mailbox kernel.
```

In the package list, remove:

```md
- `Lakona.Actor` for process-local actor runtime
```

- [ ] **Step 3: Update monorepo docs**

In `docs/lakona-monorepo.md`, replace any statement that says
`Lakona.Actor` owns a standalone runtime with:

```md
`Lakona.Game.Server` owns the game-facing actor API and its internal actor
kernel. Actor mailbox execution is an implementation detail of
`Lakona.Game.Server`, not a separate package boundary.
```

- [ ] **Step 4: Rewrite actor boundary doc**

In `docs/game/lakona-actor-boundary.md`, change the title to:

```md
# Lakona.Game Actor Boundary
```

Replace the old facade framing with:

```md
`Lakona.Game.Server.Actors` is the only public actor API for game code. The
runtime uses an internal actor kernel under `Lakona.Game.Server.Internal`, but
that kernel is not a package, not a public API, and not something generated
projects should reference directly.
```

Keep the responsibility split, but update it:

```txt
Internal ActorKernel                 Lakona.Game.Server.Actors
─────────────────────────────       ─────────────────────────────
Mailbox queue                       Game actor identity
Sequential dispatch                 Actor base class and context
Call/response slots                 IActorRuntime
Timers                              DI activation
Stop/drain lifecycle                Remote actor calls
Diagnostics mechanism               Cluster routing
Backpressure metrics                Message recording storage
```

- [ ] **Step 5: Update Game.Server README**

In `src/Lakona.Game.Server/README.md`, replace:

```md
It builds on Lakona.Actor
```

with:

```md
It provides a game-facing actor API backed by an internal mailbox kernel
```

Remove instructions that tell users to add `Lakona.Actor.SourceGenerator`
directly.

- [ ] **Step 6: Run docs scan**

Run:

```powershell
rg -n "Lakona\\.Actor|Lakona.Actor|src/Lakona\\.Actor|Actor.SourceGenerator|ActorClientAttribute" README.md CONTRIBUTING.md docs src -g '*.md' -g '!docs/maintenance/imported-contributing-notes.md' -g '!CHANGELOG.md'
```

Expected result: no current docs recommend or describe `Lakona.Actor` as a
current package. Historical imported docs are excluded.

- [ ] **Step 7: Run docs consistency check**

Run:

```powershell
pwsh -NoProfile -File scripts\rpc\check-docs-consistency.ps1
```

Expected result:

```txt
Documentation consistency check passed.
```

- [ ] **Step 8: Commit**

Run:

```powershell
git add README.md CONTRIBUTING.md docs src/Lakona.Game.Server/README.md
git commit -m "Document Game.Server actor boundary"
```

## Task 8: Update Publishing Assumptions And Pack Checks

**Files:**

- Modify if needed: `.github/workflows/publish-nuget.yml`

- [ ] **Step 1: Confirm publish workflow behavior**

The publish workflow packs all `src/*/*.csproj`. After deleting
`src/Lakona.Actor` and `src/Lakona.Actor.SourceGenerator`, it should naturally
stop publishing actor packages.

Run:

```powershell
Get-ChildItem -Path src -Depth 1 -Filter *.csproj | Select-Object FullName
```

Expected result:

- no `src\Lakona.Actor\Lakona.Actor.csproj`
- no `src\Lakona.Actor.SourceGenerator\Lakona.Actor.SourceGenerator.csproj`
- yes `src\Lakona.Game.Server\Lakona.Game.Server.csproj`

- [ ] **Step 2: Pack Game.Server**

Run:

```powershell
dotnet pack src\Lakona.Game.Server\Lakona.Game.Server.csproj -c Release -o artifacts\actor-kernel-pack-check
```

Expected result: `Lakona.Game.Server.*.nupkg` is created. There should be no
dependency on `Lakona.Actor` in the generated `.nuspec`.

- [ ] **Step 3: Inspect nuspec dependencies**

Run:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = Get-ChildItem artifacts\actor-kernel-pack-check\Lakona.Game.Server.*.nupkg | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$extract = Join-Path $env:TEMP ("lakona-game-server-pack-" + [guid]::NewGuid().ToString("N"))
[System.IO.Compression.ZipFile]::ExtractToDirectory($package.FullName, $extract)
rg -n "Lakona.Actor" $extract
```

Expected result: no matches.

- [ ] **Step 4: Commit if workflow changed**

If `.github/workflows/publish-nuget.yml` changed, commit:

```powershell
git add .github/workflows/publish-nuget.yml
git commit -m "Stop publishing standalone actor package"
```

If no file changed, do not create an empty commit.

## Task 9: Final Verification

**Files:**

- All touched files

- [ ] **Step 1: Run final source scan**

Run:

```powershell
rg -n "Lakona\\.Actor|src/Lakona\\.Actor|tests/Lakona\\.Actor|Actor.SourceGenerator|ActorClientAttribute" . -g '!**/bin/**' -g '!**/obj/**' -g '!CHANGELOG.md' -g '!docs/maintenance/imported-contributing-notes.md'
```

Expected result: no matches, except old text in committed historical docs if
the exclusion pattern misses them.

- [ ] **Step 2: Build solution**

Run:

```powershell
dotnet build Lakona.slnx --no-restore -m:1 /nr:false /p:UseSharedCompilation=false
```

Expected result: pass.

- [ ] **Step 3: Run Game.Server tests**

Run:

```powershell
dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-build
```

Expected result: pass.

- [ ] **Step 4: Run full sequential tests**

Run:

```powershell
$projects = Get-ChildItem -Path tests -Recurse -Filter '*.csproj' | Sort-Object FullName
foreach ($project in $projects) {
  dotnet test $project.FullName --no-build
  if ($LASTEXITCODE -ne 0) { throw "Tests failed for $($project.FullName)" }
}
```

Expected result: all test projects pass.

- [ ] **Step 5: Run docs check**

Run:

```powershell
pwsh -NoProfile -File scripts\rpc\check-docs-consistency.ps1
```

Expected result: pass.

- [ ] **Step 6: Check git status**

Run:

```powershell
git status --short
```

Expected result: no untracked build outputs, package extraction directories, or
temporary artifacts staged.

- [ ] **Step 7: Final cleanup commit**

If final verification required cleanup changes:

```powershell
git add .
git commit -m "Finish actor kernel consolidation"
```

If there are no changes after previous commits, do not create an empty commit.

## Completion Checklist

- [ ] `src/Lakona.Actor` is deleted.
- [ ] `src/Lakona.Actor.SourceGenerator` is deleted.
- [ ] `tests/Lakona.Actor.Tests` is deleted.
- [ ] `docs/actor` is deleted or fully replaced by Game.Server actor docs.
- [ ] `Lakona.slnx` has no actor project entries.
- [ ] `src/Lakona.Game.Server/Lakona.Game.Server.csproj` has no
      `Lakona.Actor` project or package reference.
- [ ] `Lakona.Game.Server.Actors` remains the public actor API.
- [ ] Internal kernel code lives under
      `src/Lakona.Game.Server/Internal/ActorKernel`.
- [ ] Former runtime behavior tests are represented under
      `tests/Lakona.Game.Server.Tests/ActorKernel`.
- [ ] Current docs do not recommend using `Lakona.Actor`.
- [ ] `dotnet build Lakona.slnx --no-restore` passes.
- [ ] `dotnet test tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj`
      passes.
