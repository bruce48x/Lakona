using System.Collections.Concurrent;
using System.Threading.Tasks.Sources;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Client;

internal sealed class RpcPendingRequestCollection
{
    private readonly ConcurrentDictionary<uint, PendingCall> _pending = new();

    public uint Reserve(ref int nextRequestId, PendingCall call)
    {
        for (uint attempts = 0; attempts < uint.MaxValue; attempts++)
        {
            var id = unchecked((uint)Interlocked.Increment(ref nextRequestId));
            if (id != 0 && _pending.TryAdd(id, call)) return id;
        }
        throw new InvalidOperationException("No RPC request id available; too many pending requests.");
    }

    public bool TryCancel(uint id, CancellationToken token) => Fail(id, new TaskCanceledException("RPC request was canceled.", null, token));

    public bool Fail(uint id, Exception error)
    {
        if (!_pending.TryRemove(id, out var call)) return false;
        call.Fail(error);
        return true;
    }

    public bool Contains(uint id) => _pending.ContainsKey(id);

    public void Complete(RpcResponseFrame response)
    {
        if (_pending.TryRemove(response.RequestId, out var call)) call.Complete(response);
        else response.Dispose();
    }

    public void FailAll(Exception error)
    {
        foreach (var item in _pending) Fail(item.Key, error);
    }

    internal abstract class PendingCall
    {
        private readonly object _gate = new();
        private CancellationTokenRegistration _registration;
        private bool _finished;

        public void SetCancellationRegistration(CancellationTokenRegistration registration)
        {
            lock (_gate)
            {
                if (!_finished) { _registration = registration; return; }
            }
            registration.Dispose();
        }

        protected void Finish()
        {
            CancellationTokenRegistration registration;
            lock (_gate)
            {
                _finished = true;
                registration = _registration;
                _registration = default;
            }
            registration.Dispose();
        }

        public abstract void Complete(RpcResponseFrame response);
        public abstract void Fail(Exception error);
    }

    // Single-use: the pending dictionary arbitrates response, cancellation and send failure.
    // ManualResetValueTaskSourceCore handles completion/continuation-registration races.
    internal sealed class PendingCall<T> : PendingCall, IValueTaskSource<T>
    {
        private ManualResetValueTaskSourceCore<T> _source;
        private readonly Func<RpcResponseFrame, T> _convert;

        public PendingCall(Func<RpcResponseFrame, T> convert) { _convert = convert; }
        public ValueTask<T> Task => new ValueTask<T>(this, _source.Version);

        public override void Complete(RpcResponseFrame response)
        {
            T value;
            try { using (response) value = _convert(response); }
            catch (Exception error) { Fail(error); return; }
            Finish();
            _source.SetResult(value);
        }

        public override void Fail(Exception error)
        {
            Finish();
            _source.SetException(error);
        }

        public T GetResult(short token) => _source.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _source.OnCompleted(continuation, state, token, flags);
    }
}
