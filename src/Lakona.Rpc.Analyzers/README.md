# Lakona.Rpc.Analyzers

Internal Roslyn analyzers and source generators for Lakona.Rpc contract
projects. The assembly is delivered inside `Lakona.Rpc.Core`; it is not a
separately published package.

Generated starter server and SDK-style client projects reference this package as a private build dependency. It generates RPC client and server glue at compile time from interfaces annotated with `RpcService`, `RpcMethod`, `RpcNotificationContract`, and `RpcNotification`, and reports diagnostics for invalid or duplicate contract ids.

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
transitively, including this compiler extension.
