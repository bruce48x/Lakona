namespace Lakona.Tool.RpcStarter;

internal static class StarterSharedTemplate
{
    public static void Generate(StarterTemplateContext context)
    {
        var sharedPath = context.Paths.SharedPath;
        var projectName = Path.GetFileName(sharedPath);

        var interfacesDir = Path.Combine(sharedPath, "Interfaces");
        Directory.CreateDirectory(interfacesDir);

        StarterFileWriter.Write(Path.Combine(sharedPath, "Directory.Build.props"), BuildSharedDirectoryBuildProps());
        StarterFileWriter.Write(Path.Combine(sharedPath, $"{projectName}.csproj"), BuildSharedProjectFile(context));
        StarterFileWriter.Write(Path.Combine(sharedPath, "Interfaces", "SharedDtos.cs"), BuildSharedDtos(context.Serializer));
        StarterFileWriter.Write(Path.Combine(sharedPath, "Interfaces", "RpcContractIds.cs"), BuildSharedContractIds());
        StarterFileWriter.Write(Path.Combine(sharedPath, "Interfaces", "IPingService.cs"), BuildSharedServiceContract());
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

        var packageReferences = RenderPackageReferences(StarterDependencyPlanner.Create(context, StarterProjectRole.Shared));

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

    private static string RenderPackageReferences(StarterDependencyPlan plan) =>
        string.Join(Environment.NewLine, plan.PackageReferences.Select(RenderPackageReference));

    private static string RenderPackageReference(StarterPackageReference reference)
    {
        if (reference.PrivateAssets is null && reference.IncludeAssets is null)
        {
            return $"    <PackageReference Include=\"{reference.Id}\" Version=\"{reference.Version}\" />";
        }

        var metadata = new List<string>();
        if (reference.PrivateAssets is not null)
        {
            metadata.Add($"      <PrivateAssets>{reference.PrivateAssets}</PrivateAssets>");
        }

        if (reference.IncludeAssets is not null)
        {
            metadata.Add($"      <IncludeAssets>{reference.IncludeAssets}</IncludeAssets>");
        }

        return string.Join(
            Environment.NewLine,
            $"    <PackageReference Include=\"{reference.Id}\" Version=\"{reference.Version}\">",
            string.Join(Environment.NewLine, metadata),
            "    </PackageReference>");
    }

    private static string BuildSharedDtos(SerializerKind serializer) => serializer == SerializerKind.MemoryPack
        ? """
using MemoryPack;

namespace Shared.Interfaces
{
    [MemoryPackable]
    public sealed partial class PingRequest
    {
        [MemoryPackOrder(0)]
        public string Message { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public sealed partial class PingReply
    {
        [MemoryPackOrder(0)]
        public string Message { get; set; } = string.Empty;

        [MemoryPackOrder(1)]
        public string ServerTimeUtc { get; set; } = string.Empty;
    }
}
"""
        : """
namespace Shared.Interfaces
{
    public sealed class PingRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    public sealed class PingReply
    {
        public string Message { get; set; } = string.Empty;
        public string ServerTimeUtc { get; set; } = string.Empty;
    }
}
""";

    private static string BuildSharedContractIds() => """
namespace Shared.Interfaces
{
    public static class RpcContractIds
    {
        public static class Services
        {
            public const int Ping = 1;
        }

        public static class PingServiceMethods
        {
            public const int PingAsync = 1;
        }
    }
}
""";

    private static string BuildSharedServiceContract() => """
using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Shared.Interfaces
{
    [RpcService(RpcContractIds.Services.Ping)]
    public interface IPingService
    {
        [RpcMethod(RpcContractIds.PingServiceMethods.PingAsync)]
        ValueTask<PingReply> PingAsync(PingRequest request);
    }
}
""";

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
