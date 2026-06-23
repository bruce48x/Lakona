# Lakona.Rpc.Analyzers

Roslyn analyzers and source generators for Lakona.Rpc contract projects.

Generated starter server and SDK-style client projects reference this package as a private build dependency. It generates RPC client and server glue at compile time from interfaces annotated with `RpcService`, `RpcMethod`, `RpcNotificationContract`, and `RpcNotification`, and reports diagnostics for invalid or duplicate contract ids.

Unity-compatible client assemblies should opt in with `[assembly: LakonaRpcGenerateClient("Rpc.Generated")]` so only one Unity script assembly receives generated client glue.

Game clients can also opt into a generated `LakonaGameClient` wrapper:

```xml
<LakonaGameGenerateClient>true</LakonaGameGenerateClient>
<LakonaGameClientRuntime>unity</LakonaGameClientRuntime>
<LakonaGameClientPlatform>unity</LakonaGameClientPlatform>
<LakonaGameClientGameVersion>chat</LakonaGameClientGameVersion>
```

Unity script assemblies can use assembly markers beside the RPC client marker:

```csharp
[assembly: LakonaRpcGenerateClient("Rpc.Generated")]
[assembly: LakonaGameGenerateClient("unity", "unity", "chat")]
```

Typical projects should add this package with:

```xml
<PackageReference Include="Lakona.Rpc.Analyzers" Version="0.2.2">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```
