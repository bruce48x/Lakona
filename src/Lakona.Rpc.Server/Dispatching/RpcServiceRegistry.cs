using System.Buffers;
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
internal delegate ValueTask<TransportFrame> RpcSessionHandler(RpcSession session, RpcRequestFrame req, CancellationToken ct);

[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask<RpcRawResult> RpcRawHandler(
    RpcConnectionInfo connection,
    RpcNotificationChannel notifications,
    ReadOnlyMemory<byte> payload,
    CancellationToken cancellationToken);

[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask RpcRawWriterHandler(
    RpcConnectionInfo connection,
    RpcNotificationChannel notifications,
    ReadOnlyMemory<byte> payload,
    IBufferWriter<byte> response,
    CancellationToken cancellationToken);

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
    private readonly ConcurrentDictionary<(int serviceId, int methodId), RpcServiceRegistryEntry> _entries = new();
    private readonly ConcurrentDictionary<int, object> _serviceRegistrations = new();

    public bool IsEmpty => _entries.IsEmpty;

    internal void Register(
        int serviceId,
        int methodId,
        RpcSessionHandler handler,
        string? serviceName = null,
        string? methodName = null)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        var key = (serviceId, methodId);
        var entry = new RpcServiceRegistryEntry(
            handler,
            new RpcMethodDescriptor(serviceId, methodId, serviceName, methodName));
        if (!_entries.TryAdd(key, entry))
        {
            throw new InvalidOperationException(
                $"RPC method {serviceId}:{methodId} is already registered.");
        }
    }

    public RpcServiceRegistration<TService> RegisterPerConnection<TService>(
        int serviceId,
        Func<RpcConnectionInfo, RpcNotificationChannel, TService> factory,
        string? serviceName = null)
        where TService : class
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        var registration = new RpcServiceRegistration<TService>(
            this,
            serviceId,
            serviceName,
            session => session.GetOrAddScopedService(
                serviceId,
                current => factory(
                    current.ConnectionInfo,
                    new RpcNotificationChannel(current))));
        if (!_serviceRegistrations.TryAdd(serviceId, registration))
        {
            throw new InvalidOperationException(
                $"RPC service {serviceId} is already registered.");
        }

        return registration;
    }

    public RpcServiceRegistration<TService> RegisterSingleton<TService>(
        int serviceId,
        TService instance,
        string? serviceName = null)
        where TService : class
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));

        return new RpcServiceRegistration<TService>(
            this,
            serviceId,
            serviceName,
            _ => instance);
    }

    public void RegisterRaw(
        int serviceId,
        int methodId,
        RpcRawHandler handler,
        string? serviceName = null,
        string? methodName = null)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        Register(
            serviceId,
            methodId,
            async (session, request, cancellationToken) =>
            {
                var result = await handler(
                        session.ConnectionInfo,
                        new RpcNotificationChannel(session),
                        request.Payload.Memory,
                        cancellationToken)
                    .ConfigureAwait(false);
                return RpcEnvelopeCodec.EncodeResponse(
                    request.RequestId,
                    result.Status,
                    result.Payload,
                    result.ErrorMessage);
            },
            serviceName,
            methodName);
    }

    public void RegisterRawWriter(
        int serviceId,
        int methodId,
        RpcRawWriterHandler handler,
        string? serviceName = null,
        string? methodName = null)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        Register(
            serviceId,
            methodId,
            async (session, request, cancellationToken) =>
            {
                using var response = RpcEnvelopeCodec.BeginResponse();
                await handler(
                        session.ConnectionInfo,
                        new RpcNotificationChannel(session),
                        request.Payload.Memory,
                        response,
                        cancellationToken)
                    .ConfigureAwait(false);
                return RpcEnvelopeCodec.CompleteResponse(
                    response,
                    request.RequestId,
                    RpcStatus.Ok);
            },
            serviceName,
            methodName);
    }

    internal bool TryGetHandler(int serviceId, int methodId, out RpcSessionHandler handler)
    {
        if (_entries.TryGetValue((serviceId, methodId), out var entry))
        {
            handler = entry.Handler;
            return true;
        }

        handler = null!;
        return false;
    }

    public bool TryGetDescriptor(int serviceId, int methodId, out RpcMethodDescriptor descriptor)
    {
        if (_entries.TryGetValue((serviceId, methodId), out var entry))
        {
            descriptor = entry.Descriptor;
            return true;
        }

        descriptor = null!;
        return false;
    }

    private sealed record RpcServiceRegistryEntry(
        RpcSessionHandler Handler,
        RpcMethodDescriptor Descriptor);
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
