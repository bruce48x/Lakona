using System.Xml.Linq;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterRpcSerializerNeutralityTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

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
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .ToArray();

        Assert.DoesNotContain("MemoryPack", packageIds);
        Assert.DoesNotContain("MemoryPack.Generator", packageIds);
    }

    [Fact]
    public void ClusterRpcSources_do_not_reference_memorypack()
    {
        var sourceRoot = Path.Combine(RepositoryRoot, "src", "Lakona.Game.Cluster.Rpc");
        var text = string.Join(
            "\n",
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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
            Directory.EnumerateFiles(sessionsRoot, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("public sealed partial class ClientNotificationDispatchRequest", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed partial class ClientNotificationDispatchReply", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed partial class ClientNotificationCommand", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed partial class ClientNotificationArgument", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPackable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryPackOrder", text, StringComparison.Ordinal);
    }
}
