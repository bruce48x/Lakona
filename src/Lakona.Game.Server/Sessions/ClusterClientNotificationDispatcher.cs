using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using System.Text;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotificationBatchOptions
{
    public TimeSpan Window { get; set; } = TimeSpan.FromMilliseconds(10);

    public int MaximumBatchSize { get; set; } = 256;

    public int MaximumBatchBytes { get; set; } = 256 * 1024;

    public int MaximumPendingBytesPerSession { get; set; } = 128 * 1024 * 1024;
    public int MaximumPendingBytesPerProcess { get; set; } = 512 * 1024 * 1024;

    internal void Validate()
    {
        if (Window < TimeSpan.Zero || Window > TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(Window));
        }

        if (MaximumBatchSize <= 0 || MaximumBatchSize > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumBatchSize));
        }

        if (MaximumBatchBytes < 128 || MaximumBatchBytes > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumBatchBytes));
        }
    }
}

internal sealed class ClusterClientNotificationDispatcher :
    IClientNotificationRemoteDispatcher,
    IAsyncDisposable
{
    private readonly IClusterClientFactory clientFactory;
    private readonly ClientNotificationBatchOptions options;
    private readonly object gate = new();
    private readonly Dictionary<GatewayKey, PendingBatch> batches = new();
    private int disposed;
    private readonly HashSet<PendingBatch> outstanding = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly NotificationByteBudget<GameSessionKey> byteBudget;

    public ClusterClientNotificationDispatcher(
        IClusterClientFactory clientFactory,
        ClientNotificationBatchOptions? options = null)
    {
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        this.options = options ?? new ClientNotificationBatchOptions();
        this.options.Validate();
        byteBudget = new(this.options.MaximumPendingBytesPerSession, this.options.MaximumPendingBytesPerProcess);
    }

    public async ValueTask<ClientNotificationStatus> DispatchAsync(
        RouteLocation target,
        ClientNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<ClientNotificationStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        List<PendingBatch>? flushes = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            var estimatedBytes = EstimateBytes(command);
            var reservation = byteBudget.TryReserve(new GameSessionKey(command.OwnerKey, command.SessionId),
                ClientNotificationSize.RetainedBytes(command));
            if (reservation is null)
            {
                ClientNotificationDiagnostics.RecordBackpressure(
                    ClientNotificationBackpressureReason.PendingBytes);
                completion.TrySetResult(ClientNotificationStatus.Backpressure);
                goto ExitLock;
            }

            var key = GatewayKey.From(target);
            if (!batches.TryGetValue(key, out var batch))
            {
                batch = new PendingBatch(key, target);
                batches.Add(key, batch);
                outstanding.Add(batch);
            }

            if (batch.Items.Count > 0
                && batch.EstimatedBytes + estimatedBytes > options.MaximumBatchBytes)
            {
                batches.Remove(key);
                (flushes ??= []).Add(batch);
                batch = new PendingBatch(key, target);
                batches.Add(key, batch);
                outstanding.Add(batch);
            }

            batch.Items.Add(new PendingItem(command, completion, reservation));
            batch.EstimatedBytes += estimatedBytes;
            if (batch.Items.Count >= options.MaximumBatchSize || batch.EstimatedBytes >= options.MaximumBatchBytes || options.Window == TimeSpan.Zero)
            {
                batches.Remove(key);
                (flushes ??= []).Add(batch);
            }
            else if (!batch.TimerStarted)
            {
                batch.TimerStarted = true;
                _ = FlushAfterWindowAsync(batch);
            }

        ExitLock:;
        }

        if (flushes is not null)
        {
            for (var i = 0; i < flushes.Count; i++)
            {
                _ = FlushAsync(flushes[i]);
            }
        }

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        PendingBatch[] pending;
        lock (gate)
        {
            pending = outstanding.ToArray();
            batches.Clear();
        }
        shutdown.Cancel();
        foreach (var batch in pending) _ = FlushAsync(batch);
        await Task.WhenAll(pending.Select(static batch => batch.Done.Task)).ConfigureAwait(false);
        shutdown.Dispose();
    }

    private async Task FlushAfterWindowAsync(PendingBatch batch)
    {
        try { await Task.Delay(options.Window, shutdown.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        lock (gate)
        {
            if (!batches.TryGetValue(batch.Key, out var current) || !ReferenceEquals(current, batch)) return;
            batches.Remove(batch.Key);
        }
        await FlushAsync(batch).ConfigureAwait(false);
    }

    private async Task FlushAsync(PendingBatch batch)
    {
        if (Interlocked.Exchange(ref batch.Started, 1) != 0) return;
        int[]? statuses = null;
        try
        {
            var client = await clientFactory.GetClientAsync(batch.Target, shutdown.Token).ConfigureAwait(false);
            var reply = await client.CallAsync(
                ClusterClientNotificationProtocol.BatchDispatchMethod,
                new ClientNotificationBatchDispatchRequest
                {
                    Commands = batch.Items.Select(static item => item.Command).ToArray()
                }, shutdown.Token).ConfigureAwait(false);
            if (reply is not null && reply.Statuses.Length == batch.Items.Count) statuses = reply.Statuses;
        }
        catch
        {
            // Complete all items with a delivery failure after releasing their reservations.
        }
        finally
        {
            foreach (var item in batch.Items) item.Reservation.Dispose();
            lock (gate) outstanding.Remove(batch);
            for (var i = 0; i < batch.Items.Count; i++)
            {
                var raw = statuses is null ? (int)ClientNotificationStatus.Failed : statuses[i];
                batch.Items[i].Completion.TrySetResult(Enum.IsDefined(typeof(ClientNotificationStatus), raw)
                    ? (ClientNotificationStatus)raw : ClientNotificationStatus.Failed);
            }
            // An already scheduled batching timer may still reference this batch.
            batch.Items.Clear();
            batch.Done.TrySetResult();
        }
    }

    private static int EstimateBytes(ClientNotificationCommand command)
    {
        var bytes = 64;
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(command.OwnerKey));
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(command.SessionId));
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(command.CallbackContractType));
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(command.MethodName));
        if (command.SerializerProfile is { } profile)
            bytes = checked(bytes + Encoding.UTF8.GetByteCount(profile));
        bytes = checked(bytes + command.Payload.Length);

        if (command.Metadata is not null)
        {
            bytes = checked(bytes + 16);
            bytes = checked(bytes + Encoding.UTF8.GetByteCount(command.Metadata.Type));
            bytes = checked(bytes + command.Metadata.Payload.Length);
        }

        return bytes;
    }

    private readonly record struct GatewayKey(
        string Node,
        string Endpoint,
        Guid Cluster,
        Guid Incarnation)
    {
        public static GatewayKey From(RouteLocation target) => new(
            target.NodeReference.Node.Value,
            target.Endpoint.Address,
            target.NodeReference.Cluster.Value,
            target.NodeReference.Incarnation.Value);
    }

    private sealed class PendingBatch
    {
        public PendingBatch(GatewayKey key, RouteLocation target)
        {
            Key = key;
            Target = target;
        }

        public GatewayKey Key { get; }
        public RouteLocation Target { get; }
        public List<PendingItem> Items { get; } = new();
        public int EstimatedBytes { get; set; }
        public bool TimerStarted { get; set; }
        public int Started;
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record PendingItem(
        ClientNotificationCommand Command,
        TaskCompletionSource<ClientNotificationStatus> Completion,
        IDisposable Reservation);
}
