using Lakona.ProjectSystem.Generation.Domain;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Domain;

public sealed class ProjectSpecFactoryTests
{
    [Fact]
    public void Create_UsesOptionsAndDefaultCapabilities()
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            nameof(ProjectSpecFactoryTests),
            nameof(Create_UsesOptionsAndDefaultCapabilities));
        var options = new ProjectSpecTestOptions(
            ProjectName: "Space Arena",
            OutputPath: outputPath,
            ClientEngine: ClientEngine.Godot,
            Transport: TransportKind.WebSocket,
            Serializer: SerializerKind.Json,
            NuGetForUnitySource: NuGetForUnitySource.Embedded,
            DeploymentProfile: DeploymentProfile.Compose);

        var spec = new ProjectSpecTestFactory().Create(options);

        Assert.Equal("Space Arena", spec.Name);
        Assert.Equal(outputPath, spec.Layout.OutputPath);
        Assert.Equal(Path.Combine(outputPath, "Space Arena"), spec.Layout.RootPath);
        Assert.Equal("SpaceArena", spec.Layout.RootNamespace);
        Assert.Equal("Server.App", spec.Layout.ServerProjectName);
        Assert.Equal("Shared", spec.Layout.SharedProjectName);
        Assert.Equal("Client", spec.Layout.GodotAssemblyName);
        Assert.Equal(ClientEngine.Godot, spec.ClientEngine);
        Assert.Equal(ClientEngineVersion.Godot46, spec.ClientEngineVersion);
        Assert.Equal(TransportKind.WebSocket, spec.Transport);
        Assert.Equal(SerializerKind.Json, spec.Serializer);
        Assert.Equal(NuGetForUnitySource.Embedded, spec.NuGetForUnitySource);
        Assert.Equal(DeploymentProfile.Compose, spec.DeploymentProfile);
        Assert.Equal(
            [
                ProjectCapability.ClusterLocal,
                ProjectCapability.Hotfix,
                ProjectCapability.ReliablePush,
                ProjectCapability.LoginSlice,
                ProjectCapability.GameSlice
            ],
            spec.Capabilities);
    }

    [Theory]
    [InlineData("Unity", null, "Unity2022")]
    [InlineData("Unity", "Unity60", "Unity60")]
    [InlineData("Unity", "Unity63", "Unity63")]
    [InlineData("Tuanjie", null, "Tuanjie167")]
    [InlineData("Godot", null, "Godot46")]
    [InlineData("Console", null, null)]
    public void Create_ResolvesClientEngineVersion(
        string engineName,
        string? requestedName,
        string? expectedName)
    {
        var engine = Enum.Parse<ClientEngine>(engineName);
        var requested = requestedName is null
            ? (ClientEngineVersion?)null
            : Enum.Parse<ClientEngineVersion>(requestedName);
        var expected = expectedName is null
            ? (ClientEngineVersion?)null
            : Enum.Parse<ClientEngineVersion>(expectedName);
        var options = new ProjectSpecTestOptions(
            "VersionedClient",
            ".",
            engine,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None,
            ClientEngineVersion: requested);

        var spec = new ProjectSpecTestFactory().Create(options);

        Assert.Equal(expected, spec.ClientEngineVersion);
    }

    [Fact]
    public void Create_SanitizesNamingDecisions()
    {
        var options = new ProjectSpecTestOptions(
            ProjectName: "99 Arena-战斗!",
            OutputPath: ".",
            ClientEngine: ClientEngine.Unity,
            Transport: TransportKind.Kcp,
            Serializer: SerializerKind.MemoryPack,
            NuGetForUnitySource: NuGetForUnitySource.OpenUpm,
            DeploymentProfile: DeploymentProfile.None);

        var spec = new ProjectSpecTestFactory().Create(options);

        Assert.Equal("_99Arena", spec.Layout.RootNamespace);
        Assert.Equal("com.lakona.99arena", spec.Layout.UnityPackageId);
        Assert.Equal("Lakona 99 Arena", spec.Layout.GeneratedDocsTitle);
    }

    [Fact]
    public void Create_ForcesEmbeddedNuGetForUnitySource_ForTuanjie()
    {
        var options = new ProjectSpecTestOptions(
            ProjectName: "ChinaNet",
            OutputPath: ".",
            ClientEngine: ClientEngine.Tuanjie,
            Transport: TransportKind.Kcp,
            Serializer: SerializerKind.MemoryPack,
            NuGetForUnitySource: NuGetForUnitySource.OpenUpm,
            DeploymentProfile: DeploymentProfile.None,
            Presence: ProjectSpecTestOptionPresence.NuGetForUnitySource);

        var spec = new ProjectSpecTestFactory().Create(options);

        Assert.Equal(NuGetForUnitySource.Embedded, spec.NuGetForUnitySource);
    }

    [Fact]
    public void Create_KeepsExplicitEmbeddedSource_ForStandardUnity()
    {
        var options = new ProjectSpecTestOptions(
            ProjectName: "OfflineUnity",
            OutputPath: ".",
            ClientEngine: ClientEngine.Unity,
            Transport: TransportKind.Kcp,
            Serializer: SerializerKind.MemoryPack,
            NuGetForUnitySource: NuGetForUnitySource.Embedded,
            DeploymentProfile: DeploymentProfile.None,
            Presence: ProjectSpecTestOptionPresence.NuGetForUnitySource);

        var spec = new ProjectSpecTestFactory().Create(options);

        Assert.Equal(NuGetForUnitySource.Embedded, spec.NuGetForUnitySource);
    }
}
