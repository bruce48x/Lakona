using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

internal sealed class ServerRequestDispatcher
{
    private const string InternalErrorMessage = "RPC server failed to process the request.";

    private readonly ILogger _logger;
    private readonly RpcServiceRegistry _registry;
    private readonly IReadOnlyList<IRpcSessionRequestGate> _requestGates;
    private readonly RpcConnectionChannel _connection;

    public ServerRequestDispatcher(
        RpcServiceRegistry registry,
        IReadOnlyList<IRpcSessionRequestGate>? requestGates,
        RpcConnectionChannel connection,
        ILogger logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _requestGates = requestGates ?? Array.Empty<IRpcSessionRequestGate>();
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RpcStatus> DispatchAsync(
        RpcSession session,
        RpcRequestFrame req,
        CancellationToken ct,
        long startedAt,
        Action? onEntered = null)
    {
        LogRequestReceived(session, req);

        var rejection = await EvaluateGatesAsync(session, req, ct).ConfigureAwait(false);
        if (rejection.HasValue)
            return await SendResponseAsync(session, req, rejection.Value, ct, startedAt).ConfigureAwait(false);

        if (_registry.TryGetHandler(req.ServiceId, req.MethodId, out var sessionHandler))
        {
            var work = DispatchRegistryHandlerAsync(session, req, sessionHandler, ct, startedAt);
            onEntered?.Invoke();
            return await work.ConfigureAwait(false);
        }

        var notFound = RpcServerResponse.Encode(
            req.RequestId, RpcStatus.NotFound, ReadOnlyMemory<byte>.Empty,
            $"No handler for {req.ServiceId}:{req.MethodId}");
        return await SendResponseAsync(session, req, notFound, ct, startedAt).ConfigureAwait(false);
    }

    private async ValueTask<RpcServerResponse?> EvaluateGatesAsync(
        RpcSession session, RpcRequestFrame req, CancellationToken ct)
    {
        if (_requestGates.Count == 0)
            return null;

        var context = new RpcSessionRequestGateContext(session.ConnectionInfo, req.ServiceId, req.MethodId);
        foreach (var gate in _requestGates)
        {
            RpcSessionRequestGateResult result;
            try
            {
                result = await gate.EvaluateAsync(context, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "RPC request gate {RequestGate} failed for request {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in connection {ConnectionId}.",
                    gate.GetType().FullName,
                    req.RequestId,
                    ResolveRpcMethod(req),
                    req.ServiceId,
                    req.MethodId,
                    session.ConnectionId);
                return RpcServerResponse.Encode(
                    req.RequestId, RpcStatus.InternalError, ReadOnlyMemory<byte>.Empty, InternalErrorMessage);
            }

            if (!result.Allowed)
                return RpcServerResponse.Encode(
                    req.RequestId, result.Status, ReadOnlyMemory<byte>.Empty, result.ErrorMessage);
        }

        return null;
    }

    public async Task SendOverloadedResponseAsync(uint requestId, CancellationToken ct)
    {
        using var response = RpcServerResponse.Encode(
            requestId, RpcStatus.Overloaded, ReadOnlyMemory<byte>.Empty,
            "RPC server is overloaded; request queue is full.");
        await _connection.SendAsync(response.Memory, ct).ConfigureAwait(false);
    }

    private async Task<RpcStatus> DispatchRegistryHandlerAsync(
        RpcSession session,
        RpcRequestFrame req,
        RpcSessionHandler sessionHandler,
        CancellationToken ct,
        long startedAt)
    {
        using var publications = new RpcResponsePublicationScope(session.ConnectionId);
        RpcServerResponse response;
        try
        {
            response = await CompletePublicationsAsync(
                () => sessionHandler(session, req, ct), publications, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcBadRequestException exception)
        {
            _logger.LogWarning(
                "RPC request content was invalid for request {RequestId} {RpcMethod} service {ServiceId} method {MethodId} in connection {ConnectionId}; payload length {PayloadLength}; exception {ExceptionType}.",
                req.RequestId,
                ResolveRpcMethod(req),
                req.ServiceId,
                req.MethodId,
                session.ConnectionId,
                req.Payload.Length,
                exception.GetType().Name);
            response = RpcServerResponse.Encode(
                req.RequestId, RpcStatus.BadRequest, ReadOnlyMemory<byte>.Empty,
                "RPC request payload is invalid.");
        }
        catch (Exception ex)
        {
            LogHandlerFailure(session, req, ex);
            response = RpcServerResponse.Encode(
                req.RequestId, RpcStatus.InternalError, ReadOnlyMemory<byte>.Empty, InternalErrorMessage);
        }

        // A send failure must propagate, never become a second response.
        return await SendResponseAsync(session, req, response, ct, startedAt).ConfigureAwait(false);
    }

    private async Task<RpcStatus> SendResponseAsync(
        RpcSession session, RpcRequestFrame req, RpcServerResponse response, CancellationToken ct, long startedAt)
    {
        using (response)
        {
            await _connection.SendAsync(response.Memory, ct).ConfigureAwait(false);
            LogRequestCompleted(session, req, response.Status, GetElapsedTime(startedAt), response.ErrorMessage);
            return response.Status;
        }
    }

    private static async ValueTask<RpcServerResponse> CompletePublicationsAsync(
        Func<ValueTask<RpcServerResponse>> invoke, RpcResponsePublicationScope publications, CancellationToken ct)
    {
        RpcServerResponse result;
        try { result = await invoke().ConfigureAwait(false); }
        catch
        {
            await publications.WaitAsync(ct).ConfigureAwait(false);
            throw;
        }
        try
        {
            await publications.WaitAsync(ct).ConfigureAwait(false);
            _ = result.Memory; // Reject an uninitialized internal response before sending.
        }
        catch
        {
            result.Dispose();
            throw;
        }
        return result;
    }

    private static TimeSpan GetElapsedTime(long startedAt)
    {
        return Stopwatch.GetElapsedTime(startedAt);
    }

    private void LogRequestReceived(RpcSession session, RpcRequestFrame req)
    {
        _logger.LogTrace(
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
            _logger.LogTrace(
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
        return _registry.TryGetDescriptor(req.ServiceId, req.MethodId, out var descriptor)
            ? descriptor.DisplayName
            : $"{req.ServiceId}:{req.MethodId}";
    }

}
