# Lakona.Rpc.Analyzers

Roslyn analyzers and source generators for Lakona.Rpc contract projects.

Generated starter server and SDK-style client projects reference this package as a private build dependency. It generates RPC client and server glue at compile time from interfaces annotated with `RpcService`, `RpcMethod`, `RpcCallback`, and `RpcPush`, and reports diagnostics for invalid or duplicate contract ids.

Unity-compatible client assemblies should opt in with `[assembly: LakonaRpcGenerateClient("Rpc.Generated")]` so only one Unity script assembly receives generated client glue.

Typical projects should add this package with:

```xml
<PackageReference Include="Lakona.Rpc.Analyzers" Version="0.1.6">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```
