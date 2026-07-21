using Lakona.RepositoryGuards.Tests.PackageVersions;

namespace Lakona.RepositoryGuards.Tests.HubVersions;

internal static class HubVersionGuard
{
    private static readonly string[] ReleaseInputPrefixes =
    [
        "src/Lakona.Hub/",
        "src/Lakona.ProjectSystem/",
        "scripts/hub/"
    ];

    // Project package versions are compiled into Lakona.ProjectSystem and therefore into Hub.
    // HubVersionGuardTests verifies this list against every XmlPeek input in that project.
    private static readonly HashSet<string> ReleaseInputFiles = new(StringComparer.Ordinal)
    {
        ".github/workflows/publish-hub.yml",
        "Directory.Build.props",
        "Directory.Build.targets",
        "global.json",
        "src/Lakona.Game.Abstractions/Lakona.Game.Abstractions.csproj",
        "src/Lakona.Game.Client/Lakona.Game.Client.csproj",
        "src/Lakona.Game.LoadTesting/Lakona.Game.LoadTesting.csproj",
        "src/Lakona.Game.Server/Lakona.Game.Server.csproj",
        "src/Lakona.Game.Server.Generators/Lakona.Game.Server.Generators.csproj",
        "src/Lakona.Game.Cluster/Lakona.Game.Cluster.csproj",
        "src/Lakona.Game.Cluster.Rpc/Lakona.Game.Cluster.Rpc.csproj",
        "src/Lakona.Game.Cluster.Rpc.Transport.Tcp/Lakona.Game.Cluster.Rpc.Transport.Tcp.csproj",
        "src/Lakona.Game.Cluster.Rpc.Serializer.Json/Lakona.Game.Cluster.Rpc.Serializer.Json.csproj",
        "src/Lakona.Game.Cluster.Rpc.Serializer.MemoryPack/Lakona.Game.Cluster.Rpc.Serializer.MemoryPack.csproj",
        "src/Lakona.Game.Server.Hotfix/Lakona.Game.Server.Hotfix.csproj",
        "src/Lakona.Game.Server.Hotfix.Abstractions/Lakona.Game.Server.Hotfix.Abstractions.csproj",
        "src/Lakona.Game.Server.Hotfix.Generators/Lakona.Game.Server.Hotfix.Generators.csproj",
        "src/Lakona.Rpc.Core/Lakona.Rpc.Core.csproj",
        "src/Lakona.Rpc.Server/Lakona.Rpc.Server.csproj",
        "src/Lakona.Rpc.Client/Lakona.Rpc.Client.csproj",
        "src/Lakona.Rpc.Transport.Tcp/Lakona.Rpc.Transport.Tcp.csproj",
        "src/Lakona.Rpc.Transport.WebSocket/Lakona.Rpc.Transport.WebSocket.csproj",
        "src/Lakona.Rpc.Transport.Kcp/Lakona.Rpc.Transport.Kcp.csproj",
        "src/Lakona.Rpc.Serializer.Json/Lakona.Rpc.Serializer.Json.csproj",
        "src/Lakona.Rpc.Serializer.MemoryPack/Lakona.Rpc.Serializer.MemoryPack.csproj",
        "src/Lakona.Rpc.Analyzers/Lakona.Rpc.Analyzers.csproj"
    };

    internal static readonly VersionGuardScope Scope = new(
        HubVersionProjectReader.ProjectPath,
        "LAKONA_HUB_VERSION_GUARD_BASE",
        "LAKONA_HUB_VERSION_GUARD_HEAD",
        "Hub version guard",
        IsReleaseInputPath);

    public static HubVersionGuardResult Evaluate(
        string repositoryRoot,
        string baseVersion,
        string headVersion,
        IReadOnlyCollection<string> changedPaths)
    {
        var changedInputs = changedPaths
            .Select(path => ToRepositoryRelativePath(repositoryRoot, path))
            .Where(IsReleaseInputPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var versionChanged = !string.Equals(baseVersion, headVersion, StringComparison.Ordinal);
        return new HubVersionGuardResult(baseVersion, headVersion, versionChanged, changedInputs);
    }

    internal static bool IsReleaseInputPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return ReleaseInputFiles.Contains(normalized) ||
               ReleaseInputPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string ToRepositoryRelativePath(string repositoryRoot, string path)
    {
        var normalized = path.Replace('\\', '/');
        var root = Path.GetFullPath(repositoryRoot).Replace('\\', '/').TrimEnd('/') + "/";
        return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? normalized[root.Length..]
            : normalized.TrimStart('/');
    }
}

internal sealed record HubVersionGuardResult(
    string BaseVersion,
    string HeadVersion,
    bool VersionChanged,
    IReadOnlyList<string> ChangedInputs)
{
    public bool Succeeded => ChangedInputs.Count == 0 || VersionChanged;
}
