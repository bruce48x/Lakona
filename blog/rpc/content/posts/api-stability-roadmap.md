+++
title = "API Stability Roadmap"
date = 2026-05-15T00:00:00+08:00
+++

Lakona.Rpc is currently ready for a **soft freeze**: the main integration path, wire protocol direction, and package boundaries are mostly stable, but it is not yet ready to declare a full hard freeze or 1.0 API freeze.

This document records the current judgment and future optimization direction. It is not a one-time checklist; it is the basis for evaluating breaking changes before future releases.

## Current Judgment

Areas that can be stabilized first:

- C# contract-first workflow: `[RpcService]`, `[RpcMethod]`, `[RpcNotificationContract]`, `[RpcNotification]`
- Basic usage of the generated client facade, server binder, and notification binder
- Main user entry points such as `RpcClientOptions` and `RpcServerHostBuilder`
- Extension entry points such as `ITransport`, `IRpcSerializer`, `IRpcConnectionAcceptor`, `RpcAcceptedConnection`, and `TransportFrame`
- Package boundaries for TCP, WebSocket, KCP, and Loopback transports
- Replacement boundary between JSON and MemoryPack serializers
- Basic wire semantics for request / response / push / keepalive

Areas that should not be hard-frozen yet:

- Public commitment boundary for low-level frame/envelope/session types that still appear in API reference
- RPC status taxonomy and remaining error model details
- Restart and reuse semantics for runtime, transport, and session objects
- Naming rules and conflict handling for generated facades
- Generated server binding shape that currently exposes `RpcSession`

## API Commitment Layers

Future documentation and release notes should divide public APIs into five layers.

### Stable User API

This layer targets regular users. After a hard freeze, it should mostly receive only compatible additions.

- contract attributes
- starter-recommended client/server initialization
- generated `RpcClient` facade lifetime
- `RpcClientOptions`
- `RpcServerHostBuilder`
- `RpcServerHost`
- `RpcServerLimits`
- transport construction entry points
- serializer construction entry points
- `RpcKeepAliveOptions`
- `RpcException`
- `RpcStatus`

### Stable Extension API

This layer targets third-party transport, serializer, and connection acceptor authors. Official transport and serializer packages cannot cover every production environment, so these interfaces are long-term extension points.

- `ITransport`
- `IRpcSerializer`
- `IRpcConnectionAcceptor`
- `IRemoteEndPointProvider`
- `RpcAcceptedConnection`
- `TransportFrame`

This layer needs focused contract tests before hard freeze.

### Generated-Support API

This layer primarily supports source generator output. Users can see it and may call it in advanced scenarios, but its compatibility should be tied to matching versions of `Lakona.Rpc.Analyzers` and runtime packages.

- `IRpcClient`
- `RpcMethod<TArg, TResult>`
- `RpcNotificationMethod<TArg>`
- `RpcGeneratedServicesBinderAttribute`
- `RpcGeneratedServiceBinder`
- `RpcServiceRegistry`, until generator output no longer exposes registry binding directly

When this layer has a breaking change, releases must explicitly require users to rebuild so the source generator regenerates glue code, and they should avoid silent failures from combining a new runtime with old generated code.

### Runtime Internal API

This layer should not be presented as a user extension surface. If a type remains public temporarily, it should be hidden from normal user docs and documented as implementation support.

- `RpcSession`
- `RpcHandler`
- `RpcSessionHandler`
- direct `(serviceId, methodId)` handler registration
- low-level session notification sending

Lakona.Rpc does not support user-authored server hosts as the normal extension model. Server applications should use `RpcServerHostBuilder` and generated binders.

### Protocol and Infrastructure API

This layer is for protocol tools, tests, diagnostics, and package-internal cooperation. It is not a business application entry point.

- `RpcEnvelopeCodec`
- envelope/frame DTOs
- `RpcFrameType`
- `RpcProtocolLimits`
- `LengthPrefix`
- `TransportFrameCodec`
- `TransformingTransport`
- `TransportSecurityConfig`
- `PooledFrameBufferWriter`

If a type is public only for package-internal cooperation or tests, prefer narrowing its visibility. If it must remain public, add documentation and contract tests.

## Pre-Freeze Improvements

### 1. Clarify Public Commitment Boundaries

The current API Reference lists many low-level types as public APIs. Before freezing, decide:

- which types are stable user entry points
- which types are stable extension points
- which types only support generated code
- which types are runtime internal support
- which types can become internal or hide behind narrower facades

The goal is not to reduce every public type, but to prevent temporary public surface area from being mistaken for a long-term stability promise.

