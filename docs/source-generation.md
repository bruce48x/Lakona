# RPC Source Generation

Lakona.Rpc uses Roslyn source generators as the normal RPC glue route for
generated Lakona projects and hand-written applications.

## Contract

RPC service interfaces, method ids, notification contracts, and DTOs live in a
shared assembly. `Lakona.Rpc.Analyzers` reads those contracts at compilation
time and emits:

- client facades and service clients
- notification binders
- server binders
- generated binder assembly metadata

Generated RPC glue is compiler output. New Lakona projects must not contain
project-local `Generated/` RPC source folders, codegen scripts, editor
postprocessors, or tool manifests for day-to-day RPC generation.

## Project Configuration

Generated server projects opt into server glue with:

```xml
<LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>
<LakonaRpcServerGeneratedNamespace>Server.App.Generated</LakonaRpcServerGeneratedNamespace>
```

Generated client projects opt into client glue with the matching client
generation property or Unity-compatible analyzer configuration.

Game client wrappers are an additional opt-in for projects that use
Lakona.Game:

```xml
<LakonaGameGenerateClient>true</LakonaGameGenerateClient>
<LakonaGameClientRuntime>godot</LakonaGameClientRuntime>
<LakonaGameClientPlatform>godot</LakonaGameClientPlatform>
<LakonaGameClientGameVersion>chat</LakonaGameClientGameVersion>
```

When enabled, the generator emits `LakonaGameClient` in the same namespace as
the generated `RpcClient`. The wrapper owns the generated `RpcClient`,
framework handshake, Game heartbeat, and static callback receiver type checks.
Business RPC services are exposed through `gameClient.Api`, so game client code
uses the generated wrapper as its single connection entry point.

Generated application code should look like this:

```csharp
using Rpc.Generated;

await using var gameClient = new LakonaGameClient(options, callbackReceiver);
await gameClient.ConnectAsync(cancellationToken);

var login = gameClient.Api.Shared.Login;
```

Users should not construct or store the generated `RpcClient`, build
`GameClientHello`, call handshake methods, create callback binding containers,
or hard-code framework heartbeat RPC ids. `RpcClient` remains a pure RPC client
and does not know about `LakonaGameClient`; the generated wrapper composes the
generated `RpcClient` with `Lakona.Game.Client.LakonaGameClientCore`.

The generated wrapper is single-use. `ConnectAsync` is the only normal
connection entry point and performs the full framework connection sequence:

1. validate the wrapper is not disposed and has not already started;
2. move the core state to connecting;
3. bind generated callbacks from the supplied receivers;
4. connect the internal RPC client;
5. run the framework game handshake;
6. apply `ServerHello` capabilities, including reliable push mode;
7. start the framework heartbeat when enabled;
8. mark the wrapper ready and open the `Api` gate.

`gameClient.Api` must stay closed until step 8 completes. Accessing it before a
successful connection throws:

```txt
LakonaGameClient is not connected. Call ConnectAsync first.
```

After a connection failure or disconnect, the wrapper is not reusable; dispose
it and create a new generated `LakonaGameClient`.

Generated constructors accept either raw RPC options or game client options:

```csharp
public LakonaGameClient(RpcClientOptions rpcOptions, params object[] callbackReceivers)
public LakonaGameClient(LakonaGameClientOptions gameOptions, params object[] callbackReceivers)
```

Callback receivers are optional, but null receivers are invalid. A receiver may
implement multiple generated callback contracts. A receiver that implements no
known generated callback contract is allowed and ignored. Supplying more than
one receiver for the same callback contract is invalid. The generator should
use static type checks for known callback contracts, not runtime
reflection-based discovery.

Game client metadata comes from MSBuild properties or assembly metadata. The
precedence is:

1. explicit MSBuild properties;
2. assembly metadata such as `[assembly: LakonaGameGenerateClient(...)]`;
3. generated defaults.

An explicit MSBuild `false` disables generation. Tool-generated Unity, Godot,
and console clients should enable game client generation by default and set
runtime, platform, and game-version metadata for the handshake.

## Ownership

- `Lakona.Rpc.Core` owns runtime attributes and shared contracts.
- `Lakona.Rpc.Analyzers` owns compile-time diagnostics and source generation.
- `Lakona.Tool` owns generated project files and package references, but does
  not write generated RPC glue as source files.
