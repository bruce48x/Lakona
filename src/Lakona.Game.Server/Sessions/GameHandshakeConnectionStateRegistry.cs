using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameHandshakeConnectionStateRegistry
{
    private readonly ConcurrentDictionary<string, GameHandshakeConnectionLease> connections =
        new(StringComparer.Ordinal);

    public bool IsComplete(string connectionId)
    {
        return connections.TryGetValue(connectionId, out var state) && state.IsComplete;
    }

    public bool MarkComplete(string connectionId)
    {
        return connections.TryGetValue(connectionId, out var state) && state.MarkComplete();
    }

    public bool TryClose(string connectionId)
    {
        return connections.TryGetValue(connectionId, out var state) && state.TryClose();
    }

    public GameHandshakeConnectionLease RegisterPending(
        string connectionId,
        TimeSpan timeout,
        SemaphoreSlim pendingHandshakes,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id cannot be empty.", nameof(connectionId));

        GameHandshakeConnectionLease lease;
        try
        {
            lease = new GameHandshakeConnectionLease(
                this,
                connectionId,
                pendingHandshakes,
                logger);
        }
        catch
        {
            pendingHandshakes.Release();
            throw;
        }

        if (!connections.TryAdd(connectionId, lease))
        {
            lease.DisposeUnregistered();
            throw new InvalidOperationException($"Connection '{connectionId}' is already registered for game handshake.");
        }

        lease.StartDeadline(timeout);
        return lease;
    }

    internal void Remove(string connectionId, GameHandshakeConnectionLease lease)
    {
        connections.TryRemove(new KeyValuePair<string, GameHandshakeConnectionLease>(connectionId, lease));
    }
}

internal sealed class GameHandshakeConnectionLease : IAsyncDisposable
{
    private const int Pending = 0;
    private const int Complete = 1;
    private const int Closed = 2;

    private readonly GameHandshakeConnectionStateRegistry _owner;
    private readonly string _connectionId;
    private readonly SemaphoreSlim _pendingHandshakes;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _deadlineCancellation = new();
    private readonly CancellationTokenSource _sessionCancellation = new();
    private Task? _deadlineTask;
    private int _disposed;
    private int _state;

    public GameHandshakeConnectionLease(
        GameHandshakeConnectionStateRegistry owner,
        string connectionId,
        SemaphoreSlim pendingHandshakes,
        ILogger logger)
    {
        _owner = owner;
        _connectionId = connectionId;
        _pendingHandshakes = pendingHandshakes;
        _logger = logger;
    }

    public bool IsComplete => Volatile.Read(ref _state) == Complete;

    public CancellationToken SessionCancellation => _sessionCancellation.Token;

    public void StartDeadline(TimeSpan timeout)
    {
        _deadlineTask = MonitorDeadlineAsync(timeout);
    }

    public bool MarkComplete()
    {
        if (Interlocked.CompareExchange(ref _state, Complete, Pending) == Pending)
        {
            _pendingHandshakes.Release();
            _deadlineCancellation.Cancel();
            return true;
        }

        return Volatile.Read(ref _state) == Complete;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        TryClose();
        try
        {
            if (_deadlineTask is not null)
                await _deadlineTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _deadlineCancellation.Dispose();
        _sessionCancellation.Dispose();
    }

    internal void DisposeUnregistered()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        TryClose();
        _deadlineCancellation.Dispose();
        _sessionCancellation.Dispose();
    }

    private async Task MonitorDeadlineAsync(TimeSpan timeout)
    {
        try
        {
            await Task.Delay(timeout, _deadlineCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_deadlineCancellation.IsCancellationRequested)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _state, Closed, Pending) != Pending)
            return;

        _pendingHandshakes.Release();
        _owner.Remove(_connectionId, this);
        _logger.LogWarning(
            "Game handshake timed out for RPC connection {ConnectionId}.",
            _connectionId);
        _sessionCancellation.Cancel();
    }

    internal bool TryClose()
    {
        var previous = Interlocked.Exchange(ref _state, Closed);
        if (previous == Closed)
            return false;
        if (previous == Pending)
            _pendingHandshakes.Release();

        _owner.Remove(_connectionId, this);
        try
        {
            _deadlineCancellation.Cancel();
            _sessionCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }
}
