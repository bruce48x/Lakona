# Hotfix Architecture

## Purpose

Lakona.Game hotfix lets a running game server replace request logic and actor
business behavior without restarting the process.

The model is:

```txt
stable runtime state + replaceable business logic
```

Long-lived state, actor mailboxes, RPC transport, session ownership,
persistence, logging, timers, and process lifecycle stay in stable assemblies.
Replaceable business logic lives in `Server.Hotfix` and is loaded through a
collectible `AssemblyLoadContext`.

## Core Concepts

Lakona uses Actor terminology, not ECS terminology. Public hotfix names must not
use Entity, Component, or System for the game-facing model.

| Concept | Assembly | Example | Responsibility |
| --- | --- | --- | --- |
| Service contract | `Shared` | `IChatService` | RPC interface shared by client and server |
| Service | `Server.Hotfix` | `ChatService` | Request business logic for a Shared service contract |
| Actor | `Server.App` | `ChatRoomActor` | Stable mailbox and fields only |
| Behavior | `Server.Hotfix` | `ChatRoomBehavior` | Hot-reloadable behavior for one actor type |
| Service proxy | `Server.App` | `ChatServiceProxy` | Stable RPC binding that forwards each call to current hotfix service logic |

The Service and Behavior concepts are deliberately separate:

- A Service corresponds to a `Shared` service interface. It handles request
  business logic and may call zero, one, or many actors.
- A Behavior corresponds one-to-one with an Actor. It runs inside an actor turn
  and reads or writes that actor's fields.
- A Service must not be named `*Behavior`.
- A Behavior must not become an RPC endpoint.

## Project Structure

```txt
Server.App (stable)                         Server.Hotfix (reloadable)
────────────────────                        ──────────────────────────
Program entry point                         ChatService
RPC service proxies                         ChatRoomBehavior
Actor fields and mailbox ownership          Service helpers
Hotfix dispatch bridge                      Request orchestration
Admin hotfix endpoint                       Replaceable rules
BuildTag metadata

Reference direction: Server.Hotfix -> Server.App and Shared
```

`Server.App` must not reference `Server.Hotfix`. It loads the hotfix assembly
dynamically through `HotfixManager`. No host, sample, tool template, or feature
discovery path may load hotfix assemblies with `Assembly.LoadFrom` into the
default `AssemblyLoadContext`.

## Request Flow

Each RPC session holds stable proxy instances, not hotfix service instances.
This guarantees already-connected clients use the newest service logic on their
next RPC call after a successful reload.

```txt
client RPC
  -> Server.App ChatServiceProxy
  -> current hotfix ChatService
  -> IActorRuntime Ask/Call for ChatRoomActor
  -> current ChatRoomBehavior inside the actor turn
  -> mutate ChatRoomActor fields
  -> return stable DTO/effects to the proxy/runtime
```

Existing calls use next-entry semantics. A call that already resolved a delegate
continues with that delegate. New proxy calls and new actor behavior calls see
the new dispatch table after a successful reload.

## Actor And Behavior Boundary

Actors are stable state holders and mailbox identities. User actor classes in
hotfix-enabled samples should contain fields and framework lifecycle hooks only.
Business decisions belong in the matching Behavior.

```csharp
// Server.App
internal sealed class ChatRoomActor : Actor
{
    internal readonly Dictionary<string, ChatRoomMember> Members = new();
    internal readonly Queue<ChatMessage> RecentMessages = new();
}
```

```csharp
// Server.Hotfix
[HotfixBehaviorOf(typeof(ChatRoomActor))]
internal static class ChatRoomBehavior
{
    public static ValueTask<LoginReply> LoginAsync(
        this ChatRoomActor self,
        ChatLoginCommand command)
    {
        // Reads and writes ChatRoomActor fields inside the actor turn.
    }
}
```

Behavior field access should use `internal` access with
`InternalsVisibleTo("Server.Hotfix")` or generated friend accessors. It should
not use runtime reflection for normal actor field access.

Hotfix code must not own long-lived timers, threads, static event
subscriptions, cached callbacks, or any object that can keep an old collectible
load context alive.

## Service Proxy Boundary

Stable service proxies are the RPC binding surface. They implement the `Shared`
contract and forward every call to the currently loaded hotfix Service through
the hotfix dispatch layer.

```txt
Shared.IChatService
  implemented by Server.App.ChatServiceProxy
  forwarded to Server.Hotfix.ChatService
```

This prevents RPC registries and existing sessions from holding instances of
types loaded from the hotfix assembly. It is required for old connections to use
new service logic after reload and for old hotfix load contexts to unload.

## BuildTag

`BuildTag` is the stable hotfix compatibility tag. It proves a hotfix package
was built against the same stable boundary as the running server.

