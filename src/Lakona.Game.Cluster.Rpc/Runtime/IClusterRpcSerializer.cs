using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc;

/// <summary>
/// Identifies and creates the serializer used by every node on a cluster RPC channel.
/// </summary>
public interface IClusterRpcSerializer
{
    /// <summary>
    /// Gets the stable wire-protocol identifier used during cluster connection negotiation.
    /// </summary>
    string ProtocolId { get; }

    /// <summary>
    /// Creates the serializer for cluster RPC payloads.
    /// </summary>
    IRpcSerializer CreateSerializer();
}
