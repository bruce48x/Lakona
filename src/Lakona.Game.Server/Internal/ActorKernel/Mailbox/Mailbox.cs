using System.Threading.Channels;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Mailbox;

internal sealed class Mailbox
{
    private static long totalQueuedCount;

    private readonly Channel<Envelope> channel;
    private readonly Func<Envelope, ValueTask> dispatch;
    private readonly Task completion;
    private readonly int capacity;
    private readonly SemaphoreSlim availableSlots;
    private long queuedCount;
    private long enqueuedCount;
    private long processedCount;
    private long rejectedCount;

    public Mailbox(Func<Envelope, ValueTask> dispatch, int capacity)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        this.dispatch = dispatch;
        this.capacity = capacity;
        availableSlots = new SemaphoreSlim(capacity, capacity);
        channel = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        completion = ProcessAsync();
    }

    public Task Completion => completion;

    public async ValueTask Send(Envelope envelope, CancellationToken cancellationToken)
    {
        await availableSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        IncrementQueued();
        if (channel.Writer.TryWrite(envelope))
        {
            Interlocked.Increment(ref enqueuedCount);
            LakonaActorDiagnostics.MessageAcceptedCounter.Add(1, CreateKindTag(envelope));
            return;
        }

        DecrementQueued();
        availableSlots.Release();
        Interlocked.Increment(ref rejectedCount);
        throw new InvalidOperationException("The actor mailbox is completed.");
    }

    public bool TrySend(Envelope envelope)
    {
        if (!availableSlots.Wait(0))
        {
            Interlocked.Increment(ref rejectedCount);
            return false;
        }

        IncrementQueued();
        if (channel.Writer.TryWrite(envelope))
        {
            Interlocked.Increment(ref enqueuedCount);
            LakonaActorDiagnostics.MessageAcceptedCounter.Add(1, CreateKindTag(envelope));
            return true;
        }

        DecrementQueued();
        availableSlots.Release();
        Interlocked.Increment(ref rejectedCount);
        return false;
    }

    public void Complete()
    {
        channel.Writer.TryComplete();
    }

    public MailboxMetrics GetMetrics()
    {
        return new MailboxMetrics(
            capacity,
            checked((int)Volatile.Read(ref queuedCount)),
            Interlocked.Read(ref enqueuedCount),
            Interlocked.Read(ref processedCount),
            Interlocked.Read(ref rejectedCount),
            Completion.IsCompleted);
    }

    public static long GetTotalQueuedCount()
    {
        return Volatile.Read(ref totalQueuedCount);
    }

    private static KeyValuePair<string, object?> CreateKindTag(Envelope envelope)
    {
        return new KeyValuePair<string, object?>("kind", envelope.Response is null ? "send" : "call");
    }

    private void IncrementQueued()
    {
        Interlocked.Increment(ref queuedCount);
        Interlocked.Increment(ref totalQueuedCount);
    }

    private void DecrementQueued()
    {
        Interlocked.Decrement(ref queuedCount);
        Interlocked.Decrement(ref totalQueuedCount);
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var envelope))
                {
                    DecrementQueued();
                    try
                    {
                        await dispatch(envelope).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Increment(ref processedCount);
                        availableSlots.Release();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            channel.Writer.TryComplete(exception);
            while (channel.Reader.TryRead(out _))
            {
                DecrementQueued();
                availableSlots.Release();
            }

            throw;
        }
    }
}
