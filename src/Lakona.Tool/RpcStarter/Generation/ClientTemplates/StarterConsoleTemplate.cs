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
        var packageReferences = RenderPackageReferences(StarterDependencyPlanner.Create(context, StarterProjectRole.ConsoleClient));

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

    private static string RenderPackageReferences(StarterDependencyPlan plan) =>
        string.Join(Environment.NewLine, plan.PackageReferences.Select(RenderPackageReference));

    private static string RenderPackageReference(StarterPackageReference reference)
    {
        if (reference.PrivateAssets is null && reference.IncludeAssets is null)
            return $"    <PackageReference Include=\"{reference.Id}\" Version=\"{reference.Version}\" />";

        var metadata = new List<string>();
        if (reference.PrivateAssets is not null)
            metadata.Add($"      <PrivateAssets>{reference.PrivateAssets}</PrivateAssets>");
        if (reference.IncludeAssets is not null)
            metadata.Add($"      <IncludeAssets>{reference.IncludeAssets}</IncludeAssets>");

        return string.Join(
            Environment.NewLine,
            $"    <PackageReference Include=\"{reference.Id}\" Version=\"{reference.Version}\">",
            string.Join(Environment.NewLine, metadata),
            "    </PackageReference>");
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
using Rpc.Generated;
using Shared.Interfaces;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
{{transportUsing}}
{{serializerUsing}}

var host = Environment.GetEnvironmentVariable("ULINKRPC_HOST") ?? "127.0.0.1";
var port = int.TryParse(Environment.GetEnvironmentVariable("ULINKRPC_PORT"), out var configuredPort)
    ? configuredPort
    : 20000;
var path = Environment.GetEnvironmentVariable("ULINKRPC_PATH") ?? "{{defaultPath}}";
var message = args.Length > 0 ? string.Join(" ", args) : "hello";

await using var client = new RpcClient(new RpcClientOptions(
    {{transportConstruction}},
    {{serializerConstruction}})
    .UseSecurity(ConfigureTransportSecurity));

await client.ConnectAsync();

var reply = await client.Api.Shared.Ping.PingAsync(new PingRequest
{
    Message = message
});

Console.WriteLine($"Ping ok: message={reply.Message}, serverTimeUtc={reply.ServerTimeUtc}");

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

- `ULINKRPC_HOST` defaults to `127.0.0.1`.
- `ULINKRPC_PORT` defaults to `20000`.
- `ULINKRPC_PATH` defaults to `{{(context.Transport == TransportKind.WebSocket ? "/ws" : string.Empty)}}`.

Selected transport: {{context.Transport}}
Selected serializer: {{context.Serializer}}
""";
}
