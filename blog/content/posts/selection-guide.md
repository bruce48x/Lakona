+++
title = "Technology Selection Guide"
date = 2026-05-12T09:00:00+08:00
+++

Lakona.Rpc fits projects that share contracts between a C# server and a C# game client. Its core value is building a strongly typed communication boundary around the same C# interfaces and DTOs on both sides, while keeping transport, serializer, and runtime integration on a fixed workflow.

## Good Fits for Lakona.Rpc

Your server is .NET, and your client is Unity, Tuanjie Engine, or Godot C#.

You want `Shared` contracts to be the single source of truth, with both server and client compiling communication glue from the same C# DTOs and interfaces.

You need to choose between JSON and MemoryPack, and you want the transport to be swappable between TCP, WebSocket, and KCP.

You accept the boundary that connection state, reconnect, authentication, business error codes, and versioning strategy belong to the application layer.

You want the default template to generate server, client, Shared, source generator configuration, and a minimal connection test directly.

## Poor Fits for Lakona.Rpc

Your client is mainly not C#, such as TypeScript, C++, Java, Go, or a native mobile stack. The current repository is not a schema-first toolchain for cross-language IDL.

You need mature service-governance features such as service discovery, load balancing, streaming, deadline propagation, interceptor ecosystems, unified tracing, and cross-language tooling. Those needs are closer to gRPC or an internal RPC platform.

You need a built-in account system, authorization middleware, session resume, automatic reconnect, or request replay. Lakona.Rpc currently leaves these to the application layer.

You only need a very small number of messages and the protocol will not grow for a long time. Hand-written message dispatch may be more direct.

## Compared with Hand-Written Message Dispatch

Hand-written message dispatch is simple, fully controlled, and has no generation layer. Its downside is that as interfaces grow, route ids, DTOs, serialization, and caller wrappers can drift out of sync.

Lakona.Rpc is a better fit when the number of RPC methods will grow and both server and client want strongly typed call entry points. The cost is that you must follow the Shared contracts and refresh source-generated glue through normal builds.

## Compared with Schema-First RPC Tools

Schema-first tools usually start from `.proto`, IDL, or another schema and then generate code for multiple languages. They fit cross-language, multi-team, platform-style APIs.

Lakona.Rpc is currently C# contract-first: you write C# interfaces and DTOs directly. That is closer to Unity / Godot C# projects, but it does not provide a cross-language schema ecosystem.

## Recommended Combinations

First integration:

```bash
lakona-tool new --name MyGame --client-engine unity --transport websocket --serializer json
```

Godot:

```bash
lakona-tool new --name MyGame --client-engine godot --transport websocket --serializer json
```

Evaluate MemoryPack, TCP, or KCP during performance tuning. Do not choose a complex combination early without benchmarks and production constraints.

## Decision Checklist

- Is the team willing to make `Shared` the single source of truth for contracts?
- Is the client mainly C#?
- Can the application layer own reconnect and authentication?
- Is there a plan for cross-version DTO tests?
- Will performance be tested on real devices and real networks?

If most answers are yes, Lakona.Rpc is a reasonable candidate. If not, evaluate a hand-written protocol or schema-first RPC first.
