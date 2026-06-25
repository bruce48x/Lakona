using System.Xml.Linq;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterRpcSerializerNeutralityTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

    [Fact]
    public void ClusterRpcProject_does_not_reference_memorypack_packages()
    {
        var projectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Lakona.Game.Cluster.Rpc",
            "Lakona.Game.Cluster.Rpc.csproj");
        var project = XDocument.Load(projectPath);
        var packageIds = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();
        var projectReferenceValues = project
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .SelectMany(element => new[]
            {
                element.Attribute("Include")?.Value,
                element.Attribute("Update")?.Value
            })
            .OfType<string>()
            .ToArray();
        var packageItemValues = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference" || element.Name.LocalName == "PackageVersion")
            .SelectMany(element => new[]
            {
                element.Attribute("Include")?.Value,
                element.Attribute("Update")?.Value
            })
            .OfType<string>()
            .ToArray();

        Assert.DoesNotContain("MemoryPack", packageIds);
        Assert.DoesNotContain("MemoryPack.Generator", packageIds);
        Assert.DoesNotContain(projectReferenceValues, value =>
            value.Contains("Lakona.Rpc.Serializer.MemoryPack", StringComparison.Ordinal));
        Assert.DoesNotContain(packageItemValues, value =>
            value.Contains("MemoryPack", StringComparison.Ordinal));
    }

    [Fact]
    public void ClusterRpcSources_do_not_reference_memorypack()
    {
        var sourceRoot = Path.Combine(RepositoryRoot, "src", "Lakona.Game.Cluster.Rpc");
        var text = string.Join(
            "\n",
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutputPath(path))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("using MemoryPack;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPackable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPackOrder", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerSessionSources_do_not_define_cluster_notification_wire_dtos()
    {
        var sessionsRoot = Path.Combine(RepositoryRoot, "src", "Lakona.Game.Server", "Sessions");
        var text = string.Join(
            "\n",
            Directory.EnumerateFiles(sessionsRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutputPath(path))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("public sealed partial class ClientNotificationDispatchRequest", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed partial class ClientNotificationDispatchReply", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed partial class ClientNotificationCommand", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed partial class ClientNotificationArgument", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPackable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPackOrder", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository root from '{startPath}'.");
    }

    private static bool IsBuildOutputPath(string path)
    {
        var segments = Path.GetFullPath(path).Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Contains("bin", StringComparer.Ordinal) ||
            segments.Contains("obj", StringComparer.Ordinal);
    }
}
