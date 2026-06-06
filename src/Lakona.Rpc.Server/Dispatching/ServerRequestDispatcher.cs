using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

internal sealed class ServerRequestDispatcher
{
    private const string HandlerExecutionErrorMessage = "RPC handler failed.";

    private readonly ConcurrentDictionary<(int serviceId, int methodId), RpcHandler> _handlers;
    private readonly ILogger _logger;
    private readonly RpcServiceRegistry? _registry;
    private readonly SerializedFrameSender _sender;

    public ServerRequestDispatcher(
        ConcurrentDictionary<(int serviceId, int methodId), RpcHandler> handlers,
        RpcServiceRegistry? registry,
        SerializedFrameSender sender,
        ILogger logger)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _registry = registry;
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task DispatchAsync(RpcSession session, RpcRequestFrame req, CancellationToken ct)
    {
        if (_handlers.TryGetValue((req.ServiceId, req.MethodId), out var handler))
        {
            await DispatchUserHandlerAsync(session, req, handler, ct).ConfigureAwait(false);
            return;
        }

        if (_registry is not null && _registry.TryGetHandler(req.ServiceId, req.MethodId, out var sessionHandler))
        {
            await DispatchRegistryHandlerAsync(session, req, sessionHandler, ct).ConfigureAwait(false);
            return;
        }

        using var notFoundFrame = RpcEnvelopeCodec.EncodeResponse(
            req.RequestId,
            RpcStatus.NotFound,
            ReadOnlyMemory<byte>.Empty,
            $"No handler for {req.ServiceId}:{req.MethodId}");
        await _sender.SendAsync(notFoundFrame.Memory, ct).ConfigureAwait(false);
    }

    public async Task SendOverloadedResponseAsync(uint requestId, CancellationToken ct)
    {
        var response = new RpcResponseEnvelope
        {
            RequestId = requestId,
            Status = RpcStatus.Overloaded,
            Payload = Array.Empty<byte>(),
            ErrorMessage = "RPC server is overloaded; request queue is full."
        };

        using var respBytes = RpcEnvelopeCodec.EncodeResponse(response);
        await _sender.SendAsync(respBytes.Memory, ct).ConfigureAwait(false);
    }

    private async Task DispatchUserHandlerAsync(RpcSession session, RpcRequestFrame req, RpcHandler handler, CancellationToken ct)
    {
        RpcResponseEnvelope resp;
        try
        {
            resp = await handler(new RpcRequestEnvelope
            {
                RequestId = req.RequestId,
                ServiceId = req.ServiceId,
                MethodId = req.MethodId,
                Payload = req.Payload.Memory
            }, ct).ConfigureAwait(false);
            if (resp is null)
            {
                resp = new RpcResponseEnvelope
                {
                    RequestId = req.RequestId,
                    Status = RpcStatus.HandlerError,
                    Payload = Array.Empty<byte>(),
                    ErrorMessage = "RPC handler returned null response."
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogHandlerFailure(session, req, ex);
            resp = new RpcResponseEnvelope
            {
                RequestId = req.RequestId,
                Status = RpcStatus.HandlerError,
                Payload = Array.Empty<byte>(),
                ErrorMessage = HandlerExecutionErrorMessage
            };
        }

        using var respBytes = RpcEnvelopeCodec.EncodeResponse(resp);
        await _sender.SendAsync(respBytes.Memory, ct).ConfigureAwait(false);
    }

    private async Task DispatchRegistryHandlerAsync(
        RpcSession session,
        RpcRequestFrame req,
        RpcSessionHandler sessionHandler,
        CancellationToken ct)
    {
        TransportFrame? respFrame = null;
        try
        {
            try
            {
                respFrame = await sessionHandler(session, req, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogHandlerFailure(session, req, ex);
                using var errFrame = RpcEnvelopeCodec.EncodeResponse(
                    req.RequestId,
                    RpcStatus.HandlerError,
                    ReadOnlyMemory<byte>.Empty,
                    HandlerExecutionErrorMessage);
                await _sender.SendAsync(errFrame.Memory, ct).ConfigureAwait(false);
                return;
            }

            await _sender.SendAsync(respFrame.Memory, ct).ConfigureAwait(false);
        }
        finally
        {
            respFrame?.Dispose();
        }
    }

    private void LogHandlerFailure(RpcSession session, RpcRequestFrame req, Exception ex)
    {
        _logger.LogError(
            ex,
            "RPC handler failed for request {RequestId} service {ServiceId} method {MethodId} in session {ContextId}.",
            req.RequestId,
            req.ServiceId,
            req.MethodId,
            session.ContextId);
    }
}
