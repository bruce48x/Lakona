using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

internal sealed class BoundedConnectionAcceptor : IRpcConnectionAcceptor
{
    private readonly IRpcConnectionAcceptor _inner;
    private readonly ILogger _logger;
    private readonly Channel<RpcAcceptedConnection> _pendingConnections;
    private readonly CancellationTokenSource _disposeCts;
    private readonly Task _acceptLoop;
    private int _disposed;

    public BoundedConnectionAcceptor(
        IRpcConnectionAcceptor inner,
        int maxPendingAcceptedConnections,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (maxPendingAcceptedConnections <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxPendingAcceptedConnections),
                "Pending accepted connection limit must be positive.");

        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _disposeCts = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();
        _pendingConnections = Channel.CreateBounded<RpcAcceptedConnection>(new BoundedChannelOptions(maxPendingAcceptedConnections)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string ListenAddress => _inner.ListenAddress;

    public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var connection = await _pendingConnections.Reader.ReadAsync(ct).ConfigureAwait(false);
            if (connection.Transport.IsConnected)
                return connection;

            await DisposeRejectedConnectionAsync(connection).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            _disposeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _pendingConnections.Writer.TryComplete();
        }

        while (_pendingConnections.Reader.TryRead(out var buffered))
            await DisposeRejectedConnectionAsync(buffered).ConfigureAwait(false);

        await _inner.DisposeAsync().ConfigureAwait(false);
        _disposeCts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_disposeCts.IsCancellationRequested)
            {
                var connection = await _inner.AcceptAsync(_disposeCts.Token).ConfigureAwait(false);
                if (_pendingConnections.Writer.TryWrite(connection))
                    continue;

                _logger.LogWarning(
                    "[{DisplayName}] Rejected because the pending accepted connection queue is full.",
                    connection.DisplayName);
                await DisposeRejectedConnectionAsync(connection).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
        finally
        {
            _pendingConnections.Writer.TryComplete();
        }
    }

    private static async ValueTask DisposeRejectedConnectionAsync(RpcAcceptedConnection connection)
    {
        try
        {
            await connection.Transport.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
