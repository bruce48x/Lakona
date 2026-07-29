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
        builder.AddFile("Client/Client.csproj", RenderClientProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Client/Program.cs", ConsoleClientCodeTemplates.RenderProgram(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/ClientRuntime/ConsoleClientSettings.cs", ConsoleClientCodeTemplates.RenderConsoleClientSettings(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/ClientRuntime/GameClientFactory.cs", ConsoleClientCodeTemplates.RenderGameClientFactory(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/LoadScenarios/GameLoadScenario.cs", ConsoleClientCodeTemplates.RenderGameLoadScenario(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Client/LoadScenarios/GameLoadScenarioOptions.cs", ConsoleClientCodeTemplates.RenderGameLoadScenarioOptions(), FileWriteMode.Replace, GeneratedFileKind.Text);
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
            <LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>
            <LakonaGameGenerateClient>true</LakonaGameGenerateClient>
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
