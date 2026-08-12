using System.Collections.Concurrent;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.ReliablePush;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationCommandRouter : IClientNotificationCommandRouter, IDisposable, IAsyncDisposable
{
    internal const int DefaultCapacityPerSession = 256;
    internal const int DefaultTotalCapacity = 65_536;

    private readonly IReliablePushRuntime? _localOwner;
    private readonly LocalClientNotificationCommandDispatcher? _localDispatcher;
    private readonly IClusterMembership? _membership;
    private readonly IClientNotificationRemoteDispatcher? _remoteDispatcher;
    private readonly NodeId? _localNode;
    private readonly ILogger<ClientNotificationCommandRouter>? _logger;
    private readonly int _capacityPerSession;
    private readonly int _totalCapacity;
    private readonly ConcurrentDictionary<GameSessionKey, SessionQueue> _queues = new();
    private readonly CancellationTokenSource _shutdown = new();
    private int _disposed;
    private int _pendingTotal;

    public ClientNotificationCommandRouter(
        IReliablePushRuntime localOwner,
        IClusterMembership? membership = null,
        IClientNotificationRemoteDispatcher? remoteDispatcher = null,
        NodeId? localNode = null,
        ILogger<ClientNotificationCommandRouter>? logger = null,
        int capacityPerSession = DefaultCapacityPerSession,
        int totalCapacity = DefaultTotalCapacity)
    {
        _localOwner = localOwner ?? throw new ArgumentNullException(nameof(localOwner));
        _membership = membership;
        _remoteDispatcher = remoteDispatcher;
        _localNode = localNode;
        _logger = logger;
        _capacityPerSession = ValidateCapacity(capacityPerSession);
        _totalCapacity = ValidateTotalCapacity(totalCapacity);
    }

    public ClientNotificationCommandRouter(
        LocalClientNotificationCommandDispatcher localDispatcher,
        IClusterMembership? membership = null,
        IClientNotificationRemoteDispatcher? remoteDispatcher = null,
        NodeId? localNode = null,
        ILogger<ClientNotificationCommandRouter>? logger = null,
        int capacityPerSession = DefaultCapacityPerSession,
        int totalCapacity = DefaultTotalCapacity)
    {
        _localDispatcher = localDispatcher ?? throw new ArgumentNullException(nameof(localDispatcher));
        _membership = membership;
        _remoteDispatcher = remoteDispatcher;
        _localNode = localNode;
        _logger = logger;
        _capacityPerSession = ValidateCapacity(capacityPerSession);
        _totalCapacity = ValidateTotalCapacity(totalCapacity);
    }

    public ClientNotificationStatus Enqueue(ClientNotificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return EnqueueWorkItem(new CommandWorkItem(ToSessionKey(command), command));
    }

    public ClientNotificationStatus EnqueueGenerated<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload)
        where TCallback : class =>
        EnqueueWorkItem(
            new GeneratedWorkItem<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload));

    internal async ValueTask WaitForIdleAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        while (_queues.TryGetValue(session, out var queue))
        {
            Task? drain;
            lock (queue.Gate)
            {
                drain = queue.DrainTask;
            }

            if (drain is not null)
            {
                await drain.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        WaitForDrainsAsync().GetAwaiter().GetResult();
        _shutdown.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        await WaitForDrainsAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private ClientNotificationStatus EnqueueWorkItem(ClientNotificationWorkItem item)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return ClientNotificationStatus.Failed;
        }

        while (true)
        {
            var queue = _queues.GetOrAdd(
                item.Session,
                static (session, owner) => new SessionQueue(owner, session),
                this);
            lock (queue.Gate)
            {
                if (queue.Retired)
                {
                    continue;
                }

                if (Volatile.Read(ref _disposed) != 0)
                {
                    return ClientNotificationStatus.Failed;
                }

                if (queue.PendingCount >= _capacityPerSession)
                {
                    _logger?.LogWarning(
                        "Client notification admission rejected because the per-session queue reached its capacity of {Capacity}.",
                        _capacityPerSession);
                    return ClientNotificationStatus.Backpressure;
                }

                if (Interlocked.Increment(ref _pendingTotal) > _totalCapacity)
                {
                    Interlocked.Decrement(ref _pendingTotal);
                    _logger?.LogWarning(
                        "Client notification admission rejected because the process queue reached its total capacity of {Capacity}.",
                        _totalCapacity);
                    return ClientNotificationStatus.Backpressure;
                }

                queue.Items.Enqueue(item);
                queue.PendingCount++;
                queue.DrainTask ??= StartDrain(queue);
                return ClientNotificationStatus.Accepted;
            }
        }
    }

    private static Task StartDrain(SessionQueue queue)
    {
        if (ExecutionContext.IsFlowSuppressed())
        {
            return QueueDrain(queue);
        }

        using (ExecutionContext.SuppressFlow())
        {
            return QueueDrain(queue);
        }
    }

    private static Task QueueDrain(SessionQueue queue) =>
        Task.Factory.StartNew(
                static state =>
                {
                    var queuedSession = (SessionQueue)state!;
                    return queuedSession.Owner.DrainAsync(queuedSession);
                },
                queue,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default)
            .Unwrap();

    private async Task DrainAsync(SessionQueue queue)
    {
        while (true)
        {
            ClientNotificationWorkItem item;
            lock (queue.Gate)
            {
                if (_shutdown.IsCancellationRequested)
                {
                    Retire(queue, discardPending: true);
                    return;
                }

                if (!queue.Items.TryDequeue(out item!))
                {
                    Retire(queue, discardPending: false);
                    return;
                }
            }

            try
            {
                var status = await item.DeliverAsync(this, _shutdown.Token).ConfigureAwait(false);
                if (status != ClientNotificationStatus.Accepted)
                {
                    _logger?.LogDebug(
                        "Background client notification delivery completed with status {Status}.",
                        status);
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                // Host shutdown owns cancellation after admission.
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(
                    exception,
                    "Background client notification delivery failed after framework admission.");
            }
            finally
            {
                lock (queue.Gate)
                {
                    queue.PendingCount--;
                }
                Interlocked.Decrement(ref _pendingTotal);
            }
        }
    }

    private void Retire(SessionQueue queue, bool discardPending)
    {
        queue.Retired = true;
        if (discardPending)
        {
            var discarded = queue.PendingCount;
            queue.Items.Clear();
            queue.PendingCount = 0;
            if (discarded > 0)
            {
                Interlocked.Add(ref _pendingTotal, -discarded);
            }
        }

        ((ICollection<KeyValuePair<GameSessionKey, SessionQueue>>)_queues)
            .Remove(new KeyValuePair<GameSessionKey, SessionQueue>(queue.Session, queue));
    }

    private async Task WaitForDrainsAsync()
    {
        var drains = _queues.Values
            .Select(queue =>
            {
                lock (queue.Gate)
                {
                    return queue.DrainTask;
                }
            })
            .OfType<Task>()
            .ToArray();

        if (drains.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(drains).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _queues.Clear();
        }
    }

    private async ValueTask<ClientNotificationStatus> DeliverCommandAsync(
        GameSessionKey session,
        ClientNotificationCommand command,
        CancellationToken cancellationToken)
    {
        if (_membership is null || _remoteDispatcher is null || _localNode is null)
        {
            return await DispatchLocalAsync(session, command, cancellationToken).ConfigureAwait(false);
        }

        var route = await ResolveAsync(session, cancellationToken).ConfigureAwait(false);
        if (route is null)
        {
            return MembershipSessionLocator.ClassifyMissing(session.SessionId, _membership);
        }

        if (route!.Node == _localNode.Value)
        {
            return await DispatchLocalAsync(session, command, cancellationToken).ConfigureAwait(false);
        }

        command.Metadata = null;
        return await _remoteDispatcher.DispatchAsync(route, command, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ClientNotificationStatus> DeliverGeneratedAsync<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        if (_membership is null || _remoteDispatcher is null || _localNode is null)
        {
            return await DispatchGeneratedLocalAsync<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        var route = await ResolveAsync(session, cancellationToken).ConfigureAwait(false);
        if (route is null)
        {
            return MembershipSessionLocator.ClassifyMissing(session.SessionId, _membership);
        }

        if (route!.Node == _localNode.Value)
        {
            return await DispatchGeneratedLocalAsync<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        var command = ClientNotificationCommandFactory.CreateGenerated<TCallback, TPayload>(
            session,
            serviceId,
            methodId,
            methodName,
            payload);
        return await _remoteDispatcher.DispatchAsync(route, command, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<ClientNotificationStatus> DispatchLocalAsync(
        GameSessionKey session,
        ClientNotificationCommand command,
        CancellationToken cancellationToken) =>
        _localOwner is not null
            ? _localOwner.PublishAsync(session, command, cancellationToken)
            : _localDispatcher!.DispatchAsync(command, cancellationToken);

    private ValueTask<RouteLocation?> ResolveAsync(
        GameSessionKey session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<RouteLocation?>(
            MembershipSessionLocator.TryResolve(session, _membership!, out var target)
                ? target
                : null);
    }

    private ValueTask<ClientNotificationStatus> DispatchGeneratedLocalAsync<TCallback, TPayload>(
        GameSessionKey session,
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload,
        CancellationToken cancellationToken)
        where TCallback : class =>
        _localOwner is not null
            ? _localOwner.PublishGeneratedAsync<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                methodName,
                payload,
                cancellationToken)
            : _localDispatcher!.DispatchGeneratedAsync<TCallback, TPayload>(
                session,
                serviceId,
                methodId,
                payload,
                cancellationToken);

    private static int ValidateCapacity(int capacityPerSession)
    {
        if (capacityPerSession <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacityPerSession),
                capacityPerSession,
                "The client notification queue capacity must be greater than zero.");
        }

        return capacityPerSession;
    }

    private static int ValidateTotalCapacity(int totalCapacity)
    {
        if (totalCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalCapacity),
                totalCapacity,
                "The total client notification queue capacity must be greater than zero.");
        }

        return totalCapacity;
    }

    private static GameSessionKey ToSessionKey(ClientNotificationCommand command) =>
        new(command.OwnerKey, command.SessionId);

    private sealed class SessionQueue
    {
        public SessionQueue(ClientNotificationCommandRouter owner, GameSessionKey session)
        {
            Owner = owner;
            Session = session;
        }

        public ClientNotificationCommandRouter Owner { get; }

        public GameSessionKey Session { get; }

        public object Gate { get; } = new();

        public Queue<ClientNotificationWorkItem> Items { get; } = new();

        public Task? DrainTask { get; set; }

        public int PendingCount { get; set; }

        public bool Retired { get; set; }
    }

    private abstract class ClientNotificationWorkItem
    {
        protected ClientNotificationWorkItem(GameSessionKey session)
        {
            Session = session;
        }

        public GameSessionKey Session { get; }

        public abstract ValueTask<ClientNotificationStatus> DeliverAsync(
            ClientNotificationCommandRouter router,
            CancellationToken cancellationToken);
    }

    private sealed class CommandWorkItem : ClientNotificationWorkItem
    {
        private readonly ClientNotificationCommand _command;

        public CommandWorkItem(GameSessionKey session, ClientNotificationCommand command)
            : base(session)
        {
            _command = command;
        }

        public override ValueTask<ClientNotificationStatus> DeliverAsync(
            ClientNotificationCommandRouter router,
            CancellationToken cancellationToken) =>
            router.DeliverCommandAsync(Session, _command, cancellationToken);
    }

    private sealed class GeneratedWorkItem<TCallback, TPayload> : ClientNotificationWorkItem
        where TCallback : class
    {
        private readonly int _serviceId;
        private readonly int _methodId;
        private readonly string _methodName;
        private readonly TPayload _payload;

        public GeneratedWorkItem(
            GameSessionKey session,
            int serviceId,
            int methodId,
            string methodName,
            TPayload payload)
            : base(session)
        {
            _serviceId = serviceId;
            _methodId = methodId;
            _methodName = methodName;
            _payload = payload;
        }

        public override ValueTask<ClientNotificationStatus> DeliverAsync(
            ClientNotificationCommandRouter router,
            CancellationToken cancellationToken) =>
            router.DeliverGeneratedAsync<TCallback, TPayload>(
                Session,
                _serviceId,
                _methodId,
                _methodName,
                _payload,
                cancellationToken);
    }
}
