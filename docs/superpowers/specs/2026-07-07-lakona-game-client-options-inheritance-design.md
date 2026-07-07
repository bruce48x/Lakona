# LakonaGameClientOptions Inheritance Design

Date: 2026-07-07
Status: accepted design

## Problem

Generated game clients currently expose two equivalent entry constructors:

```csharp
public LakonaGameClient(RpcClientOptions rpcOptions, params object[] callbackReceivers)
public LakonaGameClient(LakonaGameClientOptions options, params object[] callbackReceivers)
```

The first constructor is a convenience wrapper that internally allocates
`new LakonaGameClientOptions(rpcOptions)`. In practice, samples, tool templates,
and user code prefer the shorter form:

```csharp
new LakonaGameClient(new RpcClientOptions(transport, serializer), callbackReceiver);
```

That API shape is misleading:

- `RpcClientOptions` is the low-level RPC runtime configuration type. It belongs
  to `Lakona.Rpc.Client` and is the correct entry point for non-game RPC
  clients.
- `LakonaGameClient` is a game-framework client. It also needs game-layer
  settings such as `HeartbeatEnabled`, `HeartbeatInterval`, and
  `HeartbeatTimeout`.
- When users start from `RpcClientOptions`, they cannot configure heartbeat
  behavior without switching to a different type mid-setup.
- `LakonaGameClientOptions` currently composes `RpcClientOptions` through an
  `RpcOptions` property, which splits one logical client configuration across
  two objects.

The result is an API that looks simpler than it is and encourages users to
create game clients without the options type that actually owns game behavior.

## Product Decision

`LakonaGameClientOptions` becomes the single public configuration type for
generated `LakonaGameClient`.

Users should create game clients like this:

```csharp
await using var gameClient = new LakonaGameClient(
    new LakonaGameClientOptions(transport, serializer),
    callbackReceiver);
```

`RpcClientOptions` remains the configuration type for low-level RPC-only
clients such as `RpcClientRuntime` and non-game RPC samples. Game clients
should not take `RpcClientOptions` directly at the public constructor surface.

Early-development compatibility policy applies: remove the misleading
`LakonaGameClient(RpcClientOptions, ...)` constructor instead of keeping a
permanent dual-entry API.

## Recommended Design

### 1. Make `LakonaGameClientOptions` inherit `RpcClientOptions`

`Lakona.Game.Client` already depends on `Lakona.Rpc.Client`. The game client
options type should extend the RPC options type rather than wrap it.

Target shape:

```csharp
public sealed class LakonaGameClientOptions : RpcClientOptions
{
    public LakonaGameClientOptions(ITransport transport, IRpcSerializer serializer)
        : base(transport, serializer)
    {
    }

    public bool HeartbeatEnabled { get; set; } = true;

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(45);

    public new LakonaGameClientOptions UseSecurity(Action<TransportSecurityConfig> configure)
    {
        base.UseSecurity(configure);
        return this;
    }
}
```

Remove:

- `RpcOptions` property
- `LakonaGameClientOptions(RpcClientOptions rpcOptions)` wrapper constructor

After the change, game client configuration uses the inherited RPC surface
directly:

```csharp
var options = new LakonaGameClientOptions(transport, serializer)
{
    KeepAlive = RpcKeepAliveOptions.Enabled(interval, timeout),
    LoggerFactory = loggerFactory,
    HeartbeatInterval = TimeSpan.FromSeconds(10),
    HeartbeatTimeout = TimeSpan.FromSeconds(30),
};
```

### 2. Unseal `RpcClientOptions`

`RpcClientOptions` is currently `sealed`. Inheritance requires removing
`sealed` from the RPC options class.

`RpcClientOptions` stays in `Lakona.Rpc.Client` and remains the base type for
RPC transport configuration. Only `LakonaGameClientOptions` should inherit it
in the first implementation. Do not move heartbeat settings into
`RpcClientOptions`.

`docs/api-stability/public-api-boundaries.md` should note that
`RpcClientOptions` is intentionally unsealed and `LakonaGameClientOptions` is
the only supported game-layer subclass.

### 3. Keep transport heartbeat and game heartbeat separate

The unified options object will expose two different heartbeat concepts. The
design must document them clearly and must not merge their semantics.

| Setting | Layer | Purpose |
| --- | --- | --- |
| `KeepAlive` on `RpcClientOptions` | RPC transport | Frame-level ping/pong and transport disconnect detection |
| `HeartbeatEnabled` / `HeartbeatInterval` / `HeartbeatTimeout` on `LakonaGameClientOptions` | Game framework | `GameHeartbeat` RPC used for session liveness and state-loss detection |

