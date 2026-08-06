using System.ComponentModel;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

/// <summary>
///     Generated-support registration for one RPC service.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RpcServiceRegistration<TService>
    where TService : class
{
    private const string InvalidRequestPayloadMessage = "RPC request payload is invalid.";
    private readonly Func<RpcSession, TService> _activate;
    private readonly RpcServiceRegistry _registry;
    private readonly int _serviceId;
    private readonly string? _serviceName;

    internal RpcServiceRegistration(
        RpcServiceRegistry registry,
        int serviceId,
        string? serviceName,
        Func<RpcSession, TService> activate)
    {
        _registry = registry;
        _serviceId = serviceId;
        _serviceName = serviceName;
        _activate = activate;
    }

    public void Register<TRequest>(
        int methodId,
        Func<TService, TRequest, CancellationToken, ValueTask> invoke,
        string? methodName = null)
    {
        if (invoke is null) throw new ArgumentNullException(nameof(invoke));

        _registry.Register(
            _serviceId,
            methodId,
            async (session, request, cancellationToken) =>
            {
                if (!TryDeserializeRequest(session, request, out TRequest argument, out var badRequestResponse))
                {
                    return badRequestResponse;
                }

                var service = _activate(session);
                await invoke(service, argument, cancellationToken).ConfigureAwait(false);
                using var response = RpcEnvelopeCodec.BeginResponsePayload(
                    request.RequestId,
                    RpcStatus.Ok);
                return RpcEnvelopeCodec.CompletePayload(response);
            },
            _serviceName,
            methodName);
    }

    public void Register<TRequest, TResponse>(
        int methodId,
        Func<TService, TRequest, CancellationToken, ValueTask<TResponse>> invoke,
        string? methodName = null)
    {
        if (invoke is null) throw new ArgumentNullException(nameof(invoke));

        _registry.Register(
            _serviceId,
            methodId,
            async (session, request, cancellationToken) =>
            {
                if (!TryDeserializeRequest(session, request, out TRequest argument, out var badRequestResponse))
                {
                    return badRequestResponse;
                }

                var service = _activate(session);
                var result = await invoke(service, argument, cancellationToken).ConfigureAwait(false);
                using var response = RpcEnvelopeCodec.BeginResponsePayload(
                    request.RequestId,
                    RpcStatus.Ok);
                session.Serializer.Serialize(response, result);
                return RpcEnvelopeCodec.CompletePayload(response);
            },
            _serviceName,
            methodName);
    }

    private static bool TryDeserializeRequest<TRequest>(
        RpcSession session,
        RpcRequestFrame request,
        out TRequest argument,
        out TransportFrame badRequestResponse)
    {
        try
        {
            argument = session.Serializer.Deserialize<TRequest>(request.Payload.Memory)
                ?? throw new InvalidOperationException("The RPC request payload deserialized to null.");
            badRequestResponse = null!;
            return true;
        }
        catch (Exception exception)
        {
            session.LogInvalidRequestPayload(request, exception);
            argument = default!;
            badRequestResponse = RpcEnvelopeCodec.EncodeResponse(
                request.RequestId,
                RpcStatus.BadRequest,
                ReadOnlyMemory<byte>.Empty,
                InvalidRequestPayloadMessage);
            return false;
        }
    }

}
