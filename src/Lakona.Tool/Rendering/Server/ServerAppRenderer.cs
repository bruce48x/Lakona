using System.Text.Json;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;

namespace Lakona.Tool.Rendering.Server;

internal sealed class ServerAppRenderer : IPlanContributor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Server/Server.slnx", RenderSolution(), FileWriteMode.Replace, GeneratedFileKind.Solution);
        builder.AddFile("Server/App/Server.App.csproj", RenderProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/App/Program.cs", RenderProgram(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/appsettings.json", RenderAppSettings(spec), FileWriteMode.Replace, GeneratedFileKind.Json);
    }

    private static string RenderSolution()
    {
        return """
        <Solution>
          <Project Path="../Shared/Shared.csproj" />
          <Project Path="App/Server.App.csproj" />
          <Project Path="Hotfix/Server.Hotfix.csproj" />
        </Solution>
        """;
    }

    private static string RenderProject(LakonaProjectSpec spec)
    {
        var packageReferences = PackageReferenceRenderer.RenderSdkPackageReferences(
            DependencyPlanner.Create(ProjectTarget.ServerApp, spec).PackageReferences);

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <RootNamespace>Server</RootNamespace>
            <BuildInParallel>false</BuildInParallel>
            <RestoreBuildInParallel>false</RestoreBuildInParallel>
            <LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>
            <LakonaRpcServerGeneratedNamespace>Server.App.Generated</LakonaRpcServerGeneratedNamespace>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\..\Shared\Shared.csproj" TargetFramework="net10.0" />
          </ItemGroup>

          <ItemGroup>
        {{packageReferences}}
          </ItemGroup>

          <ItemGroup>
            <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
          </ItemGroup>
        </Project>
        """;
    }

    private static string RenderProgram()
    {
        return """
        using Lakona.Game.Server.Hosting;

        await LakonaGameServer.RunAsync(args);
        """;
    }

    private static string RenderAppSettings(LakonaProjectSpec spec)
    {
        var endpoint = new Dictionary<string, object?>
        {
            ["Transport"] = ToolEnumText.ToCliValue(spec.Transport),
            ["Host"] = "127.0.0.1",
            ["Port"] = 20000
        };
        if (spec.Transport == TransportKind.WebSocket)
        {
            endpoint["Path"] = "/ws";
        }

        var settings = new Dictionary<string, object?>
        {
            ["Lakona.Game"] = new Dictionary<string, object?>
            {
                ["Node"] = new Dictionary<string, object?>
                {
                    ["Id"] = "dev-1"
                },
                ["Endpoints"] = new[] { endpoint }
            }
        };

        return JsonSerializer.Serialize(settings, JsonOptions);
    }
}
