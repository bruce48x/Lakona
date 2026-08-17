using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal static class MembershipBinaryCodec
    {
        public const int MaximumMapEntries = 256;
        public const int MaximumStringBytes = 64 * 1024;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Utf8.GetBytes(value);
            if (bytes.Length > MaximumStringBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Membership strings cannot exceed {MaximumStringBytes} UTF-8 bytes.");
            }

            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        public static string ReadString(BinaryReader reader)
        {
            try
            {
                return Utf8.GetString(ReadBytes(reader, MaximumStringBytes, "string"));
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "Membership string contains malformed UTF-8.",
                    exception);
            }
        }

        public static void WriteGuid(BinaryWriter writer, Guid value)
        {
            writer.Write(value.ToByteArray());
        }

        public static Guid ReadGuid(BinaryReader reader)
        {
            return new Guid(ReadExactly(reader, 16));
        }

        public static void WriteBytes(
            BinaryWriter writer,
            ReadOnlyMemory<byte> bytes)
        {
            WriteBytes(
                writer,
                bytes,
                ClusterMembershipTransportFrame.MaximumPayloadLength,
                "binary payload");
        }

        public static void WriteBytes(
            BinaryWriter writer,
            ReadOnlyMemory<byte> bytes,
            int maximumLength,
            string field)
        {
            if (bytes.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bytes),
                    $"Membership {field} cannot exceed {maximumLength} bytes.");
            }

            writer.Write(bytes.Length);
            writer.Write(bytes.Span);
        }

        public static byte[] ReadBytes(
            BinaryReader reader)
        {
            return ReadBytes(
                reader,
                ClusterMembershipTransportFrame.MaximumPayloadLength,
                "binary payload");
        }

        public static byte[] ReadBytes(
            BinaryReader reader,
            int maximumLength,
            string field)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > maximumLength)
            {
                throw InvalidEncoding($"invalid {field} length");
            }

            return ReadExactly(reader, length);
        }

        public static int ReadCount(BinaryReader reader, int maximum, string field)
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > maximum)
            {
                throw InvalidEncoding($"invalid {field} count");
            }

            return count;
        }

        public static void WriteMap(
            BinaryWriter writer,
            IReadOnlyDictionary<string, string> values,
            bool deterministic)
        {
            if (values.Count > MaximumMapEntries)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(values),
                    $"Membership maps cannot exceed {MaximumMapEntries} entries.");
            }

            writer.Write(values.Count);
            IEnumerable<KeyValuePair<string, string>> entries = deterministic
                ? values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                : values;
            foreach (var pair in entries)
            {
                WriteString(writer, pair.Key);
                WriteString(writer, pair.Value);
            }
        }

        public static Dictionary<string, string> ReadMap(BinaryReader reader)
        {
            var count = ReadCount(reader, MaximumMapEntries, "map");
            var values = new Dictionary<string, string>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                if (!values.TryAdd(ReadString(reader), ReadString(reader)))
                {
                    throw InvalidEncoding("duplicate map key");
                }
            }

            return values;
        }

        public static void EnsureEnd(Stream stream)
        {
            if (stream.Position != stream.Length)
            {
                throw InvalidEncoding("trailing bytes");
            }
        }

        private static byte[] ReadExactly(BinaryReader reader, int length)
        {
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw InvalidEncoding("truncated payload");
            }

            return bytes;
        }

        private static InvalidDataException InvalidEncoding(string reason)
        {
            return new InvalidDataException($"Membership payload has an invalid encoding: {reason}.");
        }
    }
}
