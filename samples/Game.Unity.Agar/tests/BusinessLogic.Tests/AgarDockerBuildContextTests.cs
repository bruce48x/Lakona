using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarDockerBuildContextTests
{
    [Fact]
    public void ServerDockerfileCopiesSourceTreeAsSingleBuildContext()
    {
        var root = FindRepositoryRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Dockerfile"));
        var dockerignore = File.ReadAllText(Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Dockerfile.dockerignore"));

        Assert.Contains("COPY src/ src/", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("COPY src/Lakona.", dockerfile, StringComparison.Ordinal);
        Assert.Contains("!src/", dockerignore, StringComparison.Ordinal);
        Assert.Contains("!src/**", dockerignore, StringComparison.Ordinal);
        Assert.DoesNotContain("!src/Lakona.", dockerignore, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerDockerfileUsesAspNetRuntimeImage()
    {
        var dockerfile = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "Dockerfile"));

        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0.7 AS runtime", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM mcr.microsoft.com/dotnet/runtime:10.0.7 AS runtime", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerDockerfilePublishesHotfixAssemblyIntoRuntimeImage()
    {
        var dockerfile = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "Dockerfile"));

        Assert.Contains("dotnet restore samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet build samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY --from=publish-gateway /src/samples/Game.Unity.Agar/Server/App/bin/Release/net10.0/hotfix ./hotfix/", dockerfile, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }
}