Naming stays as-is for this change. Documentation and XML comments must explain
the distinction so users do not treat `KeepAlive` and `HeartbeatInterval` as
duplicates.

### 4. Generated `LakonaGameClient` constructor surface

`Lakona.Rpc.Analyzers` should emit only:

```csharp
public LakonaGameClient(LakonaGameClientOptions options, params object[] callbackReceivers)
```

Remove the generated overload:

```csharp
public LakonaGameClient(RpcClientOptions rpcOptions, params object[] callbackReceivers)
```

Generated client construction should pass the options object directly into the
generated `RpcClient`:

```csharp
return new Client.Generated.RpcClient(_options, bindings);
```

Replace current `_options.RpcOptions` usage.

### 5. `LakonaGameClientCore` and heartbeat loop

`LakonaGameClientCore.StartHeartbeat(RpcClientRuntime, LakonaGameClientOptions)`
and `LakonaGameHeartbeatLoop` already consume `LakonaGameClientOptions`.
No behavioral change is required beyond removing `.RpcOptions` call sites.

### 6. Tool-generated client templates

`Lakona.Tool` Unity, Tuanjie, Godot, and console client templates should stop
creating `RpcClientOptions` for game client entry points.

Rename helper methods where appropriate:

- `CreateRpcClientOptions()` -> `CreateLakonaGameClientOptions()`
- generated `LoginClient(RpcClientOptions options)` -> `LoginClient(LakonaGameClientOptions options)`

Generated sample code should construct:

```csharp
new LakonaGameClient(CreateLakonaGameClientOptions(), callbackReceiver)
```

not `new RpcClientOptions(...)`.

Tuanjie uses the same Unity client templates through `UnityClientRenderer`.

### 7. Documentation updates

Update these docs as part of implementation:

- `src/Lakona.Game.Client/README.md`
- `docs/source-generation.md`
- `docs/api-stability/public-api-boundaries.md`
- any sample README that shows `new LakonaGameClient(new RpcClientOptions(...))`

Document that:

- `LakonaGameClientOptions` is the game client configuration type
- `RpcClientOptions` remains valid for RPC-only clients
- game heartbeat and transport keepalive are separate concerns on the same object

## Package Boundaries

| Package | Responsibility after change |
| --- | --- |
| `Lakona.Rpc.Client` | Owns `RpcClientOptions`, `RpcClientRuntime`, transport keepalive, RPC request logging |
| `Lakona.Game.Client` | Owns `LakonaGameClientOptions`, `LakonaGameClientCore`, game heartbeat loop, session/reliable-push client state |
| `Lakona.Rpc.Analyzers` | Generates `LakonaGameClient` with the single game-options constructor |
| `Lakona.Tool` | Generates project code that uses `LakonaGameClientOptions` at the public entry point |

Do not add game heartbeat settings to `RpcClientOptions`.
Do not require game projects to reference lower-level RPC entry APIs for normal
client startup.

## Compatibility Boundary

This is an intentional public API cleanup.

Breaking changes:

- remove `LakonaGameClient(RpcClientOptions, ...)`
- remove `LakonaGameClientOptions.RpcOptions`
- remove `LakonaGameClientOptions(RpcClientOptions)`
- unseal `RpcClientOptions` (source-compatible for most callers, but public
  inheritance is now allowed)

Non-breaking or low-impact:

- existing `RpcClientRuntime(new RpcClientOptions(...))` call sites remain valid
- RPC-only samples that do not use `LakonaGameClient` are unaffected

Migration rule:

```csharp
// before
var rpcOptions = new RpcClientOptions(transport, serializer);
var client = new LakonaGameClient(rpcOptions, receiver);

// after
var options = new LakonaGameClientOptions(transport, serializer);
var client = new LakonaGameClient(options, receiver);
```

### Migration for fluent `UseSecurity()`

Tool and Godot samples currently use:

```csharp
return new RpcClientOptions(transport, serializer).UseSecurity(ConfigureTransportSecurity);
```

After the change, prefer:

```csharp
return new LakonaGameClientOptions(transport, serializer)
    .UseSecurity(ConfigureTransportSecurity);
```

`LakonaGameClientOptions` should hide base `UseSecurity()` with `new` and return
`LakonaGameClientOptions` so fluent configuration keeps working.

