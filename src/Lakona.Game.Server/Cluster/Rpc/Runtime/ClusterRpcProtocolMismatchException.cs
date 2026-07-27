using System;

namespace Lakona.Game.Cluster.Rpc;

/// <summary>
/// Indicates that two cluster peers selected incompatible serializer wire protocols.
/// </summary>
public sealed class ClusterRpcProtocolMismatchException : InvalidOperationException
{
    /// <summary>
    /// Initializes a protocol mismatch error.
    /// </summary>
    public ClusterRpcProtocolMismatchException(string localProtocolId, string remoteProtocolId)
        : base($"Cluster RPC protocol mismatch: local '{localProtocolId}', remote '{remoteProtocolId}'.")
    {
        LocalProtocolId = localProtocolId;
        RemoteProtocolId = remoteProtocolId;
    }

    /// <summary>
    /// Gets the protocol selected by the local process.
    /// </summary>
    public string LocalProtocolId { get; }

    /// <summary>
    /// Gets the protocol reported by the remote process.
    /// </summary>
    public string RemoteProtocolId { get; }
}
