using System;

namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Serializer for RPC method payloads (arguments and return values).
    ///     Envelope encoding is handled by <see cref="RpcEnvelopeCodec"/>.
    /// </summary>
    public interface IRpcSerializer
    {
        /// <summary>
        ///     Serializes a DTO value into an owned transport frame.
        /// </summary>
        /// <typeparam name="T">DTO type.</typeparam>
        /// <param name="value">DTO instance to serialize.</param>
        /// <returns>An owned frame containing the serialized payload. The caller disposes it.</returns>
        TransportFrame SerializeFrame<T>(T value);

        /// <summary>
        ///     Deserializes a DTO value from payload bytes.
        /// </summary>
        /// <typeparam name="T">DTO type.</typeparam>
        /// <param name="data">Payload bytes.</param>
        /// <returns>The deserialized DTO value.</returns>
        T Deserialize<T>(ReadOnlySpan<byte> data);

        /// <summary>
        ///     Deserializes a DTO value from payload bytes.
        /// </summary>
        /// <typeparam name="T">DTO type.</typeparam>
        /// <param name="data">Payload bytes.</param>
        /// <returns>The deserialized DTO value.</returns>
        T Deserialize<T>(ReadOnlyMemory<byte> data);
    }
}
