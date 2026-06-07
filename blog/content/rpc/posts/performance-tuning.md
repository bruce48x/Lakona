+++
title = "Performance Tuning"
date = 2026-05-12T09:35:00+08:00
+++

This page only describes tuning directions supported by the current repository. The project has not published official benchmark numbers; do not state serializer or transport performance as absolute facts. Before production, measure with your own payloads, devices, network conditions, and engine versions.

## Prefer Observability Before Performance

For first integration, use `websocket + json`. JSON payloads make DTO shapes, field names, and server behavior easier to inspect. After the path is stable, evaluate `memorypack` or other transports.

When optimizing performance, measure these first:

- Single RPC round-trip latency.
- Requests per second.
- Serialized payload size.
- Unity / Godot client GC allocations.
- Server CPU, memory, and concurrent request queuing.
- Weak network, packet loss, disconnect, and reconnect behavior.

## Serializer Tradeoffs

`JsonRpcSerializer` uses `System.Text.Json` with `IncludeFields` enabled. It is useful for debugging and early integration, but payloads are usually larger and serialization cost is more affected by field names and object structure.

`MemoryPackRpcSerializer` uses MemoryPack and is better suited to compact binary payloads and performance-focused stages. The cost is stricter DTO annotations and versioning, and debugging is less direct than JSON.

Do not switch serializers without measurement. Capture real DTOs and call frequency first, then compare.

## Transport Tradeoffs

The current starter supports `tcp`, `websocket`, and `kcp`:

- WebSocket: suitable for projects that need HTTP/WebSocket infrastructure, proxies, or browser-like network environments. The starter's default path is `/ws`.
- TCP: suitable for direct long-lived connections, simple deployment, internal networks, or controlled networks.
- KCP: suitable for low-latency interaction over UDP, but it depends more heavily on network conditions and parameter validation.

`LoopbackTransport` is better for local tests and is not a production network transport.

## Keepalive Cost

Keepalive is disabled by default. When enabled, idle connections send pings according to `Interval` and disconnect if no inbound frame is received within `Timeout`. It adds a small amount of frames, timers, and background task cost, but detects half-open connections faster.

For mobile and weak-network scenarios, do not set the interval too low. Overly frequent heartbeats increase battery use, traffic, and server pressure.

## Payload Size

Reduce payloads for high-frequency RPCs first:

- Avoid sending complete large objects in high-frequency methods.
- Use pagination, deltas, or version-number synchronization for lists.
- Avoid putting logs, debug text, or repeated fields into production DTOs.
- For large compressible payloads, evaluate `EnableCompression`, but measure CPU cost and security boundaries.

`TransportSecurityConfig.MaxDecompressedFrameBytes` limits decompressed frame size. Production environments should not accept unbounded large payloads.

## Server Pressure Limits

`RpcServerLimits` currently provides:

- `MaxConcurrentRequestsPerSession`, default 64.
- `MaxQueuedRequestsPerSession`, default 256.
- `MaxPendingAcceptedConnections`, default from the connection acceptor.

When the queue is full, the server returns `RpcStatus.Overloaded` and an overload message. Before increasing these values, confirm whether service methods block, access shared locks, or create more memory pressure.

## Current Benchmark Status

The current repository does not publish cross-transport / cross-serializer benchmark reports. Performance documentation can provide measurement directions, but should not claim that one combination is faster in every scenario.
