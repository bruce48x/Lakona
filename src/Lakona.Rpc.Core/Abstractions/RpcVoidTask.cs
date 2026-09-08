using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Lakona.Rpc.Core
{
    /// <summary>Adapts a generated void RPC without adding an asynchronous task wrapper.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class RpcVoidTask
    {
        public static ValueTask FromResult(ValueTask<RpcVoid> result)
        {
            if (result.IsCompletedSuccessfully)
            {
                result.GetAwaiter().GetResult();
                return default;
            }
            return new Completion(result).Task;
        }

        private sealed class Completion : IValueTaskSource
        {
            private ManualResetValueTaskSourceCore<bool> _source;
            private readonly ValueTask<RpcVoid> _result;

            public Completion(ValueTask<RpcVoid> result)
            {
                _result = result;
                var awaiter = result.ConfigureAwait(false).GetAwaiter();
                if (awaiter.IsCompleted) Complete();
                else awaiter.UnsafeOnCompleted(Complete);
            }

            public ValueTask Task => new ValueTask(this, _source.Version);

            private void Complete()
            {
                try { _result.GetAwaiter().GetResult(); }
                catch (Exception error) { _source.SetException(error); return; }
                _source.SetResult(true);
            }

            public void GetResult(short token) => _source.GetResult(token);
            public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
            public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
                => _source.OnCompleted(continuation, state, token, flags);
        }
    }
}
