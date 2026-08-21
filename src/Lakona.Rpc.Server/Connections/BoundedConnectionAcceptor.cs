using System.Runtime.ExceptionServices;
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

        // Start accepting immediately. Scheduling this loop through Task.Run can leave a
        // newly started host idle while the thread pool is saturated by concurrent builds
        // or tests, even though the acceptor itself already exposes an asynchronous API.
        _acceptLoop = AcceptLoopAsync();
    }

    public string ListenAddress => _inner.ListenAddress;

    public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
    {
        while (true)
        {
            RpcAcceptedConnection connection;
            try
            {
                connection = await _pendingConnections.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

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

        // Disposing the concrete listener is the forced-unblock path for an
        // AcceptAsync implementation that cannot observe cancellation promptly.
        ExceptionDispatchInfo? cleanupFailure = null;
        try
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure ??= ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            _pendingConnections.Writer.TryComplete();
        }

        while (_pendingConnections.Reader.TryRead(out var buffered))
            await DisposeRejectedConnectionAsync(buffered).ConfigureAwait(false);

        _disposeCts.Dispose();
        cleanupFailure?.Throw();
    }

    private async Task AcceptLoopAsync()
    {
        Exception? failure = null;
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
        catch (ObjectDisposedException) when (_disposeCts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            _pendingConnections.Writer.TryComplete(failure);
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