### Migration for pre-built `RpcClientOptions`

When an existing `RpcClientOptions` instance is already configured:

```csharp
var options = new LakonaGameClientOptions(old.Transport, old.Serializer)
{
    KeepAlive = old.KeepAlive,
    LoggerFactory = old.LoggerFactory,
};

if (old.Security.IsEnabled)
{
    options.UseSecurity(security =>
    {
        security.EnableCompression = old.Security.EnableCompression;
        security.CompressionThresholdBytes = old.Security.CompressionThresholdBytes;
        // copy other configured security fields as needed
    });
}
```

## Implementation Scope

### In scope

- `src/Lakona.Rpc.Client/Configuration/RpcClientOptions.cs`
- `src/Lakona.Game.Client/LakonaGameClientOptions.cs`
- `src/Lakona.Rpc.Analyzers/SourceGeneration/LakonaRpcSourceGenerator.cs`
- `src/Lakona.Tool/Rendering/Client/UnityClientCodeTemplates.cs`
- `src/Lakona.Tool/Rendering/Client/GodotClientCodeTemplates.cs`
- `src/Lakona.Tool/Rendering/Client/ConsoleClientCodeTemplates.cs`
- `samples/Game.Unity.Agar/Client/Assets/Scripts/Rpc/WebSocketRpcClientFactory.cs`
- `samples/Game.Unity.Agar/Client/Assets/Scripts/Rpc/KcpRpcClientFactory.cs`
- `samples/Game.Godot.Chat/Client/Scripts/Login/LoginClient.cs`
- `samples/Game.Godot.Chat/Client/Scripts/Login/LoginScene.cs`
- tests under `Lakona.Game.Client.Tests`, `Lakona.Rpc.Analyzers.Tests`,
  `Lakona.Tool.Tests`
- docs listed above

### Out of scope

- changing default heartbeat values
- adding appsettings-driven client configuration
- Unity-specific logger factory wiring for `Lakona.Rpc.Client.Request`
- renaming `HeartbeatInterval` / `HeartbeatTimeout`
- introducing a new client builder or fluent factory beyond constructor-based
  options

## Test Plan

1. Unit tests for `LakonaGameClientOptions` inheritance and removed wrapper API.
2. Analyzer tests assert generated `LakonaGameClient`:
   - exposes only `LakonaGameClientOptions` constructor
   - constructs `RpcClient` with `_options`, not `_options.RpcOptions`
   - does not emit `LakonaGameClient(RpcClientOptions`
3. Game client heartbeat tests updated to construct options directly.
4. Tool rendering tests assert generated Unity/Godot/console clients use
   `LakonaGameClientOptions`.
5. Repository validation:
   - `dotnet build Lakona.slnx`
   - `dotnet test Lakona.slnx --no-build`
   - `pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1`
6. Sample grep guard: no remaining `new LakonaGameClient(new RpcClientOptions`
   in maintained samples unless explicitly documented as anti-pattern tests.

## Versioning

Expect package version bumps for publishing-impacting packages:

- `Lakona.Rpc.Client` (unseal base options type)
- `Lakona.Game.Client` (public options shape change)
- `Lakona.Rpc.Analyzers` (generated constructor surface change)
- `Lakona.Tool` (generated template output change)
- downstream consumers required by package version graph guard

## Risks

| Risk | Mitigation |
| --- | --- |
| Users confuse transport `KeepAlive` with game heartbeat | Document clearly in README and XML comments; keep names unchanged but explain in spec and docs |
| External hand-written clients still use removed constructor | Acceptable in early development; update maintained samples and generated templates |
| Subclassing `RpcClientOptions` for unrelated types | Keep class non-sealed but document that only `LakonaGameClientOptions` is supported game extension |
| Generated client code churn across samples | Update tool templates and backport affected samples in the same change |
| `UseSecurity()` fluent chaining loses derived type | Add `new LakonaGameClientOptions UseSecurity(...)` override |

## Acceptance Criteria

- A new game client can be created with one `LakonaGameClientOptions` object
  that configures transport, serializer, RPC logging, transport keepalive, and
  game heartbeat in one place.
- Generated `LakonaGameClient` no longer exposes `RpcClientOptions` at its
  public constructor surface.
- `RpcClientOptions` remains the entry type for RPC-only clients.
- Tool-generated Unity, Godot, Tuanjie, and console clients compile without
  referencing the removed constructor.
- Tests and package version graph guard pass.
