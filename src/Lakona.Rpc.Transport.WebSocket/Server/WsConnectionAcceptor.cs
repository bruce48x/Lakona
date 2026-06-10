#if NET10_0_OR_GREATER
using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.WebSocket;

public sealed class WsConnectionAcceptor : IRpcConnectionAcceptor
{
    private readonly WebApplication _app;
    private readonly Channel<RpcAcceptedConnection> _connections;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly string _listenAddress;
    private readonly int _maxPendingAcceptedConnections;
    private int _pendingAcceptedConnections;
    private int _disposed;

    private WsConnectionAcceptor(WebApplication app, string listenAddress, int maxPendingAcceptedConnections)
    {
        _app = app;
        _listenAddress = listenAddress;
        _maxPendingAcceptedConnections = maxPendingAcceptedConnections;
        _connections = Channel.CreateBounded<RpcAcceptedConnection>(new BoundedChannelOptions(maxPendingAcceptedConnections)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public string ListenAddress => _listenAddress;

    public static async ValueTask<WsConnectionAcceptor> CreateAsync(int port, string path, CancellationToken ct = default)
    {
        return await CreateAsync(port, path, "127.0.0.1", ct).ConfigureAwait(false);
    }

    public static async ValueTask<WsConnectionAcceptor> CreateAsync(int port, string path, string host, CancellationToken ct = default)
    {
        return await CreateAsync(
            port,
            path,
            host,
            RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections,
            ct).ConfigureAwait(false);
    }

    public static async ValueTask<WsConnectionAcceptor> CreateAsync(
        int port,
        string path,
        string host,
        int maxPendingAcceptedConnections,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host is required.", nameof(host));

        var bindHost = IPAddress.TryParse(host, out var parsed)
            ? parsed.ToString()
            : host;

        ValidatePendingAcceptedConnectionLimit(maxPendingAcceptedConnections);
        var normalizedPath = NormalizePath(path);
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls($"http://{bindHost}:{port}");

        var app = builder.Build();
        var acceptor = new WsConnectionAcceptor(
            app,
            $"ws://{bindHost}:{port}{normalizedPath}",
            maxPendingAcceptedConnections);

        app.UseWebSockets();
        app.Map(normalizedPath, acceptor.HandleAsync);

        await app.StartAsync(ct).ConfigureAwait(false);
        return acceptor;
    }

    public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var accepted = await _connections.Reader.ReadAsync(ct).ConfigureAwait(false);
            ReleasePendingSlot();
            if (accepted.Transport.IsConnected)
                return accepted;

            await accepted.Transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _disposeCts.Cancel();
        _connections.Writer.TryComplete();

        try
        {
            await _app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            while (_connections.Reader.TryRead(out var buffered))
            {
                ReleasePendingSlot();
                await buffered.Transport.DisposeAsync().ConfigureAwait(false);
            }

            await _app.DisposeAsync().ConfigureAwait(false);
            _disposeCts.Dispose();
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Expected a WebSocket upgrade request.", _disposeCts.Token).ConfigureAwait(false);
            return;
        }

        if (!TryAcquirePendingSlot())
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("RPC server is busy.", _disposeCts.Token).ConfigureAwait(false);
            return;
        }

        var remoteEndPoint = context.Connection.RemoteIpAddress is null
            ? null
            : new IPEndPoint(context.Connection.RemoteIpAddress, context.Connection.RemotePort);

        WsServerTransport? transport = null;
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slotOwnedByHandler = true;
        var transportTransferredToQueue = false;

        try
        {
            transport = new WsServerTransport(
                await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false),
                remoteEndPoint,
                () => completion.TrySetResult(null),
                context.RequestAborted);

            if (!_connections.Writer.TryWrite(new RpcAcceptedConnection(transport, remoteEndPoint?.ToString() ?? "?", remoteEndPoint)))
            {
                ReleaseHandlerPendingSlot();
                await transport.DisposeAsync().ConfigureAwait(false);
                return;
            }

            slotOwnedByHandler = false;
            transportTransferredToQueue = true;

            using var registration = _disposeCts.Token.Register(static state =>
            {
                ((TaskCompletionSource<object?>)state!).TrySetCanceled();
            }, completion);

            await completion.Task.ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            ReleaseHandlerPendingSlot();
            if (!transportTransferredToQueue && transport is not null)
                await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ReleaseHandlerPendingSlot();
            if (!transportTransferredToQueue && transport is not null)
                await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            ReleaseHandlerPendingSlot();
            if (!transportTransferredToQueue && transport is not null)
                await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        void ReleaseHandlerPendingSlot()
        {
            if (!slotOwnedByHandler)
                return;

            slotOwnedByHandler = false;
            ReleasePendingSlot();
        }
    }

    private bool TryAcquirePendingSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _pendingAcceptedConnections);
            if (current >= _maxPendingAcceptedConnections)
                return false;

            if (Interlocked.CompareExchange(ref _pendingAcceptedConnections, current + 1, current) == current)
                return true;
        }
    }

    private void ReleasePendingSlot()
    {
        Interlocked.Decrement(ref _pendingAcceptedConnections);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/ws";

        return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
    }

    private static void ValidatePendingAcceptedConnectionLimit(int maxPendingAcceptedConnections)
    {
        if (maxPendingAcceptedConnections <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxPendingAcceptedConnections),
                "Pending accepted connection limit must be positive.");
    }
}
#endif
