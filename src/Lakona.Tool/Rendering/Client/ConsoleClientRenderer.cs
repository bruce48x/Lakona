using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;

namespace Lakona.Tool.Rendering.Client;

internal sealed class ConsoleClientRenderer : IClientRenderer
{
    public bool Supports(ClientEngine engine)
    {
        return engine == ClientEngine.Console;
    }

    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Client/Client.csproj", RenderClientProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Client/Program.cs", ConsoleClientCodeTemplates.RenderProgram(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/ClientRuntime/ConsoleClientSettings.cs", ConsoleClientCodeTemplates.RenderConsoleClientSettings(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/ClientRuntime/RpcClientFactory.cs", ConsoleClientCodeTemplates.RenderRpcClientFactory(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/LoadScenarios/LoginChatLoadScenario.cs", ConsoleClientCodeTemplates.RenderLoginChatLoadScenario(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/LoadScenarios/LoginChatLoadScenarioOptions.cs", ConsoleClientCodeTemplates.RenderLoginChatLoadScenarioOptions(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderClientProject(LakonaProjectSpec spec)
    {
        var packageReferences = PackageReferenceRenderer.RenderSdkPackageReferences(
            DependencyPlanner.Create(ProjectTarget.ConsoleClient, spec).PackageReferences);

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <RootNamespace>Client</RootNamespace>
            <NuGetAudit>false</NuGetAudit>
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
}
