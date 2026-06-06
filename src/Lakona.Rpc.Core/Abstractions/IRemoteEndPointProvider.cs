using System.Net;

namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Optional interface for transports that can report the remote endpoint
    ///     of the connected peer. Implement on server-side transports where the
    ///     remote address is known (TCP, UDP/KCP, WebSocket with HTTP context, etc.).
    /// </summary>
    public interface IRemoteEndPointProvider
    {
        EndPoint? RemoteEndPoint { get; }
    }
}
