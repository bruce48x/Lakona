using Lakona.Game.Cluster.Rpc.Serializer.MemoryPack.Generated;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using MemoryPack;

namespace Lakona.Game.Cluster.Rpc.Serializer.MemoryPack;

/// <summary>
/// Creates the MemoryPack serializer with the formatter catalog required by cluster RPC.
/// </summary>
public sealed class MemoryPackClusterRpcSerializer : IClusterRpcSerializer
{
    private readonly MemoryPackSerializerOptions? _options;

    /// <summary>
    /// Gets the shared MemoryPack cluster serializer adapter using default options.
    /// </summary>
    public static MemoryPackClusterRpcSerializer Default { get; } = new();

    /// <summary>
    /// Initializes an adapter with optional MemoryPack serializer options.
    /// </summary>
    public MemoryPackClusterRpcSerializer(MemoryPackSerializerOptions? options = null)
    {
        _options = options;
    }

    /// <inheritdoc />
    public string ProtocolId => "lakona.cluster.memorypack.v1";

    /// <inheritdoc />
    public IRpcSerializer CreateSerializer()
    {
        GeneratedClusterRpcMemoryPackFormatters.Register();
        return _options is null
            ? new MemoryPackRpcSerializer()
            : new MemoryPackRpcSerializer(_options);
    }
}
