using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Shared;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class SharedProjectRendererTests
{
    [Fact]
    public void AddFiles_UnityMemoryPack_EmitsArenaContracts()
    {
        var plan = Render(Spec(ClientEngine.Unity, SerializerKind.MemoryPack));

        var project = AssertPath(plan, "Shared/Shared.csproj").Content;
        Assert.Contains("<TargetFrameworks>netstandard2.1;net10.0</TargetFrameworks>", project, StringComparison.Ordinal);
        Assert.Contains("Lakona.Rpc.Serializer.MemoryPack", project, StringComparison.Ordinal);

        var protocols = AssertPath(plan, "Shared/Contracts/Game/GameProtocols.cs").Content;
        Assert.Contains("interface IGameService", protocols, StringComparison.Ordinal);
        Assert.Contains("ValueTask<LoginReply> LoginAsync", protocols, StringComparison.Ordinal);
        Assert.Contains("ValueTask SubmitInputAsync", protocols, StringComparison.Ordinal);
        Assert.Contains("ValueTask<WorldSnapshot> GetWorldAsync", protocols, StringComparison.Ordinal);
        Assert.Contains("void OnWorldUpdated", protocols, StringComparison.Ordinal);

        var messages = AssertPath(plan, "Shared/Contracts/Game/GameMessages.cs").Content;
        Assert.Contains("[MemoryPackable(GenerateType.VersionTolerant)]", messages, StringComparison.Ordinal);
        Assert.Contains("public long PlayerId", messages, StringComparison.Ordinal);
        Assert.Contains("public List<PlayerSnapshot> Players", messages, StringComparison.Ordinal);
        Assert.Contains("public List<MonsterSnapshot> Monsters", messages, StringComparison.Ordinal);
        Assert.Contains("public List<BulletSnapshot> Bullets", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("Chat", messages, StringComparison.Ordinal);
        AssertPath(plan, "Shared/Contracts/RpcContractIds.cs");
    }

    [Fact]
    public void AddFiles_GodotJson_UsesGodotFrameworksWithoutMemoryPackAttributes()
    {
        var plan = Render(Spec(ClientEngine.Godot, SerializerKind.Json));
        Assert.Contains("<TargetFrameworks>net8.0;net10.0</TargetFrameworks>", AssertPath(plan, "Shared/Shared.csproj").Content, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPack", AssertPath(plan, "Shared/Contracts/Game/GameMessages.cs").Content, StringComparison.Ordinal);
    }

    private static GenerationPlan Render(LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        new SharedProjectRenderer().AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(ClientEngine engine, SerializerKind serializer) =>
        new LakonaProjectSpecFactory().Create(new NewProjectOptions("MyGame", ".", engine, TransportKind.Kcp, serializer, PersistenceKind.None, NuGetForUnitySource.OpenUpm, DeploymentProfile.None));

    private static GeneratedFile AssertPath(GenerationPlan plan, string relativePath) =>
        Assert.Single(plan.Files, file => file.RelativePath == relativePath);
}