### 2. Strengthen the Error Model

`RpcStatus` is a framework-only status taxonomy: `Ok`, `NotFound`, `HandlerError`, `Overloaded`, `BadRequest`, and `ProtocolError`. Business failures stay in business DTOs.

Clients now throw `RpcException` for non-OK remote responses. `RpcException` is the dedicated framework exception for remote RPC failures and exposes:

- `RpcStatus Status`
- `string? ErrorMessage`
- request id
- service id
- method id

Before freezing, continue tightening where each status is produced, especially around decode failures and protocol violations. Business errors should still stay in application DTOs or business return models, not be forced into the low-level runtime.

### 3. Stabilize Server Notification API

Server notification registration is intentionally one-shot per generated notification method. It models a notification contract implementation, not a general event subscription system.

The runtime now uses `RegisterNotificationHandler(..., Func<T, ValueTask>)` as the core handler shape, keeps a synchronous convenience overload on `RpcClientRuntime`, and fails fast on duplicate registration. It does not provide unregistration and does not support multiple handlers for the same notification method. Applications that need fan-out should do that inside their notification implementation.

Handler exceptions and unhandled notification frames are observable through runtime events and do not disconnect the RPC transport by default. Unity, Tuanjie, and Godot main-thread dispatch remains an application-layer responsibility.

### 4. Lock Down Lifetime Semantics

The current lifecycle direction is:

- `RpcClientRuntime` and the generated `RpcClient` are single-use connection objects.
- `RpcSession` represents one accepted connection and cannot restart after stop, disconnect, timeout, or dispose.
- Cleanup methods such as `StopAsync` and `DisposeAsync` are idempotent, but idempotent cleanup does not mean restartability.
- `ConnectAsync` on accepted server transports initializes per-connection state over an already accepted connection.
- `ITransport.IsConnected` is a best-known local diagnostic signal, not a strongly consistent pre-send check.
- Application reconnect flows should create a new transport, options object, generated client/runtime, and server session.

Once users depend on these semantics, changing them becomes expensive.

### 5. Stabilize Generated Facade Naming Rules

Generated `RpcApi` derives group/property names from the contract namespace and service interface by default.

Long-lived contracts can now use `RpcServiceAttribute.ApiGroup` and `RpcServiceAttribute.ApiName` to explicitly lock `client.Api.<group>.<service>` names across namespace or interface refactors. Duplicate generated names fail source generation instead of receiving unstable numeric suffixes.

### 6. Keep Unity Dependency Constraints Accurate

`System.Threading.Channels` is an explicit dependency already used by the current runtime and Unity samples, and should remain on the allowed list.

`System.IO.Pipelines` may enter Unity-side package sets through transport or serializer dependency chains; its presence alone is no longer treated as a violation. Before adding or expanding related usage, validate it against Unity 2022 LTS, iOS, IL2CPP, and HybridCLR.

Unity client code should still avoid:

- `System.Reflection.Emit`
- runtime code generation
- JIT-only APIs

Before adding dependencies, confirm real compatibility with Unity 2022 LTS, iOS, IL2CPP, and HybridCLR.

## Phased Plan

### Soft Freeze Phase

The current phase should prioritize:

- no breaking wire protocol changes without a migration path
- stable starter generation paths
- generated code matching runtime versions
- compatible additions only for main entry APIs
- explicit upgrade instructions for breaking changes in the changelog

### Before Hard Freeze

Before 1.0 or a similar stability commitment, complete:

- API layer documentation
- finalized error model
- finalized callback registration model
- lifetime semantics documentation and contract tests
- finalized generated facade naming rules
- retention, narrowing, or annotation of low-level public types

### After Hard Freeze

After freezing, the project can still improve:

- performance and allocation behavior
- transport robustness
- starter template experience
- documentation and examples
- compatible new APIs

But it should avoid changing existing main entry signatures, generated API shape, and wire protocol. If a breaking change is truly required, use a major version, migration guide, and compatibility window.

## Release Checklist Questions

Before every release that touches runtime, source generator, or transport packages, check at least:

- Did the runtime API called by generated code change?
- Does the change require users to rebuild and refresh source-generated code?
- Did wire frame, service id, method id, request id, or payload semantics change?
- Did disconnect, dispose, pending request, or keepalive timeout behavior change?
- Did generated facade type names, namespaces, groups, or property names change?
- Was a Unity-side dependency added? Was IL2CPP compatibility verified?
- Does the changelog need to document a breaking change or migration step?

This roadmap should evolve with implementation progress. After completing a pre-freeze improvement, move it from a risk item to a stable convention and add the matching reference docs or tests.
