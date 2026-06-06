using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Lakona.Rpc.Core
{
    public sealed class TransportFrameCodec
    {
        private const byte FlagCompressed = 1;
        private const int IvSize = 16;
        private const int HmacSize = 32;
        private const int CopyBufferSize = 8192;

        private readonly bool _compressEnabled;
        private readonly int _compressThreshold;
        private readonly int _maxDecompressedFrameBytes;
        private readonly bool _encryptEnabled;
        private readonly byte[]? _encKey;
        private readonly byte[]? _macKey;

        public TransportFrameCodec(TransportSecurityConfig config)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));

            _compressEnabled = config.EnableCompression;
            _compressThreshold = Math.Max(0, config.CompressionThresholdBytes);
            _maxDecompressedFrameBytes = config.MaxDecompressedFrameBytes > 0
                ? config.MaxDecompressedFrameBytes
                : throw new ArgumentOutOfRangeException(nameof(config), "MaxDecompressedFrameBytes must be positive.");
            _encryptEnabled = config.EnableEncryption;

            if (_encryptEnabled)
            {
                var master = config.ResolveKey();
                if (master is null || master.Length == 0)
                    throw new InvalidOperationException("Encryption enabled but no key provided.");

                _encKey = DeriveKey(master, "enc");
                _macKey = DeriveKey(master, "mac");
            }
        }

        public bool IsPassthrough => !_compressEnabled && !_encryptEnabled;

        public TransportFrame Encode(ReadOnlySpan<byte> frame)
        {
            if (IsPassthrough)
                return TransportFrame.CopyOf(frame);

            if (_compressEnabled && frame.Length >= _compressThreshold)
            {
                using var compressed = CompressToFrame(frame);
                if (compressed.Length < frame.Length)
                    return _encryptEnabled
                        ? Encrypt(FlagCompressed, compressed.Memory)
                        : PrependFlags(FlagCompressed, compressed.Memory.Span);
            }

            if (_encryptEnabled)
                return Encrypt(0, frame);

            return PrependFlags(0, frame);
        }

        public TransportFrame Decode(TransportFrame frame)
        {
            if (IsPassthrough)
                return frame.Slice(0, frame.Length);

            if (_encryptEnabled)
            {
                using var decrypted = DecryptFrame(frame.Memory);
                return DecodeDecodedPayload(decrypted);
            }

            return DecodeDecodedPayload(frame);
        }

        private TransportFrame DecodeDecodedPayload(TransportFrame payloadFrame)
        {
            if (payloadFrame.Length < 1)
                throw new InvalidOperationException("Security header missing.");

            var flags = payloadFrame.Span[0];
            if (_compressEnabled && (flags & FlagCompressed) != 0)
                return DecompressToFrame(payloadFrame.Memory.Slice(1), _maxDecompressedFrameBytes);

            return payloadFrame.Slice(1, payloadFrame.Length - 1);
        }

        private static TransportFrame CompressToFrame(ReadOnlySpan<byte> data)
        {
            using var output = new PooledBufferStream(data.Length);
            using (var gz = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                gz.Write(data);
            }
            return output.DetachFrame();
        }

        private static TransportFrame DecompressToFrame(ReadOnlyMemory<byte> data, int maxOutputBytes)
        {
            using var input = new ReadOnlyMemoryStream(data);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var output = new PooledBufferStream();
            var buffer = new byte[CopyBufferSize];
            var total = 0;

            while (true)
            {
                var read = gz.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;

                total += read;
                if (total > maxOutputBytes)
                    throw new InvalidOperationException(
                        $"Decompressed frame exceeds configured limit of {maxOutputBytes} bytes.");

                output.Write(buffer, 0, read);
            }

            return output.DetachFrame();
        }

        private TransportFrame Encrypt(byte flags, ReadOnlySpan<byte> payload)
        {
            using var plaintext = TransportFrame.Allocate(1 + payload.Length);
            var span = plaintext.GetWritableSpan();
            span[0] = flags;
            payload.CopyTo(span.Slice(1));
            return EncryptPlaintext(plaintext.Memory);
        }

        private TransportFrame Encrypt(byte flags, ReadOnlyMemory<byte> payload)
        {
            using var plaintext = TransportFrame.Allocate(1 + payload.Length);
            var span = plaintext.GetWritableSpan();
            span[0] = flags;
            payload.Span.CopyTo(span.Slice(1));
            return EncryptPlaintext(plaintext.Memory);
        }

        private TransportFrame EncryptPlaintext(ReadOnlyMemory<byte> plaintext)
        {
            var iv = new byte[IvSize];
            RandomNumberGenerator.Fill(iv);
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _encKey!;
            aes.IV = iv;

            using var encryptor = aes.CreateEncryptor();
            using var source = new ReadOnlyMemoryStream(plaintext);
            using var output = new PooledBufferStream(plaintext.Length + 32);
            using (var crypto = new CryptoStream(output, encryptor, CryptoStreamMode.Write, leaveOpen: true))
            {
                source.CopyTo(crypto);
                crypto.FlushFinalBlock();
            }

            using var ciphertext = output.DetachFrame();
            var tag = ComputeHmac(iv, ciphertext.Memory);

            var encrypted = TransportFrame.Allocate(iv.Length + ciphertext.Length + tag.Length);
            var span = encrypted.GetWritableSpan();
            iv.CopyTo(span);
            ciphertext.Span.CopyTo(span.Slice(iv.Length));
            tag.CopyTo(span.Slice(iv.Length + ciphertext.Length));
            return encrypted;
        }

        private TransportFrame DecryptFrame(ReadOnlyMemory<byte> data)
        {
            if (data.Length < IvSize + HmacSize)
                throw new InvalidOperationException("Encrypted frame too small.");

            var iv = data.Slice(0, IvSize).ToArray();
            var ciphertext = data.Slice(IvSize, data.Length - IvSize - HmacSize);
            var tag = data.Span.Slice(data.Length - HmacSize, HmacSize);

            var expected = ComputeHmac(iv, ciphertext);
            if (!CryptographicOperations.FixedTimeEquals(expected, tag))
                throw new InvalidOperationException("Encrypted frame authentication failed.");

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _encKey!;
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            using var source = new ReadOnlyMemoryStream(ciphertext);
            using var crypto = new CryptoStream(source, decryptor, CryptoStreamMode.Read);
            using var output = new PooledBufferStream(ciphertext.Length);
            crypto.CopyTo(output);
            return output.DetachFrame();
        }

        private byte[] ComputeHmac(byte[] iv, ReadOnlyMemory<byte> ciphertext)
        {
            using var hmac = new HMACSHA256(_macKey!);
            hmac.TransformBlock(iv, 0, iv.Length, null, 0);
            if (MemoryMarshal.TryGetArray(ciphertext, out ArraySegment<byte> segment))
            {
                hmac.TransformFinalBlock(segment.Array!, segment.Offset, segment.Count);
            }
            else
            {
                var copy = ciphertext.ToArray();
                hmac.TransformFinalBlock(copy, 0, copy.Length);
            }
            return hmac.Hash!;
        }

        private static TransportFrame PrependFlags(byte flags, ReadOnlySpan<byte> payload)
        {
            var frame = TransportFrame.Allocate(1 + payload.Length);
            var span = frame.GetWritableSpan();
            span[0] = flags;
            payload.CopyTo(span.Slice(1));
            return frame;
        }

        private static byte[] DeriveKey(byte[] master, string purpose)
        {
            var info = Encoding.UTF8.GetBytes(purpose);
            return HkdfSha256(master, info, HmacSize);
        }

        private static byte[] HkdfSha256(byte[] ikm, byte[] info, int outputLength)
        {
            if (outputLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(outputLength));

            var hashLength = HmacSize;
            var blocks = (outputLength + hashLength - 1) / hashLength;
            if (blocks > 255)
                throw new ArgumentOutOfRangeException(nameof(outputLength), "HKDF output too long.");

            using var extractHmac = new HMACSHA256(new byte[hashLength]);
            var prk = extractHmac.ComputeHash(ikm);

            using var expandHmac = new HMACSHA256(prk);
            var okm = new byte[outputLength];
            var previous = Array.Empty<byte>();
            var offset = 0;

            for (byte blockIndex = 1; blockIndex <= blocks; blockIndex++)
            {
                var input = new byte[previous.Length + info.Length + 1];
                Buffer.BlockCopy(previous, 0, input, 0, previous.Length);
                Buffer.BlockCopy(info, 0, input, previous.Length, info.Length);
                input[^1] = blockIndex;

                previous = expandHmac.ComputeHash(input);
                var bytesToCopy = Math.Min(previous.Length, outputLength - offset);
                Buffer.BlockCopy(previous, 0, okm, offset, bytesToCopy);
                offset += bytesToCopy;
            }

            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(previous);
            return okm;
        }
    }
}
