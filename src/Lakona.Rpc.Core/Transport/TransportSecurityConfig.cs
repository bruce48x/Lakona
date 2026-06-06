using System;

namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Frame transformation settings applied above a concrete transport.
    /// </summary>
    /// <remarks>
    ///     This configuration controls optional compression and symmetric frame encryption performed by
    ///     <see cref="TransformingTransport"/>. It is not a TLS replacement and does not authenticate the
    ///     remote peer by itself.
    /// </remarks>
    public sealed class TransportSecurityConfig
    {
        /// <summary>
        ///     Compresses frames before encryption and transmission when enabled.
        /// </summary>
        public bool EnableCompression { get; set; }

        /// <summary>
        ///     Minimum frame size before compression is attempted.
        /// </summary>
        public int CompressionThresholdBytes { get; set; } = 1024;

        /// <summary>
        ///     Maximum allowed decompressed frame size.
        /// </summary>
        public int MaxDecompressedFrameBytes { get; set; } = RpcProtocolLimits.DefaultMaxDecompressedFrameBytes;

        /// <summary>
        ///     Encrypts transformed frames when enabled.
        /// </summary>
        public bool EnableEncryption { get; set; }

        /// <summary>
        ///     Raw symmetric encryption key bytes.
        /// </summary>
        public byte[]? EncryptionKey { get; set; }

        /// <summary>
        ///     Base64-encoded symmetric encryption key. Used when <see cref="EncryptionKey"/> is not set.
        /// </summary>
        public string? EncryptionKeyBase64 { get; set; }

        /// <summary>
        ///     Indicates whether any frame transformation is enabled.
        /// </summary>
        public bool IsEnabled => EnableCompression || EnableEncryption;

        /// <summary>
        ///     Resolves the configured encryption key.
        /// </summary>
        /// <returns>
        ///     <see cref="EncryptionKey"/> when present, otherwise decoded <see cref="EncryptionKeyBase64"/>,
        ///     otherwise <see langword="null"/>.
        /// </returns>
        /// <exception cref="FormatException">
        ///     Thrown when <see cref="EncryptionKeyBase64"/> is not valid base64.
        /// </exception>
        public byte[]? ResolveKey()
        {
            if (EncryptionKey is { Length: > 0 })
                return EncryptionKey;

            if (!string.IsNullOrWhiteSpace(EncryptionKeyBase64))
                return Convert.FromBase64String(EncryptionKeyBase64);

            return null;
        }
    }
}
