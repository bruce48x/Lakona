using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Xunit;

namespace Lakona.Tool.Tests.Domain;

public sealed class ProjectSpecFactoryTests
{
    [Fact]
    public void Create_UsesOptionsAndDefaultFeatures()
    {
        var options = new NewProjectOptions(
            ProjectName: "Space Arena",
            OutputPath: "D:\\Games",
            ClientEngine: ClientEngine.Godot,
            Transport: TransportKind.WebSocket,
            Serializer: SerializerKind.Json,
            Persistence: PersistenceKind.Postgres,
            NuGetForUnitySource: NuGetForUnitySource.Embedded,
            DeploymentProfile: DeploymentProfile.Compose);

        var spec = new LakonaProjectSpecFactory().Create(options);

        Assert.Equal("Space Arena", spec.Name);
        Assert.Equal("D:\\Games", spec.Layout.OutputPath);
        Assert.Equal(Path.Combine("D:\\Games", "Space Arena"), spec.Layout.RootPath);
        Assert.Equal("SpaceArena", spec.Layout.RootNamespace);
        Assert.Equal("Server.App", spec.Layout.ServerProjectName);
        Assert.Equal("Shared", spec.Layout.SharedProjectName);
        Assert.Equal("Client", spec.Layout.GodotAssemblyName);
        Assert.Equal(ClientEngine.Godot, spec.ClientEngine);
        Assert.Equal(TransportKind.WebSocket, spec.Transport);
        Assert.Equal(SerializerKind.Json, spec.Serializer);
        Assert.Equal(PersistenceKind.Postgres, spec.Persistence);
        Assert.Equal(NuGetForUnitySource.Embedded, spec.NuGetForUnitySource);
        Assert.Equal(DeploymentProfile.Compose, spec.DeploymentProfile);
        Assert.Equal(
            [
                ProjectFeature.ClusterLocal,
                ProjectFeature.Hotfix,
                ProjectFeature.ReliablePush,
                ProjectFeature.LoginSlice,
                ProjectFeature.ChatSlice
            ],
            spec.Features);
    }

    [Fact]
    public void Create_SanitizesNamingDecisions()
    {
        var options = new NewProjectOptions(
            ProjectName: "99 Arena-战斗!",
            OutputPath: ".",
            ClientEngine: ClientEngine.Unity,
            Transport: TransportKind.Kcp,
            Serializer: SerializerKind.MemoryPack,
            Persistence: PersistenceKind.None,
            NuGetForUnitySource: NuGetForUnitySource.OpenUpm,
            DeploymentProfile: DeploymentProfile.None);

        var spec = new LakonaProjectSpecFactory().Create(options);

        Assert.Equal("_99Arena", spec.Layout.RootNamespace);
        Assert.Equal("com.lakona.99arena", spec.Layout.UnityPackageId);
        Assert.Equal("Lakona 99 Arena", spec.Layout.GeneratedDocsTitle);
    }
}
