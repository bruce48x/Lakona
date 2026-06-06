using System.Text.Json;

namespace Lakona.Rpc.Starter;

internal static class NuGetVersionResolver
{
    public static ResolvedVersions ResolveVersions(TransportKind transport, SerializerKind serializer)
    {
        return new ResolvedVersions(
            StarterReleaseVersions.Core,
            StarterReleaseVersions.Server,
            StarterReleaseVersions.Client,
            GetTransportVersion(transport),
            GetSerializerVersion(serializer),
            StarterReleaseVersions.Analyzers,
            serializer is SerializerKind.MemoryPack ? StarterReleaseVersions.MemoryPackRuntime : null,
            serializer is SerializerKind.MemoryPack ? StarterReleaseVersions.MemoryPackRuntimeCore : null);
    }

    public static string GetTransportPackage(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "Lakona.Rpc.Transport.Tcp",
        TransportKind.WebSocket => "Lakona.Rpc.Transport.WebSocket",
        TransportKind.Kcp => "Lakona.Rpc.Transport.Kcp",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    public static string GetSerializerPackage(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "Lakona.Rpc.Serializer.Json",
        SerializerKind.MemoryPack => "Lakona.Rpc.Serializer.MemoryPack",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string GetTransportVersion(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => StarterReleaseVersions.TransportTcp,
        TransportKind.WebSocket => StarterReleaseVersions.TransportWebSocket,
        TransportKind.Kcp => StarterReleaseVersions.TransportKcp,
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string GetSerializerVersion(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => StarterReleaseVersions.SerializerJson,
        SerializerKind.MemoryPack => StarterReleaseVersions.SerializerMemoryPack,
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };
}

internal static class StarterReleaseVersions
{
    private static readonly Lazy<ReleaseVersionManifest> Manifest = new(LoadManifest);

    public static string Core => Manifest.Value.Core;
    public static string Server => Manifest.Value.Server;
    public static string Client => Manifest.Value.Client;
    public static string TransportTcp => Manifest.Value.TransportTcp;
    public static string TransportWebSocket => Manifest.Value.TransportWebSocket;
    public static string TransportKcp => Manifest.Value.TransportKcp;
    public static string SerializerJson => Manifest.Value.SerializerJson;
    public static string SerializerMemoryPack => Manifest.Value.SerializerMemoryPack;
    public static string Analyzers => Manifest.Value.Analyzers;
    public static string MemoryPackRuntime => Manifest.Value.MemoryPackRuntime;
    public static string MemoryPackRuntimeCore => Manifest.Value.MemoryPackRuntimeCore;

    private static ReleaseVersionManifest LoadManifest()
    {
        const string resourceName = "Lakona.Rpc.Starter.ReleaseVersions.json";
        var assembly = typeof(StarterReleaseVersions).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded starter release manifest resource '{resourceName}'.");

        var manifest = JsonSerializer.Deserialize<ReleaseVersionManifest>(stream)
            ?? throw new InvalidOperationException("Starter release manifest is empty.");

        manifest.Validate();
        return manifest;
    }
}

internal sealed class ReleaseVersionManifest
{
    public string Core { get; init; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public string Client { get; init; } = string.Empty;
    public string TransportTcp { get; init; } = string.Empty;
    public string TransportWebSocket { get; init; } = string.Empty;
    public string TransportKcp { get; init; } = string.Empty;
    public string SerializerJson { get; init; } = string.Empty;
    public string SerializerMemoryPack { get; init; } = string.Empty;
    public string Analyzers { get; init; } = string.Empty;
    public string MemoryPackRuntime { get; init; } = string.Empty;
    public string MemoryPackRuntimeCore { get; init; } = string.Empty;

    public void Validate()
    {
        foreach (var (name, version) in EnumerateVersions())
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException($"Starter release manifest is missing '{name}'.");
            }
        }
    }

    private IEnumerable<(string Name, string Version)> EnumerateVersions()
    {
        yield return (nameof(Core), Core);
        yield return (nameof(Server), Server);
        yield return (nameof(Client), Client);
        yield return (nameof(TransportTcp), TransportTcp);
        yield return (nameof(TransportWebSocket), TransportWebSocket);
        yield return (nameof(TransportKcp), TransportKcp);
        yield return (nameof(SerializerJson), SerializerJson);
        yield return (nameof(SerializerMemoryPack), SerializerMemoryPack);
        yield return (nameof(Analyzers), Analyzers);
        yield return (nameof(MemoryPackRuntime), MemoryPackRuntime);
        yield return (nameof(MemoryPackRuntimeCore), MemoryPackRuntimeCore);
    }
}
