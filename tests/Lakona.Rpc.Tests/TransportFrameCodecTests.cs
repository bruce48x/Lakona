using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public class TransportFrameCodecTests
{
    private static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(32);

    private static TransportSecurityConfig NoSecurityConfig() => new();

    private static TransportSecurityConfig CompressionOnlyConfig(int threshold = 0) => new()
    {
        EnableCompression = true,
        CompressionThresholdBytes = threshold
    };

    private static TransportSecurityConfig EncryptionOnlyConfig() => new()
    {
        EnableEncryption = true,
        EncryptionKey = GenerateKey()
    };

    private static TransportSecurityConfig FullSecurityConfig(int threshold = 0)
    {
        return new TransportSecurityConfig
        {
            EnableCompression = true,
            CompressionThresholdBytes = threshold,
            EnableEncryption = true,
            EncryptionKey = GenerateKey()
        };
    }

    [Fact]
    public void Decode_NoSecurity_ReturnsRetainedView()
    {
        var codec = new TransportFrameCodec(NoSecurityConfig());
        var original = new byte[] { 1, 2, 3, 4, 5 };
        using var input = TransportFrame.CopyOf(original);
        using var decoded = codec.Decode(input);
        Assert.Equal(original, decoded.ToArray());
        Assert.Equal(input.Span.ToArray(), decoded.Span.ToArray());
    }

    [Fact]
    public void Encode_NoSecurity_ReturnsCopy()
    {
        var codec = new TransportFrameCodec(NoSecurityConfig());
        var original = new byte[] { 1, 2, 3 };

        using var encoded = codec.Encode(original);
        Assert.Equal(original, encoded.ToArray());
    }

    [Fact]
    public void RoundTrip_NoSecurity()
    {
        var codec = new TransportFrameCodec(NoSecurityConfig());
        var data = new byte[] { 10, 20, 30 };

        using var encoded = codec.Encode(data);
        using var decoded = codec.Decode(encoded);

        Assert.Equal(data, decoded.ToArray());
    }

    [Fact]
    public void RoundTrip_CompressionOnly_SmallData_UnderThreshold()
    {
        var codec = new TransportFrameCodec(CompressionOnlyConfig(threshold: 1024));
        var data = new byte[] { 1, 2, 3, 4, 5 };

        using var encoded = codec.Encode(data);
        using var decoded = codec.Decode(encoded);

        Assert.Equal(data, decoded.ToArray());
    }

    [Fact]
    public void RoundTrip_CompressionOnly_LargeData()
    {
        var codec = new TransportFrameCodec(CompressionOnlyConfig(threshold: 0));
        var data = new byte[4096];
        Array.Fill(data, (byte)0xAB);

        using var encoded = codec.Encode(data);
        using var decoded = codec.Decode(encoded);

        Assert.Equal(data, decoded.ToArray());
        Assert.True(encoded.Length < data.Length,
            "Highly compressible data should result in smaller encoded output");
    }

    [Fact]
    public void RoundTrip_EncryptionOnly()
    {
        var config = EncryptionOnlyConfig();
        var codec = new TransportFrameCodec(config);
        var data = new byte[] { 100, 200, 255, 0, 1 };

        using var encoded = codec.Encode(data);
        using var decoded = codec.Decode(encoded);

        Assert.Equal(data, decoded.ToArray());
        Assert.NotEqual(data, encoded.ToArray());
    }

    [Fact]
    public void RoundTrip_FullSecurity()
    {
        var config = FullSecurityConfig(threshold: 0);
        var codec = new TransportFrameCodec(config);
        var data = new byte[2048];
        new Random(42).NextBytes(data);

        using var encoded = codec.Encode(data);
        using var decoded = codec.Decode(encoded);

        Assert.Equal(data, decoded.ToArray());
    }

    [Fact]
    public void Decrypt_TamperedData_ThrowsAuthFailure()
    {
        var config = EncryptionOnlyConfig();
        var codec = new TransportFrameCodec(config);
        var data = new byte[] { 1, 2, 3 };

        using var encoded = codec.Encode(data);
        var tampered = encoded.ToArray();
        tampered[20] ^= 0xFF;

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = TransportFrame.CopyOf(tampered);
            using var _ = codec.Decode(frame);
        });
    }

    [Fact]
    public void Decrypt_TooSmallFrame_Throws()
    {
        var config = EncryptionOnlyConfig();
        var codec = new TransportFrameCodec(config);

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = TransportFrame.CopyOf(new byte[10]);
            using var _ = codec.Decode(frame);
        });
    }

    [Fact]
    public void Constructor_EncryptionEnabledWithoutKey_Throws()
    {
        var config = new TransportSecurityConfig
        {
            EnableEncryption = true
        };

        Assert.Throws<InvalidOperationException>(() => new TransportFrameCodec(config));
    }

    [Fact]
    public void Constructor_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TransportFrameCodec(null!));
    }

    [Fact]
    public void RoundTrip_EmptyPayload()
    {
        var codec = new TransportFrameCodec(NoSecurityConfig());
        var data = Array.Empty<byte>();

        using var encoded = codec.Encode(data);
        using var decoded = codec.Decode(encoded);

        Assert.Empty(decoded.ToArray());
    }

    [Fact]
    public void DifferentKeys_CannotDecrypt()
    {
        var codec1 = new TransportFrameCodec(new TransportSecurityConfig
        {
            EnableEncryption = true,
            EncryptionKey = GenerateKey()
        });
        var codec2 = new TransportFrameCodec(new TransportSecurityConfig
        {
            EnableEncryption = true,
            EncryptionKey = GenerateKey()
        });

        var data = new byte[] { 1, 2, 3, 4 };
        using var encoded = codec1.Encode(data);
        Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = codec2.Decode(encoded);
        });
    }

    [Fact]
    public void Decode_CompressedPayloadExceedingLimit_Throws()
    {
        var config = new TransportSecurityConfig
        {
            EnableCompression = true,
            CompressionThresholdBytes = 0,
            MaxDecompressedFrameBytes = 128
        };

        var codec = new TransportFrameCodec(config);
        var data = new byte[1024];
        Array.Fill(data, (byte)'A');

        using var encoded = codec.Encode(data);
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var _ = codec.Decode(encoded);
        });
    }

    [Fact]
    public void DeriveKey_UsesHkdfSha256()
    {
        var master = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

        var derived = InvokeDeriveKey(master, "enc");
        var expected = ComputeHkdfSha256(master, Encoding.UTF8.GetBytes("enc"), 32);

        Assert.Equal(expected, derived);
    }

    [Fact]
    public void DeriveKey_DifferentPurpose_ProducesDifferentKeys()
    {
        var master = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

        var enc = InvokeDeriveKey(master, "enc");
        var mac = InvokeDeriveKey(master, "mac");

        Assert.NotEqual(enc, mac);
    }

    private static byte[] InvokeDeriveKey(byte[] master, string purpose)
    {
        var method = typeof(TransportFrameCodec).GetMethod(
            "DeriveKey",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (byte[])method.Invoke(null, [master, purpose])!;
    }

    private static byte[] ComputeHkdfSha256(byte[] ikm, byte[] info, int outputLength)
    {
        const int hashLength = 32;
        using var extractHmac = new HMACSHA256(new byte[hashLength]);
        var prk = extractHmac.ComputeHash(ikm);

        using var expandHmac = new HMACSHA256(prk);
        var okm = new byte[outputLength];
        var previous = Array.Empty<byte>();
        var offset = 0;
        var blocks = (outputLength + hashLength - 1) / hashLength;

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

        return okm;
    }
}
