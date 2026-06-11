# Starter Unity Shared Integration Decision

Status: accepted

Date: 2026-04-21

Scope: contributor-facing decision record for Unity RPC starter shared-code
consumption. This is not a user quick start.

Current use: keep this record when changing Unity/Tuanjie generated package
layout, `Shared` consumption, MemoryPack compatibility handling, or proposals to
replace the local UPM source link with a precompiled shared assembly.

## Context

`Lakona.Tool`'s RPC starter module generates three project areas:

- `Shared`
- `Server`
- `Client`

For Godot and server, `Shared` is consumed through the normal `.csproj` build path.

For Unity, the current starter design uses a local UPM package reference so the Unity client consumes `Shared` source code directly. This means Unity recompiles shared DTO/contracts when the shared source changes.

During Unity + `memorypack` validation, the problematic area was not the server or Godot pipeline. The instability came from Unity's script compilation path when `MemoryPack.Generator` participates in compilation. Formatter registration/runtime shape was not exposed consistently enough to rely on one fixed registration entry shape.

## Decision

The starter will keep Unity on the current source-linked shared workflow.

Specifically:

- Unity continues to consume `Shared` through the local UPM package source link.
- Server continues to reference `Shared.csproj`.
- Godot continues to reference `Shared.csproj`.
- We do not switch Unity to a prebuilt `Shared.dll` workflow at this time.

## Why

This decision was made after comparing two directions:

1. Keep Unity source-linked and add Unity-specific compatibility handling for `MemoryPack`.
2. Move Unity to a precompiled `Shared.dll` workflow so Unity no longer recompiles shared MemoryPack DTOs itself.

We intentionally chose option 1.

### Reasons for rejecting the precompiled DLL approach

- It would change the Unity workflow for all serializers, not only `memorypack`.
- After changing shared source, Unity would no longer rebuild `Shared` automatically.
- Users would need an explicit rebuild/sync step before Unity sees shared changes.
- This weakens the current developer experience of editing `Shared` and letting Unity recompile directly.
- It likely increases future friction if the Unity client later integrates `HybridCLR` or another hot-update boundary, because precompiled shared assemblies become another asset/boundary to manage.

### Why the chosen approach is acceptable

- The instability is Unity-specific, so the compatibility handling should stay Unity-specific.
- Server and Godot already use the healthier standard .NET build path and should remain untouched.
- Keeping Unity source-linked preserves the simpler authoring model for shared contracts and DTOs.

## Consequences

### Positive

- Unity shared development stays source-linked.
- Editing `Shared` remains the default workflow.
- Server and Godot architecture stay unchanged.
- Future hot-update design remains less constrained than with a precompiled shared DLL layer.

### Negative

- Unity still requires compatibility-oriented handling around MemoryPack registration/runtime shape.
- The current solution is pragmatic compatibility engineering, not a perfectly clean "single fixed registration path" design.

## Implementation Guidance

When working on starter architecture in the future:

- Treat "Unity precompiled shared DLL" as a rejected alternative unless requirements change substantially.
- Do not broaden Unity-specific fixes into server or Godot unless there is separate evidence they need the same change.
- Prefer preserving Unity source-link behavior over introducing explicit rebuild/sync steps.
- If revisiting this decision, evaluate HybridCLR/hot-update requirements before proposing any Unity shared prebuild workflow.

## Revisit Conditions

Revisit this decision only if one of the following becomes true:

- Unity source-linked compilation becomes fundamentally unworkable across supported versions.
- A future Unity packaging model requires precompiled shared assemblies anyway.
- HybridCLR or another hot-update architecture explicitly benefits from a different shared boundary and that benefit outweighs the workflow cost.

## Deferred Unity Issue

As of 2026-04-21, Unity fresh-start behavior for starter-generated `memorypack` projects is still not considered solved.

An explicit formatter-registration experiment was tried in the source after `0.2.36`:

- generating `SharedMemoryPackRegistration.cs` in `Shared`
- calling `SharedMemoryPackRegistration.RegisterAll()` from the generated Unity tester before creating `RpcClient`

That approach appeared to work in one controlled verification flow, but later failed again in fresh user-created projects and was reverted from source.

Current status:

- the Unity `memorypack` first-start/runtime issue remains open
- the source-linked Unity architecture decision still stands
- do not reuse the reverted explicit-registration approach as the default fix without new evidence
- defer a new design until there is time to investigate a more reliable solution
