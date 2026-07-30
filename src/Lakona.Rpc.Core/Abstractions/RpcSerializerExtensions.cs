using System.Buffers;

namespace Lakona.Rpc.Core;

/// <summary>
/// Convenience helpers for serializer consumers that explicitly need an owned payload frame.
/// </summary>
public static class RpcSerializerExtensions
{
    /// <summary>
    /// Serializes a value into a standalone owned transport frame.
    /// </summary>
    public static TransportFrame SerializeFrame<T>(
        this IRpcSerializer serializer,
        T value)
    {
        if (serializer is null) throw new ArgumentNullException(nameof(serializer));
        using var writer = new PooledFrameBufferWriter();
        serializer.Serialize(writer, value);
        return writer.DetachFrame();
    }
}
