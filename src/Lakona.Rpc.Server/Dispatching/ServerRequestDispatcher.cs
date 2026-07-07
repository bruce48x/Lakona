using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

internal sealed class ServerRequestDispatcher
{
    private const string HandlerExecutionErrorMessage = "RPC handler failed.";

    private readonly ConcurrentDictionary<(int serviceId, int methodId), RpcHandler> _handlers;
    private readonly ILogger _logger;
    private readonly RpcServiceRegistry? _registry;
    private readonly IReadOnlyList<IRpcSessionRequestGate> _requestGates;
    private readonly SerializedFrameSender _sender;

    public ServerRequestDispatcher(
        ConcurrentDictionary<(int serviceId, int methodId), RpcHandler> handlers,
        RpcServiceRegistry? registry,
        IReadOnlyList<IRpcSessionRequestGate>? requestGates,
        SerializedFrameSender sender,
        ILogger logger)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _registry = registry;
        _requestGates = requestGates ?? Array.Empty<IRpcSessionRequestGate>();
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task DispatchAsync(RpcSession session, RpcRequestFrame req, CancellationToken ct, Stopwatch stopwatch)
    {
        LogRequestReceived(session, req);

        if (!await IsAllowedAsync(session, req, ct, stopwatch).ConfigureAwait(false))
        {
            return;
        }

        if (_handlers.TryGetValue((req.ServiceId, req.MethodId), out var handler))
        {
            await DispatchUserHandlerAsync(session, req, handler, ct, stopwatch).ConfigureAwait(false);
            return;
        }

        if (_registry is not null && _registry.TryGetHandler(req.ServiceId, req.MethodId, out var sessionHandler))
        {
            await DispatchRegistryHandlerAsync(session, req, sessionHandler, ct, stopwatch).ConfigureAwait(false);
            return;
        }

        using var notFoundFrame = RpcEnvelopeCodec.EncodeResponse(
            req.RequestId,
            RpcStatus.NotFound,
            ReadOnlyMemory<byte>.Empty,
            $"No handler for {req.ServiceId}:{req.MethodId}");
        await _sender.SendAsync(notFoundFrame.Memory, ct).ConfigureAwait(false);
        LogRequestCompleted(
            session,
            req,
            RpcStatus.NotFound,
            stopwatch.Elapsed,
            $"No handler for {req.ServiceId}:{req.MethodId}");
    }

    private async ValueTask<bool> IsAllowedAsync(
        RpcSession session,
        RpcRequestFrame req,
        CancellationToken ct,
        Stopwatch stopwatch)
    {
        if (_requestGates.Count == 0)
        {
            return true;
        }

        var context = new RpcSessionRequestGateContext(session, req.ServiceId, req.MethodId);
        foreach (var gate in _requestGates)
        {
            var result = await gate.EvaluateAsync(context, ct).ConfigureAwait(false);
            if (result.Allowed)
            {
                continue;
            }

            using var frame = RpcEnvelopeCodec.EncodeResponse(
                req.RequestId,
                result.Status,
                ReadOnlyMemory<byte>.Empty,
                result.ErrorMessage);
            await _sender.SendAsync(frame.Memory, ct).ConfigureAwait(false);
            LogRequestCompleted(session, req, result.Status, stopwatch.Elapsed, result.ErrorMessage);
            return false;
        }

        return true;
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

    private async Task DispatchUserHandlerAsync(
        RpcSession session,
        RpcRequestFrame req,
        RpcHandler handler,
        CancellationToken ct,
        Stopwatch stopwatch)
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
                _logger.LogWarning(
                    "RPC handler returned null response for request {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in session {ContextId}.",
                    req.RequestId,
                    ResolveRpcMethod(req),
                    req.ServiceId,
                    req.MethodId,
                    session.ContextId);
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
        LogRequestCompleted(session, req, resp.Status, stopwatch.Elapsed, resp.ErrorMessage);
    }

    private async Task DispatchRegistryHandlerAsync(
        RpcSession session,
        RpcRequestFrame req,
        RpcSessionHandler sessionHandler,
        CancellationToken ct,
        Stopwatch stopwatch)
    {
        TransportFrame? respFrame = null;
        RpcStatus status = RpcStatus.HandlerError;
        string? errorMessage = null;
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
                LogRequestCompleted(session, req, RpcStatus.HandlerError, stopwatch.Elapsed, HandlerExecutionErrorMessage);
                return;
            }

            if (TryReadResponseStatus(respFrame, out status, out errorMessage))
            {
                await _sender.SendAsync(respFrame.Memory, ct).ConfigureAwait(false);
                LogRequestCompleted(session, req, status, stopwatch.Elapsed, errorMessage);
                return;
            }

            await _sender.SendAsync(respFrame.Memory, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "RPC request completed {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in session {ContextId} in {ElapsedMs}ms.",
                req.RequestId,
                ResolveRpcMethod(req),
                req.ServiceId,
                req.MethodId,
                session.ContextId,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            respFrame?.Dispose();
        }
    }

    private void LogRequestReceived(RpcSession session, RpcRequestFrame req)
    {
        _logger.LogDebug(
            "RPC request received {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in session {ContextId}.",
            req.RequestId,
            ResolveRpcMethod(req),
            req.ServiceId,
            req.MethodId,
            session.ContextId);
    }

    private void LogRequestCompleted(
        RpcSession session,
        RpcRequestFrame req,
        RpcStatus status,
        TimeSpan elapsed,
        string? errorMessage)
    {
        if (status == RpcStatus.Ok)
        {
            _logger.LogDebug(
                "RPC request completed {RequestId} {RpcMethod} status {Status} service {ServiceId} method {MethodId} in session {ContextId} in {ElapsedMs}ms.",
                req.RequestId,
                ResolveRpcMethod(req),
                status,
                req.ServiceId,
                req.MethodId,
                session.ContextId,
                elapsed.TotalMilliseconds);
            return;
        }

        _logger.LogWarning(
            "RPC request completed {RequestId} {RpcMethod} status {Status} service {ServiceId} method {MethodId} in session {ContextId} in {ElapsedMs}ms. {ErrorMessage}",
            req.RequestId,
            ResolveRpcMethod(req),
            status,
            req.ServiceId,
            req.MethodId,
            session.ContextId,
            elapsed.TotalMilliseconds,
            errorMessage);
    }

    private void LogHandlerFailure(RpcSession session, RpcRequestFrame req, Exception ex)
    {
        _logger.LogError(
            ex,
            "RPC handler failed for request {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in session {ContextId}.",
            req.RequestId,
            ResolveRpcMethod(req),
            req.ServiceId,
            req.MethodId,
            session.ContextId);
    }

    private string ResolveRpcMethod(RpcRequestFrame req)
    {
        return _registry is not null && _registry.TryGetDescriptor(req.ServiceId, req.MethodId, out var descriptor)
            ? descriptor.DisplayName
            : $"{req.ServiceId}:{req.MethodId}";
    }

    private static bool TryReadResponseStatus(TransportFrame frame, out RpcStatus status, out string? errorMessage)
    {
        status = RpcStatus.HandlerError;
        errorMessage = null;
        if (frame.IsEmpty)
        {
            return false;
        }

        if (RpcEnvelopeCodec.PeekFrameType(frame.Span) != RpcFrameType.Response)
        {
            return false;
        }

        using var response = RpcEnvelopeCodec.DecodeResponse(frame);
        status = response.Status;
        errorMessage = response.ErrorMessage;
        return true;
    }
}
