using System;
using System.Buffers;
using System.Text.Json;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Serializer.Json
{
    public sealed class JsonRpcSerializer : IRpcSerializer
    {
        private readonly JsonSerializerOptions _options;

        public JsonRpcSerializer(JsonSerializerOptions? options = null)
        {
            _options = options is null
                ? new JsonSerializerOptions()
                : new JsonSerializerOptions(options);

            _options.IncludeFields = true;
        }

        public void Serialize<T>(IBufferWriter<byte> destination, T value)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            using (var writer = new Utf8JsonWriter(destination))
            {
                JsonSerializer.Serialize(writer, value, _options);
            }
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data)
        {
            return JsonSerializer.Deserialize<T>(data, _options)!;
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            return JsonSerializer.Deserialize<T>(data.Span, _options)!;
        }
    }
}
