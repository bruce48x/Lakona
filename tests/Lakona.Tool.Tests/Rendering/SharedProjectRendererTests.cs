using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Shared;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class SharedProjectRendererTests
{
    [Fact]
    public void AddFiles_UnityMemoryPack_EmitsSharedProjectAndContracts()
    {
        var plan = Render(Spec(ClientEngine.Unity, SerializerKind.MemoryPack));

        AssertPath(plan, "Shared/Directory.Build.props");
        var csproj = AssertPath(plan, "Shared/Shared.csproj").Content;
        Assert.Contains("<TargetFrameworks>netstandard2.1;net10.0</TargetFrameworks>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Core\"", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Serializer.MemoryPack\"", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"MemoryPack\"", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"MemoryPack.Generator\"", csproj, StringComparison.Ordinal);

        var asmdef = AssertPath(plan, "Shared/Shared.asmdef").Content;
        Assert.Contains("\"MemoryPack.Core.dll\"", asmdef, StringComparison.Ordinal);
        Assert.Contains("\"allowUnsafeCode\": true", asmdef, StringComparison.Ordinal);

        var messages = AssertPath(plan, "Shared/Contracts/Chat/ChatMessages.cs").Content;
        Assert.Contains("[MemoryPackable(GenerateType.VersionTolerant)]", messages, StringComparison.Ordinal);
        Assert.Contains("public partial class ChatMessage", messages, StringComparison.Ordinal);
        AssertPath(plan, "Shared/Contracts/RpcContractIds.cs");
        AssertPath(plan, "Shared/Contracts/Login.cs");
        AssertPath(plan, "Shared/Contracts/Chat/ChatProtocols.cs");
        AssertPath(plan, "Shared/package.json");
    }

    [Fact]
    public void AddFiles_GodotJson_UsesGodotFrameworksAndDoesNotEmitMemoryPackAttributes()
    {
        var plan = Render(Spec(ClientEngine.Godot, SerializerKind.Json));

        var csproj = AssertPath(plan, "Shared/Shared.csproj").Content;
        Assert.Contains("<TargetFrameworks>net8.0;net10.0</TargetFrameworks>", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPack", csproj, StringComparison.Ordinal);

        var asmdef = AssertPath(plan, "Shared/Shared.asmdef").Content;
        Assert.Contains("\"allowUnsafeCode\": false", asmdef, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPack.Core.dll", asmdef, StringComparison.Ordinal);

        var messages = AssertPath(plan, "Shared/Contracts/Chat/ChatMessages.cs").Content;
        Assert.DoesNotContain("MemoryPack", messages, StringComparison.Ordinal);
    }

    private static GenerationPlan Render(LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        new SharedProjectRenderer().AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(ClientEngine engine, SerializerKind serializer)
    {
        return new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            engine,
            TransportKind.Kcp,
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