The tag is explicitly managed. It must not change automatically on every build.
Update it only when the stable boundary visible to hotfix code changes:

- actor fields are added, removed, renamed, or retyped
- `Shared` service contracts or DTOs change
- hotfix dispatch or generated wrapper shape changes
- hotfix-visible internal stable types change incompatibly

Do not update it for pure hotfix logic changes, comments, docs, tests, or stable
implementation details that are invisible to hotfix code.

Recommended storage:

```txt
Server/App/BuildTag.props
```

```xml
<Project>
  <PropertyGroup>
    <LakonaHotfixBuildTag>20260612.001</LakonaHotfixBuildTag>
  </PropertyGroup>
</Project>
```

`Server.App` and `Server.Hotfix` import this file. `Server.App` exposes the
running `BuildTag` through assembly metadata and the loopback hotfix admin
status endpoint. `lakona-tool hotfix pack` writes the same tag into
`hotfix.json`. Production activation rejects packages whose `BuildTag` does not
match the running server.

## Development Workflow

Development optimizes for speed.

```txt
dotnet build Server/Hotfix/Server.Hotfix.csproj
  -> copy Server.Hotfix.dll, PDB, and deps to Server.App output hotfix directory
  -> write reload.signal last
  -> development server detects reload.signal
  -> HotfixManager.ReloadAsync()
```

Development may use a signal watcher with a lightweight polling fallback.
It must watch `reload.signal`, not the DLL itself, so the server does not load a
partially copied build output.

Development reload failures are logged and keep the previous dispatch table.
They may be warnings during local iteration.

## Production Workflow

Production optimizes for reliability. It does not use file watchers.

Normal v1 flow:

```txt
build or CI machine:
  lakona-tool hotfix pack

external deployment system:
  copy the package to each target node

target node:
  lakona-tool hotfix install Server.Hotfix-v20260612-153045Z.zip --root /app/hotfix
  lakona-tool hotfix activate v20260612-153045Z --server http://127.0.0.1:20090
  lakona-tool hotfix status --server http://127.0.0.1:20090
```

Lakona v1 does not provide remote deploy or multi-node orchestration. Operators
or deployment systems roll nodes by invoking the local commands on each node.

Production hotfix root:

```txt
hotfix/
  current.txt
  previous.txt
  staging/
  versions/
    v20260612-153045Z/
      Server.Hotfix.dll
      Server.Hotfix.pdb
      Server.Hotfix.deps.json
      hotfix.json
      checksums.sha256
      READY
```

`READY` is written last. A version directory without `READY` is not installable
or activatable.

Package names and version directories use UTC timestamps accurate to seconds:

```txt
Server.Hotfix-v20260612-153045Z.zip
v20260612-153045Z
```

## Local Admin Endpoint

Production activation is explicit and local.

The v1 admin endpoint:

- uses loopback HTTP JSON
- binds only to `127.0.0.1` or `::1`
- rejects non-loopback requests
- has no public authentication model
- is not a remote deploy channel

Required v1 endpoints:

```txt
GET  /_lakona/hotfix/status
POST /_lakona/hotfix/activate
POST /_lakona/hotfix/rollback
POST /_lakona/hotfix/reload
```

`activate` validates the target version in the running server process before it
publishes a new dispatch table:

```txt
1. acquire hotfix operation lock
2. verify version directory, READY, manifest, checksums, and BuildTag
3. dry-load the hotfix assembly without publishing
4. verify expectedCurrentVersion
5. write previous.txt = old current version
6. write current.txt = target version
7. call ReloadAsync()
8. on success, return current status
9. on failure, restore current.txt and keep old dispatch table
```

`rollback` activates `previous.txt`. It is ordinary activation of the previous
version, not a separate loading path.

`reload` reloads the version already named by `current.txt` and does not change
version pointers.

## Dispatch Publication Safety

Reload failure keeps the previous dispatch table active. A reload is successful
only after all of these checks pass:

- the resolved DLL exists and can be read completely
- the assembly loads in a collectible context
- scanning finds supported hotfix Service and Behavior methods
- duplicate dispatch keys are rejected
- boundary types come from shared/default assemblies
- `BuildTag` matches in production
- typed delegates for supported dispatch shapes can be created
- no hotfix assembly was loaded in the default context by the host

## Explicit Non-Goals

V1 does not include:

- remote upload or deploy from `lakona-tool`
- multi-node orchestration
- public management endpoints
- production file watchers
- hotfixing actor runtime internals
- hotfixing serializers, transports, or persistent schema
- hotfixing `Shared` contract shape without a stable deployment and `BuildTag`
  bump
- allowing hotfix code to own long-lived runtime resources
