using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Operations;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Rendering;

public sealed class OperationsRendererTests
{
    [Fact]
    public void AddFiles_NoneProfile_EmitsNoFiles()
    {
        var plan = Render(Spec(DeploymentProfile.None));

        Assert.Empty(plan.Files);
    }

    [Theory]
    [InlineData("Kcp", "udp")]
    [InlineData("Tcp", "tcp")]
    [InlineData("WebSocket", "tcp")]
    public void AddFiles_ComposeProfile_PublishesCompleteServerAndMatchingBusinessPort(string transport, string protocol)
    {
        var plan = Render(Spec(DeploymentProfile.Compose, Enum.Parse<TransportKind>(transport)));

        var dockerfile = Assert.Single(plan.Files, file => file.RelativePath == "Server/Dockerfile").Content;
        var compose = Assert.Single(plan.Files, file => file.RelativePath == "docker-compose.cluster.yml").Content;
        Assert.Contains("RUN dotnet publish Server/App/Server.App.csproj -c Release -o /app", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN dotnet publish Server/Hotfix/Server.Hotfix.csproj -c Release -o /app/hotfix", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Server.App.dll\"]", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("command:", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet run", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("Server/Server", compose, StringComparison.Ordinal);
        Assert.Contains("Lakona__Endpoints__0__Host: \"0.0.0.0\"", compose, StringComparison.Ordinal);
        Assert.Contains("Lakona__Endpoints__0__Port: \"${LAKONA_CLIENT_PORT:-20000}\"", compose, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:${LAKONA_CLIENT_PORT:-20000}:${LAKONA_CLIENT_PORT:-20000}/" + protocol, compose, StringComparison.Ordinal);
        Assert.DoesNotContain("20080", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("Management", compose, StringComparison.Ordinal);
        var environment = Assert.Single(plan.Files, file => file.RelativePath == ".env.cluster.example").Content;
        Assert.Contains("Lakona__Node__Id=dev-1", environment, StringComparison.Ordinal);
        Assert.Contains("Lakona__Endpoints__0__AdvertisedHost=127.0.0.1", environment, StringComparison.Ordinal);
        Assert.DoesNotContain("LAKONA_CLUSTER_", environment, StringComparison.Ordinal);
        var dockerIgnore = Assert.Single(plan.Files, file => file.RelativePath == ".dockerignore").Content;
        Assert.Contains("**/bin", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/obj", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains(plan.Files, file => file.RelativePath == "ops/CLUSTER_OPERATIONS.md");
    }

    private static GenerationPlan Render(LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        new OperationsRenderer().AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(DeploymentProfile deploymentProfile, TransportKind transport = TransportKind.Kcp)
    {
        return new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions(
            "MyGame",
            ".",
            ClientEngine.Unity,
            transport,
            SerializerKind.MemoryPack,
            NuGetForUnitySource.OpenUpm,
            deploymentProfile));
    }
}
