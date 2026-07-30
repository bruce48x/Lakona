using System;
using System.Buffers;

namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Serializer for RPC method payloads (arguments and return values).
    ///     Envelope encoding is handled by <see cref="RpcEnvelopeCodec"/>.
    /// </summary>
    public interface IRpcSerializer
    {
        /// <summary>
        ///     Serializes a DTO value into the supplied destination.
        /// </summary>
        /// <typeparam name="T">DTO type.</typeparam>
        /// <param name="destination">Destination owned by the RPC runtime.</param>
        /// <param name="value">DTO instance to serialize.</param>
        void Serialize<T>(IBufferWriter<byte> destination, T value);

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
