using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;

namespace Lakona.Tool.Rendering.Shared;

internal sealed class SharedProjectRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Shared/Directory.Build.props", RenderDirectoryBuildProps(), FileWriteMode.Replace, GeneratedFileKind.Xml);
        builder.AddFile("Shared/Shared.csproj", RenderProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Shared/Shared.asmdef", RenderAsmdef(spec), FileWriteMode.Replace, GeneratedFileKind.Json);
        builder.AddFile("Shared/package.json", RenderPackageJson(spec), FileWriteMode.Replace, GeneratedFileKind.Json);
        new SharedContractsRenderer().AddFiles(spec, builder);
    }

    private static string RenderDirectoryBuildProps()
    {
        return """
        <Project>
          <PropertyGroup>
            <MSBuildProjectExtensionsPath>..\_artifacts\Shared\obj\</MSBuildProjectExtensionsPath>
            <BaseIntermediateOutputPath>..\_artifacts\Shared\obj\</BaseIntermediateOutputPath>
            <BaseOutputPath>..\_artifacts\Shared\bin\</BaseOutputPath>
          </PropertyGroup>
        </Project>
        """;
    }

    private static string RenderProject(LakonaProjectSpec spec)
    {
        var targetFrameworks = spec.ClientEngine == ClientEngine.Godot
            ? "net8.0;net10.0"
            : "netstandard2.1;net10.0";
        var packageReferences = PackageReferenceRenderer.RenderSdkPackageReferences(
            DependencyPlanner.Create(ProjectTarget.Shared, spec).PackageReferences);

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFrameworks>{{targetFrameworks}}</TargetFrameworks>
            <ImplicitUsings>disable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <LangVersion>latest</LangVersion>
            <RootNamespace>Shared</RootNamespace>
            <NuGetAudit>false</NuGetAudit>
          </PropertyGroup>

          <ItemGroup>
        {{packageReferences}}
          </ItemGroup>
        </Project>
        """;
    }

    private static string RenderAsmdef(LakonaProjectSpec spec)
    {
        var references = spec.Serializer == SerializerKind.MemoryPack
            ? """
            "Lakona.Rpc.Core.dll",
            "MemoryPack.Core.dll",
            "System.Runtime.CompilerServices.Unsafe.dll"
        """
            : """
            "Lakona.Rpc.Core.dll"
        """;
        var allowUnsafeCode = spec.Serializer == SerializerKind.MemoryPack ? "true" : "false";

        return $$"""
        {
          "name": "Shared",
          "rootNamespace": "Shared",
          "references": [],
          "includePlatforms": [],
          "excludePlatforms": [],
          "allowUnsafeCode": {{allowUnsafeCode}},
          "overrideReferences": true,
          "precompiledReferences": [
        {{references}}
          ],
          "autoReferenced": true,
          "defineConstraints": [],
          "versionDefines": [],
          "noEngineReferences": false
        }
        """;
    }

    private static string RenderPackageJson(LakonaProjectSpec spec)
    {
        return $$"""
        {
          "name": "{{spec.Layout.UnityPackageId}}.shared",
          "version": "1.0.0",
          "displayName": "Shared",
          "description": "Shared DTO and utility code",
          "unity": "2022.3",
          "author": {
            "name": "Shared"
          }
        }
        """;
    }
}
