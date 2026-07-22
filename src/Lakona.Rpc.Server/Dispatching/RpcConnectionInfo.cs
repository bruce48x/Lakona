using System.Net;

namespace Lakona.Rpc.Server;

/// <summary>
///     Immutable identity for one accepted RPC connection.
/// </summary>
/// <remarks>
///     An RPC connection ends with its transport connection. It is not a recoverable Game Session.
/// </remarks>
public sealed class RpcConnectionInfo
{
    public RpcConnectionInfo(string connectionId, EndPoint? remoteEndPoint = null)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id cannot be empty.", nameof(connectionId));

        ConnectionId = connectionId;
        RemoteEndPoint = remoteEndPoint;
    }

    public string ConnectionId { get; }

    /// <summary>
    ///     Remote transport endpoint when the transport exposes one.
    /// </summary>
    public EndPoint? RemoteEndPoint { get; }
}
