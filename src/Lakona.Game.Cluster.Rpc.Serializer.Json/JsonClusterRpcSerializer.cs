using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;

namespace Lakona.Game.Cluster.Rpc.Serializer.Json;

/// <summary>
/// Creates the JSON serializer used by the Lakona cluster RPC protocol.
/// </summary>
public sealed class JsonClusterRpcSerializer : IClusterRpcSerializer
{
    /// <summary>
    /// Gets the shared JSON cluster serializer adapter.
    /// </summary>
    public static JsonClusterRpcSerializer Default { get; } = new();

    private JsonClusterRpcSerializer()
    {
    }

    /// <inheritdoc />
    public string ProtocolId => "lakona.cluster.json.v1";

    /// <inheritdoc />
    public IRpcSerializer CreateSerializer() => new JsonRpcSerializer();
}
