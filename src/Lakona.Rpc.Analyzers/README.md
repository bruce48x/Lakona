# Lakona.Rpc.Analyzers

This directory contains the internal Roslyn analyzers and source generators for
Lakona.Rpc contract projects. It is an implementation project, not a separately
published package. Its assembly is embedded in `Lakona.Rpc.Core`.

Consumers must not reference `Lakona.Rpc.Analyzers` directly. Generated server
and SDK-style client projects reference their runtime owner packages; those
packages deliver the matching compiler extension transitively. The extension
generates RPC client and server glue at compile time from interfaces annotated
with `RpcService`, `RpcMethod`, `RpcNotificationContract`, and
`RpcNotification`, and reports diagnostics for invalid or duplicate contract
ids.

Generated Unity and Tuanjie clients use framework-owned source-generator defaults. Generated projects do not contain a project-local RPC generation marker file. The generated client API is emitted into `Client.Generated`.

Game clients can also opt into a generated `LakonaGameClient` wrapper:

```xml
<LakonaGameGenerateClient>true</LakonaGameGenerateClient>
```

Advanced client assemblies can still use assembly markers when they do not use the generated-project defaults:

```csharp
[assembly: LakonaRpcGenerateClient("Client.Generated")]
[assembly: LakonaGameGenerateClient]
```

Package consumers should reference the appropriate Lakona runtime package.
`Lakona.Rpc.Client` and `Lakona.Rpc.Server` bring in `Lakona.Rpc.Core`
transitively, including the compiler extension.
