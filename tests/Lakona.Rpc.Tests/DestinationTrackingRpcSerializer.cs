using System.Buffers;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;

namespace Lakona.Rpc.Tests;

internal sealed class DestinationTrackingRpcSerializer : IRpcSerializer
{
    private readonly JsonRpcSerializer _inner = new();
    private int _envelopeWriteCount;

    public int EnvelopeWriteCount => Volatile.Read(ref _envelopeWriteCount);

    public void Serialize<T>(IBufferWriter<byte> destination, T value)
    {
        if (destination is RpcEnvelopePayloadWriter)
        {
            Interlocked.Increment(ref _envelopeWriteCount);
        }

        _inner.Serialize(destination, value);
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        return _inner.Deserialize<T>(data);
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data)
    {
        return _inner.Deserialize<T>(data);
    }
}
