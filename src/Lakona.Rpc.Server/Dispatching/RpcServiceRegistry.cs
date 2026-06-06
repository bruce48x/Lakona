using System.Collections.Concurrent;
using System.ComponentModel;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

/// <summary>
///     Generated-binder handler for one decoded request in a session.
/// </summary>
/// <remarks>
///     Runtime-internal handler wiring. Regular applications should define RPC contracts and service
///     implementations, then let generated binders register handlers.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask<TransportFrame> RpcSessionHandler(RpcSession session, RpcRequestFrame req, CancellationToken ct);

/// <summary>
///     Registry used by generated service binders to connect service ids and method ids to runtime handlers.
/// </summary>
/// <remarks>
///     Generated-support API. Regular server applications should use <see cref="RpcServerHostBuilder"/> and
///     generated binders instead of hand-writing handler registrations.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RpcServiceRegistry
{
    private readonly ConcurrentDictionary<(int serviceId, int methodId), RpcSessionHandler> _handlers = new();

    public bool IsEmpty => _handlers.IsEmpty;

    public void Register(int serviceId, int methodId, RpcSessionHandler handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        _handlers[(serviceId, methodId)] = handler;
    }

    public bool TryGetHandler(int serviceId, int methodId, out RpcSessionHandler handler)
    {
        return _handlers.TryGetValue((serviceId, methodId), out handler!);
    }
}
