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
    private readonly ConcurrentDictionary<(int serviceId, int methodId), RpcMethodDescriptor> _descriptors = new();

    public bool IsEmpty => _handlers.IsEmpty;

    public void Register(
        int serviceId,
        int methodId,
        RpcSessionHandler handler,
        string? serviceName = null,
        string? methodName = null)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        var key = (serviceId, methodId);
        _handlers[key] = handler;
        _descriptors[key] = new RpcMethodDescriptor(serviceId, methodId, serviceName, methodName);
    }

    public bool TryGetHandler(int serviceId, int methodId, out RpcSessionHandler handler)
    {
        return _handlers.TryGetValue((serviceId, methodId), out handler!);
    }

    public bool TryGetDescriptor(int serviceId, int methodId, out RpcMethodDescriptor descriptor)
    {
        return _descriptors.TryGetValue((serviceId, methodId), out descriptor!);
    }
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record RpcMethodDescriptor(
    int ServiceId,
    int MethodId,
    string? ServiceName,
    string? MethodName)
{
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ServiceName) && !string.IsNullOrWhiteSpace(MethodName))
            {
                return ServiceName + "." + MethodName;
            }

            if (!string.IsNullOrWhiteSpace(ServiceName))
            {
                return ServiceName;
            }

            if (!string.IsNullOrWhiteSpace(MethodName))
            {
                return MethodName;
            }

            return ServiceId + ":" + MethodId;
        }
    }
}
