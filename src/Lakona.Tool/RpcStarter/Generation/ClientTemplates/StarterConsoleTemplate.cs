namespace Lakona.Tool.RpcStarter;

internal static class StarterConsoleTemplate
{
    public static void Generate(StarterTemplateContext context)
    {
        StarterFileWriter.Write(Path.Combine(context.Paths.ClientPath, "Client.csproj"), BuildClientProject(context));
        StarterFileWriter.Write(Path.Combine(context.Paths.ClientPath, "Program.cs"), BuildProgram(context));
        StarterFileWriter.Write(Path.Combine(context.Paths.ClientPath, "README.md"), BuildReadme(context));
    }

    private static string BuildClientProject(StarterTemplateContext context)
    {
        var packageReferences = PackageReferenceText.RenderSdkPackageReferences(StarterDependencyPlanner.Create(context, StarterProjectRole.ConsoleClient));

        return $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Client</RootNamespace>
    <LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>
    <LakonaRpcGeneratedNamespace>Rpc.Generated</LakonaRpcGeneratedNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Shared\Shared.csproj" />
  </ItemGroup>

  <ItemGroup>
{{packageReferences}}
  </ItemGroup>

</Project>
""";
    }

    private static string BuildProgram(StarterTemplateContext context)
    {
        var transportUsing = context.Transport switch
        {
            TransportKind.Tcp => "using Lakona.Rpc.Transport.Tcp;",
            TransportKind.WebSocket => "using Lakona.Rpc.Transport.WebSocket;",
            TransportKind.Kcp => "using Lakona.Rpc.Transport.Kcp;",
            _ => throw new ArgumentOutOfRangeException(nameof(context), context.Transport, null)
        };

        var serializerUsing = context.Serializer switch
        {
            SerializerKind.Json => "using Lakona.Rpc.Serializer.Json;",
            SerializerKind.MemoryPack => "using Lakona.Rpc.Serializer.MemoryPack;",
            _ => throw new ArgumentOutOfRangeException(nameof(context), context.Serializer, null)
        };

        var transportConstruction = context.Transport switch
        {
            TransportKind.Tcp => "new TcpTransport(host, port)",
            TransportKind.WebSocket => "new WsTransport($\"ws://{host}:{port}{NormalizePath(path)}\")",
            TransportKind.Kcp => "new KcpTransport(host, port)",
            _ => throw new ArgumentOutOfRangeException(nameof(context), context.Transport, null)
        };

        var serializerConstruction = context.Serializer switch
        {
            SerializerKind.Json => "new JsonRpcSerializer()",
            SerializerKind.MemoryPack => "new MemoryPackRpcSerializer()",
            _ => throw new ArgumentOutOfRangeException(nameof(context), context.Serializer, null)
        };

        var defaultPath = context.Transport == TransportKind.WebSocket ? "/ws" : string.Empty;

        return $$"""
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
{{transportUsing}}
{{serializerUsing}}

var host = Environment.GetEnvironmentVariable("LAKONA_RPC_HOST") ?? "127.0.0.1";
var port = int.TryParse(Environment.GetEnvironmentVariable("LAKONA_RPC_PORT"), out var configuredPort)
    ? configuredPort
    : 20000;
var path = Environment.GetEnvironmentVariable("LAKONA_RPC_PATH") ?? "{{defaultPath}}";

await using var client = new RpcClient(new RpcClientOptions(
    {{transportConstruction}},
    {{serializerConstruction}})
    .UseSecurity(ConfigureTransportSecurity));

await client.ConnectAsync();

Console.WriteLine("Connected to server.");

static string NormalizePath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        return string.Empty;

    return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
}

static void ConfigureTransportSecurity(TransportSecurityConfig security)
{
    security.EnableCompression = false;
    security.CompressionThresholdBytes = 1024;
    security.EnableEncryption = false;
    security.EncryptionKeyBase64 = null;
}
""";
    }

    private static string BuildReadme(StarterTemplateContext context) => $$"""
# Console Client Starter (.NET 10)

Run the server first:

```bash
dotnet run --project ../Server/Server/Server.csproj
```

Then run this client:

```bash
dotnet run --project Client.csproj -- hello
```

Environment overrides:

- `LAKONA_RPC_HOST` defaults to `127.0.0.1`.
- `LAKONA_RPC_PORT` defaults to `20000`.
- `LAKONA_RPC_PATH` defaults to `{{(context.Transport == TransportKind.WebSocket ? "/ws" : string.Empty)}}`.

Selected transport: {{context.Transport}}
Selected serializer: {{context.Serializer}}
""";
}
