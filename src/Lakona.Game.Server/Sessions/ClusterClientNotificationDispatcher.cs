using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using System.Text;

namespace Lakona.Game.Server.Sessions;

public sealed class ClientNotificationBatchOptions
{
    public TimeSpan Window { get; set; } = TimeSpan.FromMilliseconds(10);

    public int MaximumBatchSize { get; set; } = 256;

    public int MaximumBatchBytes { get; set; } = 256 * 1024;

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

public sealed class ClusterClientNotificationDispatcher :
    IClientNotificationRemoteDispatcher,
    IAsyncDisposable
{
    private readonly IClusterClientFactory clientFactory;
    private readonly ClientNotificationBatchOptions options;
    private readonly object gate = new();
    private readonly Dictionary<GatewayKey, PendingBatch> batches = new();
    private int disposed;

    public ClusterClientNotificationDispatcher(
        IClusterClientFactory clientFactory,
        ClientNotificationBatchOptions? options = null)
    {
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        this.options = options ?? new ClientNotificationBatchOptions();
        this.options.Validate();
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
            if (estimatedBytes > options.MaximumBatchBytes)
            {
                completion.TrySetResult(ClientNotificationStatus.Backpressure);
                goto ExitLock;
            }

            var key = GatewayKey.From(target);
            if (!batches.TryGetValue(key, out var batch))
            {
                batch = new PendingBatch(key, target);
                batches.Add(key, batch);
            }

            if (batch.Items.Count > 0
                && batch.EstimatedBytes + estimatedBytes > options.MaximumBatchBytes)
            {
                batches.Remove(key);
                (flushes ??= []).Add(batch);
                batch = new PendingBatch(key, target);
                batches.Add(key, batch);
            }

            batch.Items.Add(new PendingItem(command, completion));
            batch.EstimatedBytes += estimatedBytes;
            if (batch.Items.Count >= options.MaximumBatchSize || options.Window == TimeSpan.Zero)
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
            pending = batches.Values.ToArray();
            batches.Clear();
        }

        await Task.WhenAll(pending.Select(FlushAsync)).ConfigureAwait(false);
    }

    private async Task FlushAfterWindowAsync(PendingBatch batch)
    {
        await Task.Delay(options.Window).ConfigureAwait(false);
        lock (gate)
        {
            if (!batches.TryGetValue(batch.Key, out var current)
                || !ReferenceEquals(current, batch))
            {
                return;
            }

            batches.Remove(batch.Key);
        }

        await FlushAsync(batch).ConfigureAwait(false);
    }

    private async Task FlushAsync(PendingBatch batch)
    {
        try
        {
            var client = await clientFactory.GetClientAsync(batch.Target).ConfigureAwait(false);
            var reply = await client.CallAsync(
                ClusterClientNotificationProtocol.BatchDispatchMethod,
                new ClientNotificationBatchDispatchRequest
                {
                    Commands = batch.Items.Select(static item => item.Command).ToArray()
                }).ConfigureAwait(false);
            if (reply is null || reply.Statuses.Length != batch.Items.Count)
            {
                CompleteAll(batch, ClientNotificationStatus.Failed);
                return;
            }

            for (var i = 0; i < batch.Items.Count; i++)
            {
                var raw = reply.Statuses[i];
                batch.Items[i].Completion.TrySetResult(
                    Enum.IsDefined(typeof(ClientNotificationStatus), raw)
                        ? (ClientNotificationStatus)raw
                        : ClientNotificationStatus.Failed);
            }
        }
        catch
        {
            CompleteAll(batch, ClientNotificationStatus.Failed);
        }
    }

    private static void CompleteAll(PendingBatch batch, ClientNotificationStatus status)
    {
        for (var i = 0; i < batch.Items.Count; i++)
        {
            batch.Items[i].Completion.TrySetResult(status);
        }
    }

    private static int EstimateBytes(ClientNotificationCommand command)
    {
        var bytes = 64;
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(command.OwnerKey));
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(command.SessionId));
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(command.CallbackContractType));
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(command.MethodName));
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
    }

    private sealed record PendingItem(
        ClientNotificationCommand Command,
        TaskCompletionSource<ClientNotificationStatus> Completion);
}
