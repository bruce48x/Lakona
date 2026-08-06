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
    private readonly RpcConnectionChannel _connection;

    public ServerRequestDispatcher(
        ConcurrentDictionary<(int serviceId, int methodId), RpcHandler> handlers,
        RpcServiceRegistry? registry,
        IReadOnlyList<IRpcSessionRequestGate>? requestGates,
        RpcConnectionChannel connection,
        ILogger logger)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _registry = registry;
        _requestGates = requestGates ?? Array.Empty<IRpcSessionRequestGate>();
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task DispatchAsync(
        RpcSession session,
        RpcRequestFrame req,
        CancellationToken ct,
        long startedAt)
    {
        LogRequestReceived(session, req);

        if (!await IsAllowedAsync(session, req, ct, startedAt).ConfigureAwait(false))
        {
            return;
        }

        if (_handlers.TryGetValue((req.ServiceId, req.MethodId), out var handler))
        {
            await DispatchUserHandlerAsync(session, req, handler, ct, startedAt).ConfigureAwait(false);
            return;
        }

        if (_registry is not null && _registry.TryGetHandler(req.ServiceId, req.MethodId, out var sessionHandler))
        {
            await DispatchRegistryHandlerAsync(session, req, sessionHandler, ct, startedAt).ConfigureAwait(false);
            return;
        }

        using var notFoundFrame = RpcEnvelopeCodec.EncodeResponse(
            req.RequestId,
            RpcStatus.NotFound,
            ReadOnlyMemory<byte>.Empty,
            $"No handler for {req.ServiceId}:{req.MethodId}");
        await _connection.SendAsync(notFoundFrame.Memory, ct).ConfigureAwait(false);
        LogRequestCompleted(
            session,
            req,
            RpcStatus.NotFound,
            GetElapsedTime(startedAt),
            $"No handler for {req.ServiceId}:{req.MethodId}");
    }

    private async ValueTask<bool> IsAllowedAsync(
        RpcSession session,
        RpcRequestFrame req,
        CancellationToken ct,
        long startedAt)
    {
        if (_requestGates.Count == 0)
        {
            return true;
        }

        var context = new RpcSessionRequestGateContext(
            session.ConnectionInfo,
            req.ServiceId,
            req.MethodId);
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
            await _connection.SendAsync(frame.Memory, ct).ConfigureAwait(false);
            LogRequestCompleted(
                session,
                req,
                result.Status,
                GetElapsedTime(startedAt),
                result.ErrorMessage);
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
        await _connection.SendAsync(respBytes.Memory, ct).ConfigureAwait(false);
    }

    private async Task DispatchUserHandlerAsync(
        RpcSession session,
        RpcRequestFrame req,
        RpcHandler handler,
        CancellationToken ct,
        long startedAt)
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
                    "RPC handler returned null response for request {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in connection {ConnectionId}.",
                    req.RequestId,
                    ResolveRpcMethod(req),
                    req.ServiceId,
                    req.MethodId,
                    session.ConnectionId);
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
        await _connection.SendAsync(respBytes.Memory, ct).ConfigureAwait(false);
        LogRequestCompleted(
            session,
            req,
            resp.Status,
            GetElapsedTime(startedAt),
            resp.ErrorMessage);
    }

    private async Task DispatchRegistryHandlerAsync(
        RpcSession session,
        RpcRequestFrame req,
        RpcSessionHandler sessionHandler,
        CancellationToken ct,
        long startedAt)
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
            catch (RpcBadRequestException exception)
            {
                _logger.LogWarning(
                    exception,
                    "RPC request content was invalid for request {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in connection {ConnectionId}.",
                    req.RequestId,
                    ResolveRpcMethod(req),
                    req.ServiceId,
                    req.MethodId,
                    session.ConnectionId);
                using var badRequestFrame = RpcEnvelopeCodec.EncodeResponse(
                    req.RequestId,
                    RpcStatus.BadRequest,
                    ReadOnlyMemory<byte>.Empty,
                    "RPC request payload is invalid.");
                await _connection.SendAsync(badRequestFrame.Memory, ct).ConfigureAwait(false);
                LogRequestCompleted(
                    session,
                    req,
                    RpcStatus.BadRequest,
                    GetElapsedTime(startedAt),
                    "RPC request payload is invalid.");
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
                await _connection.SendAsync(errFrame.Memory, ct).ConfigureAwait(false);
                LogRequestCompleted(
                    session,
                    req,
                    RpcStatus.HandlerError,
                    GetElapsedTime(startedAt),
                    HandlerExecutionErrorMessage);
                return;
            }

            if (TryReadResponseStatus(respFrame, out status, out errorMessage))
            {
                await _connection.SendAsync(respFrame.Memory, ct).ConfigureAwait(false);
                LogRequestCompleted(
                    session,
                    req,
                    status,
                    GetElapsedTime(startedAt),
                    errorMessage);
                return;
            }

            await _connection.SendAsync(respFrame.Memory, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "RPC request completed {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in connection {ConnectionId} in {ElapsedMs}ms.",
                req.RequestId,
                ResolveRpcMethod(req),
                req.ServiceId,
                req.MethodId,
                session.ConnectionId,
                GetElapsedTime(startedAt).TotalMilliseconds);
        }
        finally
        {
            respFrame?.Dispose();
        }
    }

    private static TimeSpan GetElapsedTime(long startedAt)
    {
        return Stopwatch.GetElapsedTime(startedAt);
    }

    private void LogRequestReceived(RpcSession session, RpcRequestFrame req)
    {
        _logger.LogDebug(
            "RPC request received {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in connection {ConnectionId}.",
            req.RequestId,
            ResolveRpcMethod(req),
            req.ServiceId,
            req.MethodId,
            session.ConnectionId);
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
                "RPC request completed {RequestId} {RpcMethod} status {Status} service {ServiceId} method {MethodId} in connection {ConnectionId} in {ElapsedMs}ms.",
                req.RequestId,
                ResolveRpcMethod(req),
                status,
                req.ServiceId,
                req.MethodId,
                session.ConnectionId,
                elapsed.TotalMilliseconds);
            return;
        }

        _logger.LogWarning(
            "RPC request completed {RequestId} {RpcMethod} status {Status} service {ServiceId} method {MethodId} in connection {ConnectionId} in {ElapsedMs}ms. {ErrorMessage}",
            req.RequestId,
            ResolveRpcMethod(req),
            status,
            req.ServiceId,
            req.MethodId,
            session.ConnectionId,
            elapsed.TotalMilliseconds,
            errorMessage);
    }

    private void LogHandlerFailure(RpcSession session, RpcRequestFrame req, Exception ex)
    {
        _logger.LogError(
            ex,
            "RPC handler failed for request {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in connection {ConnectionId}.",
            req.RequestId,
            ResolveRpcMethod(req),
            req.ServiceId,
            req.MethodId,
            session.ConnectionId);
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
