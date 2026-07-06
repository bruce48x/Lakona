# Lakona.Rpc.Analyzers

Roslyn analyzers and source generators for Lakona.Rpc contract projects.

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

Typical projects should add this package with:

```xml
<PackageReference Include="Lakona.Rpc.Analyzers" Version="0.3.6">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```
