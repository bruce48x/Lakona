using System.ComponentModel;

namespace Lakona.Rpc.Server;

/// <summary>
///     Signals that a generated-support handler received invalid request content.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RpcBadRequestException : Exception
{
    public RpcBadRequestException(string message)
        : base(message)
    {
    }
}
