using System.Text.Json;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Server;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ServerAppRendererTests
{
    [Fact]
    public void AddFiles_EmitsServerAppProjectProgramAndCompactSettings()
    {
        var plan = Render(Spec(TransportKind.Kcp, SerializerKind.MemoryPack));

        Assert.Contains("<Project Path=\"../Shared/Shared.csproj\" />", AssertPath(plan, "Server/Server.slnx").Content, StringComparison.Ordinal);
        Assert.Contains("<Project Path=\"App/Server.App.csproj\" />", AssertPath(plan, "Server/Server.slnx").Content, StringComparison.Ordinal);

        var project = AssertPath(plan, "Server/App/Server.App.csproj").Content;
        Assert.Contains("<OutputType>Exe</OutputType>", project, StringComparison.Ordinal);
        Assert.Contains("<LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Game.Server\"", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Transport.Kcp\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona.Rpc.Serializer.MemoryPack", project, StringComparison.Ordinal);

        var program = AssertPath(plan, "Server/App/Program.cs").Content;
        Assert.Contains("using Lakona.Game.Server.Hosting;", program, StringComparison.Ordinal);
        Assert.Contains("await LakonaGameServer.RunAsync(args);", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RpcServerHostBuilder", program, StringComparison.Ordinal);

        var appsettings = AssertPath(plan, "Server/App/appsettings.json").Content;
        using var document = JsonDocument.Parse(appsettings);
        var endpoint = document.RootElement.GetProperty("Lakona.Game").GetProperty("Endpoints")[0];
        Assert.Equal("kcp", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("127.0.0.1", endpoint.GetProperty("Host").GetString());
        Assert.Equal(20000, endpoint.GetProperty("Port").GetInt32());
        Assert.DoesNotContain("Enabled", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("Bootstrap", appsettings, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFiles_WebSocketSettingsIncludeOnlyPathExtension()
    {
        var appsettings = AssertPath(Render(Spec(TransportKind.WebSocket, SerializerKind.Json)), "Server/App/appsettings.json").Content;

        using var document = JsonDocument.Parse(appsettings);
        var endpoint = document.RootElement.GetProperty("Lakona.Game").GetProperty("Endpoints")[0];
        Assert.Equal("websocket", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("/ws", endpoint.GetProperty("Path").GetString());
    }

    private static GenerationPlan Render(LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        new ServerAppRenderer().AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(TransportKind transport, SerializerKind serializer)
    {
        return new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            ClientEngine.Unity,
            transport,
            serializer,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
    }

    private static GeneratedFile AssertPath(GenerationPlan plan, string relativePath)
    {
        return Assert.Single(plan.Files, file => file.RelativePath == relativePath);
    }
}
