# Unity RPC Generation Zero-Config Design

Date: 2026-07-06
Status: accepted design

## Problem

Generated Unity and Tuanjie projects currently include:

```csharp
[assembly: LakonaRpcGenerateClient("Rpc.Generated")]
[assembly: LakonaGameGenerateClient("unity", "unity", "chat")]
```

in `Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs`.

That file is not business code. It is a framework generation switch placed in
the user's normal script tree. This creates two product problems:

- users do not have enough context to understand why the file exists;
- accidental edits can disable generated RPC clients, change namespaces, or
  break generated `LakonaGameClient` behavior.

The file also conflicts with the repository direction that generated RPC glue
and day-to-day generation mechanics should be compiler/framework output rather
than project-local source files.

## Product Decision

New Unity and Tuanjie projects must not generate
`LakonaRpcGeneration.cs` or a replacement user-maintained marker file.

Lakona should own RPC client generation for generated Unity clients:

- generated client namespace defaults to `Client.Generated`;
- generated game client wrapper is enabled by default for Lakona game clients;
- Unity-compatible platform metadata is inferred by the framework;
- game version should come from runtime options or framework defaults, not from
  a user-editable compile-time marker.

The user's Unity project should contain business scripts, scenes, UI assets,
package metadata, and shared contracts. It should not contain internal source
generation switches.

## Recommended Design

`Lakona.Rpc.Analyzers` should make Unity-aware client generation a default when
it can identify the correct client compilation.

For generated starter projects, the first supported target is Unity's default
`Assembly-CSharp` compilation. The generator should auto-enable client output
when all of these conditions hold:

- the compilation is Unity-compatible;
- the assembly is the main user script assembly, initially `Assembly-CSharp`;
- the compilation references the RPC client runtime;
- the compilation references Lakona game client runtime when the game wrapper is
  needed;
- the compilation sees shared RPC service contracts;
- the compilation does not reference the server runtime.

When those conditions hold, analyzer defaults should be equivalent to:

```xml
<LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>
<LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>
<LakonaGameGenerateClient>true</LakonaGameGenerateClient>
<LakonaGameClientPlatform>unity</LakonaGameClientPlatform>
```

`Client.Generated` is project-local client output, not framework API. The
namespace should not use `Lakona.*`, because that namespace family belongs to
framework packages and public framework surfaces. It should also avoid the old
`Rpc.Generated` default for generated Lakona game projects because the emitted
surface includes the game client wrapper, not only low-level RPC glue.

Generated projects should set `Platform` from the selected client engine:
`unity`, `unity-cn`, `tuanjie`, `godot`, or `dotnet`.

`LakonaGameClientRuntime` and `LakonaGameClientOptions.ClientRuntime` should be
removed from the generated-project model. `runtime` and `platform` currently do
not carry separate product meaning; keeping both makes the handshake metadata
harder to understand without adding behavior. `Platform` is enough for the
client host identity.

If the framework later needs to report the Lakona client package/runtime
version, add a specific field such as `ClientSdkVersion` instead of reusing the
ambiguous `ClientRuntime` concept.

`LakonaGameClientGameVersion` should stop being required for generated Unity
projects. The generated wrapper can keep accepting
`LakonaGameClientOptions.GameVersion`; if unset, it should use a framework
default that is not derived from `Assembly-CSharp`.

## Tool Changes

`Lakona.Tool` should remove these generated files from Unity/Tuanjie plans:

- `Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs`
- `Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs.meta`

Tool tests should assert that generated Unity/Tuanjie plans do not include
that file and do not include any replacement marker file under user scripts.

Generated project docs should stop telling users that Unity recompilation uses
`LakonaRpcGeneration.cs` markers. They should describe generated RPC APIs as
framework-owned compile-time output.

## Compatibility Boundary

Existing marker attributes can remain supported for hand-written or advanced
projects. Removing support is not required for this design.

The new zero-config path is the default for generated projects. Advanced Unity
projects with custom asmdef layouts are explicitly out of the first
implementation scope unless they already work through existing marker
attributes.

## Risks

The main risk is accidental generation in the wrong Unity assembly. The initial
rule deliberately limits auto-generation to `Assembly-CSharp` to avoid multiple
assemblies generating the same `Client.Generated` types.

The second risk is losing game-version identity. That value should not be
preserved by moving it into another hidden compile-time file. If the server
needs meaningful game-version semantics, the client runtime options or a
user-facing application setting should own it explicitly.

## Test And Validation Expectations

Focused tests should cover:

- Unity-compatible `Assembly-CSharp` auto-generates RPC clients with no marker;
- non-Unity SDK projects keep existing MSBuild-property behavior;
- Unity custom assemblies do not auto-generate by accident in the first scope;
- explicit marker attributes still work;
- explicit `LakonaGameGenerateClient=false` disables game wrapper generation;
- generated client code uses `Client.Generated`, not `Rpc.Generated` or
  `Lakona.*`;
- client handshake metadata uses `Platform` and no longer emits or requires
  `ClientRuntime`;
- `Lakona.Tool` Unity/Tuanjie plans omit `LakonaRpcGeneration.cs`;
- generated Unity sample scans do not contain `LakonaRpcGeneration.cs` or
  `[assembly: LakonaRpcGenerateClient]`.

Before implementation is considered complete, run the tool-focused tests and
the RPC analyzer tests. A full solution build remains the final confidence
check when package restore is available.
