using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using Lakona.Actor.Messaging;

namespace Lakona.Actor.Mailbox;

internal sealed class Mailbox
{
    private static readonly ConcurrentDictionary<long, Mailbox> ActiveMailboxes = new();
    private static long nextId;

    private readonly ActionBlock<Envelope> block;
    private readonly int capacity;
    private readonly long id;
    private long enqueuedCount;
    private long processedCount;
    private long rejectedCount;

    public Mailbox(Func<Envelope, ValueTask> dispatch, int capacity)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        this.capacity = capacity;
        id = Interlocked.Increment(ref nextId);
        block = new ActionBlock<Envelope>(
            async envelope =>
            {
                try
                {
                    await dispatch(envelope).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Increment(ref processedCount);
                }
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = capacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        ActiveMailboxes.TryAdd(id, this);
        _ = RemoveFromActiveWhenCompleted();
    }

    public Task Completion => block.Completion;

    public async ValueTask Send(Envelope envelope, CancellationToken cancellationToken)
    {
        bool accepted = await block.SendAsync(envelope, cancellationToken).ConfigureAwait(false);

        if (!accepted)
        {
            Interlocked.Increment(ref rejectedCount);
            throw new InvalidOperationException("The actor mailbox is completed.");
        }

        Interlocked.Increment(ref enqueuedCount);
        LakonaActorDiagnostics.MessageAcceptedCounter.Add(1, CreateKindTag(envelope));
    }

    public bool TrySend(Envelope envelope)
    {
        bool accepted = block.Post(envelope);

        if (accepted)
        {
            Interlocked.Increment(ref enqueuedCount);
            LakonaActorDiagnostics.MessageAcceptedCounter.Add(1, CreateKindTag(envelope));
            return true;
        }

        Interlocked.Increment(ref rejectedCount);
        return false;
    }

    public void Complete()
    {
        block.Complete();
    }

    public MailboxMetrics GetMetrics()
    {
        return new MailboxMetrics(
            capacity,
            block.InputCount,
            Interlocked.Read(ref enqueuedCount),
            Interlocked.Read(ref processedCount),
            Interlocked.Read(ref rejectedCount),
            Completion.IsCompleted);
    }

    public static long GetTotalQueuedCount()
    {
        return ActiveMailboxes.Values.Sum(static mailbox => mailbox.block.InputCount);
    }

    private static KeyValuePair<string, object?> CreateKindTag(Envelope envelope)
    {
        return new KeyValuePair<string, object?>("kind", envelope.Response is null ? "send" : "call");
    }

    private async Task RemoveFromActiveWhenCompleted()
    {
        try
        {
            await block.Completion.ConfigureAwait(false);
        }
        finally
        {
            ActiveMailboxes.TryRemove(id, out _);
        }
    }
}
