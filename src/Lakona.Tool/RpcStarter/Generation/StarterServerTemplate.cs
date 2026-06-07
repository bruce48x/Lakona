namespace Lakona.Tool.RpcStarter;

internal static class StarterServerTemplate
{
    public static void Generate(StarterTemplateContext context)
    {
        var serverPath = context.Paths.ServerAppPath;
        var serverProjectName = Path.GetFileName(serverPath);
        var servicesPath = Path.Combine(serverPath, "Services");
        Directory.CreateDirectory(servicesPath);

        StarterFileWriter.Write(Path.Combine(serverPath, $"{serverProjectName}.csproj"), BuildServerProjectFile(context));
        StarterFileWriter.Write(Path.Combine(serverPath, "Program.cs"), BuildServerProgramSource(context.Serializer, context.Transport));
        StarterFileWriter.Write(Path.Combine(servicesPath, "PingService.cs"), BuildPingServiceSource());
    }

    private static string BuildServerProjectFile(StarterTemplateContext context)
    {
        var packageReferences = RenderPackageReferences(StarterDependencyPlanner.Create(context, StarterProjectRole.Server));

        return $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Server</RootNamespace>
    <LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>
    <LakonaRpcServerGeneratedNamespace>Server.Generated</LakonaRpcServerGeneratedNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Shared\Shared.csproj" />
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

    private static string BuildServerProgramSource(SerializerKind serializer, TransportKind transport) => $$"""
{{GetServerProgramUsings(serializer, transport)}}

{{GetServerProgramBody(serializer, transport)}}
""";

    private static string BuildPingServiceSource() => """
using Shared.Interfaces;

namespace Server.Services
{
    public sealed class PingService : IPingService
    {
        public ValueTask<PingReply> PingAsync(PingRequest request)
        {
            return ValueTask.FromResult(new PingReply
            {
                Message = string.IsNullOrWhiteSpace(request.Message) ? "pong" : "pong: " + request.Message,
                ServerTimeUtc = DateTime.UtcNow.ToString("O")
            });
        }
    }
}
""";

    private static string GetServerSerializerConstruction(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "new JsonRpcSerializer()",
        SerializerKind.MemoryPack => "new MemoryPackRpcSerializer()",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string GetServerTransportConstruction(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "builder.UseAcceptor(new TcpConnectionAcceptor(builder.ResolvePort(20000)));",
        TransportKind.WebSocket => "builder.UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(builder.ResolvePort(20000), \"/ws\", builder.Limits.MaxPendingAcceptedConnections, ct));",
        TransportKind.Kcp => "builder.UseAcceptor(new KcpConnectionAcceptor(builder.ResolvePort(20000), builder.Limits.MaxPendingAcceptedConnections));",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string GetServerProgramUsings(SerializerKind serializer, TransportKind transport)
    {
        var lines = new List<string>
        {
            "using Lakona.Rpc.Core;",
            "using Lakona.Rpc.Server;"
        };

        lines.Add(serializer switch
        {
            SerializerKind.Json => "using Lakona.Rpc.Serializer.Json;",
            SerializerKind.MemoryPack => "using Lakona.Rpc.Serializer.MemoryPack;",
            _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
        });

        lines.Add(transport switch
        {
            TransportKind.Tcp => "using Lakona.Rpc.Transport.Tcp;",
            TransportKind.WebSocket => "using Lakona.Rpc.Transport.WebSocket;",
            TransportKind.Kcp => "using Lakona.Rpc.Transport.Kcp;",
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
        });

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetServerProgramBody(SerializerKind serializer, TransportKind transport)
    {
        var serializerSetup = GetServerSerializerConstruction(serializer);
        var transportSetup = GetServerTransportConstruction(transport);
        return $$"""
var commandLineArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(commandLineArgs)
    .UseSerializer({{serializerSetup}})
    .UseSecurity(ConfigureTransportSecurity);

{{transportSetup}}

await builder.RunAsync();

static void ConfigureTransportSecurity(TransportSecurityConfig security)
{
    security.EnableCompression = false;
    security.CompressionThresholdBytes = 1024;
    security.EnableEncryption = false;
    security.EncryptionKeyBase64 = null;
}
""";
    }
}
