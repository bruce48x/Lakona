namespace Lakona.Tool.RpcStarter;

internal static class StarterSharedTemplate
{
    public static void Generate(StarterTemplateContext context)
    {
        var sharedPath = context.Paths.SharedPath;
        var projectName = Path.GetFileName(sharedPath);

        StarterFileWriter.Write(Path.Combine(sharedPath, "Directory.Build.props"), BuildSharedDirectoryBuildProps());
        StarterFileWriter.Write(Path.Combine(sharedPath, $"{projectName}.csproj"), BuildSharedProjectFile(context));
        StarterFileWriter.Write(Path.Combine(sharedPath, $"{projectName}.asmdef"), BuildSharedAsmdef(context.Serializer));
        StarterFileWriter.Write(Path.Combine(sharedPath, "package.json"), BuildSharedPackageJson(context, projectName));
    }

    private static string BuildSharedDirectoryBuildProps() => """
<Project>
  <PropertyGroup>
    <MSBuildProjectExtensionsPath>..\_artifacts\Shared\obj\</MSBuildProjectExtensionsPath>
    <BaseIntermediateOutputPath>..\_artifacts\Shared\obj\</BaseIntermediateOutputPath>
    <BaseOutputPath>..\_artifacts\Shared\bin\</BaseOutputPath>
  </PropertyGroup>
</Project>
""";

    private static string BuildSharedProjectFile(StarterTemplateContext context)
    {
        var targetFrameworks = context.ClientEngine switch
        {
            ClientEngineKind.Godot => "net8.0;net10.0",
            ClientEngineKind.Console => "net10.0",
            _ => "netstandard2.1;net10.0"
        };

        var packageReferences = PackageReferenceText.RenderSdkPackageReferences(StarterDependencyPlanner.Create(context, StarterProjectRole.Shared));

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

    private static string BuildSharedPackageJson(StarterTemplateContext context, string projectName) => $$"""
{
  "name": "com.{{context.CompanyId}}.shared",
  "version": "1.0.0",
  "displayName": "{{projectName}} Shared",
  "description": "Shared DTO and utility code",
  "unity": "2022.3",
  "author": {
    "name": "Shared"
  }
}
""";

    private static string BuildSharedAsmdef(SerializerKind serializer)
    {
        var asmdefReferences = serializer == SerializerKind.MemoryPack
            ? """
    "Lakona.Rpc.Core.dll",
    "MemoryPack.Core.dll",
    "System.Runtime.CompilerServices.Unsafe.dll"
"""
            : """
    "Lakona.Rpc.Core.dll"
""";

        var allowUnsafeCode = serializer == SerializerKind.MemoryPack ? "true" : "false";
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
{{asmdefReferences}}
  ],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
""";
    }
}
