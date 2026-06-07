+++
title = "Threading Model"
date = 2026-05-12T09:15:00+08:00
+++

The Lakona.Rpc runtime uses background `Task`s for receive, push, request dispatch, and keepalive. It does not automatically switch back to the Unity, Tuanjie, or Godot main thread.

## Client Threading Model

`RpcClientRuntime.StartAsync` starts:

- receive loop: reads transport frames and handles responses, pushes, and keepalive ping / pong.
- notification loop: reads server notification frames from an internal queue and invokes registered notification handlers.
- keepalive loop: sends pings and detects timeouts when keepalive is enabled.

Notification handlers run on the runtime's notification loop. The code comments are explicit: notification handlers are not marshaled to the Unity main thread.

For normal RPC calls, the `await` continuation is determined by the caller context. However, the runtime uses `ConfigureAwait(false)` heavily, so do not rely on it to return you to the engine main thread.

## Server Threading Model

Each `RpcSession` has a receive loop. When a request arrives, the session adds request handling to tracked tasks and uses two limits to control pressure:

- `MaxConcurrentRequestsPerSession`, default 64.
- `MaxQueuedRequestsPerSession`, default 256.

Multiple requests from the same session may execute concurrently. If service implementations access shared state, they need locking, an actor / queue, or state isolated to the session scope.

Generated server binders usually create session-scoped services through `GetOrAddScopedService`. The same service instance may be used concurrently by requests from that session, so it also needs to be thread-safe.

## Unity / Tuanjie Main-Thread Responsibility

Do not directly access UnityEngine objects from notification handlers, background RPC continuations, or service notifications unless you have confirmed you are on the main thread.

Recommended pattern:

- Keep a connection state machine in a MonoBehaviour.
- Let network callbacks write only to a thread-safe queue or record lightweight state.
- Drain the queue in `Update()` and update UI, GameObjects, or the Scene.
- Use `CancellationTokenSource` for pending RPCs tied to button clicks, scene exit, or object destruction.

## Godot Main-Thread Responsibility

Godot C# node lifecycle methods such as `_Ready`, `_Process`, and `_ExitTree` run on the Godot main thread. Lakona.Rpc callbacks are not guaranteed to run on that thread.

When updating Node, SceneTree, or UI objects, hand network results off to main-thread logic. The default Godot tester script only performs a simple connection and logging; real projects should add a connection state machine and main-thread dispatch.

## Not Implemented Today

The current repository does not include a Unity main-thread dispatcher, Godot main-thread dispatcher, `SynchronizationContext` capture strategy, single-threaded service actor model, or per-method serial execution declarations.
