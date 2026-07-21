using System;
using System.Text;

namespace Lakona.Game.Cluster
{
    public static class NodeAdvertisementLimits
    {
        public const int MaximumAdvertisementsPerMember = 16;

        public const int MaximumKindBytes = 64;

        public const int MaximumFormatBytes = 64;

        public const int MaximumPayloadBytes = 4 * 1024;

        public const int MaximumTotalBytesPerMember = 16 * 1024;
    }

    public sealed class NodeAdvertisement
    {
        private readonly byte[] payload;

        public NodeAdvertisement(
            string kind,
            string format,
            ReadOnlyMemory<byte> payload)
        {
            Kind = ValidateIdentifier(
                kind,
                nameof(kind),
                NodeAdvertisementLimits.MaximumKindBytes);
            Format = ValidateIdentifier(
                format,
                nameof(format),
                NodeAdvertisementLimits.MaximumFormatBytes);
            if (payload.Length > NodeAdvertisementLimits.MaximumPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payload),
                    $"Advertisement payload cannot exceed {NodeAdvertisementLimits.MaximumPayloadBytes} bytes.");
            }

            this.payload = payload.ToArray();
            SerializedSize = Encoding.UTF8.GetByteCount(Kind)
                + Encoding.UTF8.GetByteCount(Format)
                + this.payload.Length;
        }

        public string Kind { get; }

        public string Format { get; }

        public ReadOnlyMemory<byte> Payload => payload;

        internal int SerializedSize { get; }

        private static string ValidateIdentifier(string value, string parameterName, int maximumBytes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Advertisement identifier is required.",
                    parameterName);
            }

            if (Encoding.UTF8.GetByteCount(value) > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    $"Advertisement identifier cannot exceed {maximumBytes} UTF-8 bytes.");
            }

            return value;
        }
    }
}
