using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Hosting;

// Resolved by DI before the registry calls any factories, so partial registry
// construction cannot orphan serializers which have already been returned.
internal sealed class LakonaEndpointSerializerOwner : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<IRpcSerializer> _serializers = new(ReferenceEqualityComparer.Instance);
    private Task? _disposal;

    public IRpcSerializer Create(string profile, Func<IRpcSerializer> factory)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            var serializer = factory()
                ?? throw new InvalidOperationException($"Endpoint serializer factory '{profile}' returned null.");
            if (serializer is IDisposable or IAsyncDisposable)
                _serializers.Add(serializer);
            return serializer;
        }
    }

    public void Dispose() => BeginDisposal(asynchronous: false).GetAwaiter().GetResult();

    public async ValueTask DisposeAsync() => await BeginDisposal(asynchronous: true).ConfigureAwait(false);

    private Task BeginDisposal(bool asynchronous)
    {
        IRpcSerializer[] serializers;
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_disposal is not null) return _disposal;
            if (!asynchronous && _serializers.Any(static item => item is IAsyncDisposable && item is not IDisposable))
                throw new InvalidOperationException(
                    "An endpoint serializer requires asynchronous disposal. Dispose the host with DisposeAsync (await using).");
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposal = completion.Task;
            serializers = _serializers.ToArray();
            _serializers.Clear();
        }

        // User cleanup runs outside the ownership lock. Every disposer joins this
        // same completion, including failures, rather than attempting cleanup again.
        _ = CompleteDisposalAsync(serializers, asynchronous, completion);
        return completion.Task;
    }

    private static async Task CompleteDisposalAsync(
        IRpcSerializer[] serializers, bool asynchronous, TaskCompletionSource completion)
    {
        List<Exception>? errors = null;
        foreach (var serializer in serializers)
        {
            try
            {
                if (asynchronous && serializer is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (serializer is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        if (errors is null) completion.TrySetResult();
        else completion.TrySetException(new AggregateException("Endpoint serializer cleanup failed.", errors));
    }
}
