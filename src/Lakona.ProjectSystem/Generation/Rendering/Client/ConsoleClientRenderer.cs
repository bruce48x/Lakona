using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Common;

namespace Lakona.ProjectSystem.Generation.Rendering.Client;

internal sealed class ConsoleClientRenderer : IClientRenderer
{
    public bool Supports(ClientEngine engine)
    {
        return engine == ClientEngine.Console;
    }

    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Client/Client.csproj", RenderClientProject(spec));
        builder.AddFile("Client/Program.cs", ConsoleClientCodeTemplates.RenderProgram(spec));
        builder.AddFile("Client/ClientRuntime/ConsoleClientSettings.cs", ConsoleClientCodeTemplates.RenderConsoleClientSettings());
        builder.AddFile("Client/ClientRuntime/GameClientFactory.cs", ConsoleClientCodeTemplates.RenderGameClientFactory(spec));
        builder.AddFile("Client/LoadScenarios/GameLoadScenario.cs", ConsoleClientCodeTemplates.RenderGameLoadScenario());
        builder.AddFile("Client/LoadScenarios/GameLoadScenarioOptions.cs", ConsoleClientCodeTemplates.RenderGameLoadScenarioOptions());
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
            <LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>
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
