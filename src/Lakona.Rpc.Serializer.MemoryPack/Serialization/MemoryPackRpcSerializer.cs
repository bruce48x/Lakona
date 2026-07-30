using System;
using System.Buffers;
using MemoryPack;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Serializer.MemoryPack
{
    public sealed class MemoryPackRpcSerializer : IRpcSerializer
    {
        private readonly MemoryPackSerializerOptions _options;

        public MemoryPackRpcSerializer()
            : this(MemoryPackSerializerOptions.Default)
        {
        }

        public MemoryPackRpcSerializer(MemoryPackSerializerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Serialize<T>(IBufferWriter<byte> destination, T value)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            MemoryPackSerializer.Serialize(destination, value, _options);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data)
        {
            return MemoryPackSerializer.Deserialize<T>(data, _options)!;
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            return MemoryPackSerializer.Deserialize<T>(data.Span, _options)!;
        }
    }
}
