using System.Linq;
using System.Text;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tests;

public class TransportSecurityTests
{
    [Fact]
    public void CompressionOnly_Roundtrip()
    {
        var cfg = new TransportSecurityConfig
        {
            EnableCompression = true,
            CompressionThresholdBytes = 0
        };

        var codec = new TransportFrameCodec(cfg);
        var input = Enumerable.Repeat((byte)0x5A, 4096).ToArray();

        var encoded = codec.Encode(input);
        var decoded = codec.Decode(encoded);

        Assert.Equal(input, decoded.ToArray());
    }

    [Fact]
    public void EncryptionOnly_Roundtrip()
    {
        var cfg = new TransportSecurityConfig
        {
            EnableEncryption = true,
            EncryptionKey = BuildKey()
        };

        var codec = new TransportFrameCodec(cfg);
        var input = BuildPayload();

        var encoded = codec.Encode(input);
        var decoded = codec.Decode(encoded);

        Assert.Equal(input, decoded.ToArray());
    }

    [Fact]
    public void CompressionAndEncryption_Roundtrip()
    {
        var cfg = new TransportSecurityConfig
        {
            EnableCompression = true,
            CompressionThresholdBytes = 0,
            EnableEncryption = true,
            EncryptionKey = BuildKey()
        };

        var codec = new TransportFrameCodec(cfg);
        var input = Enumerable.Repeat((byte)0xAB, 8192).ToArray();

        var encoded = codec.Encode(input);
        var decoded = codec.Decode(encoded);

        Assert.Equal(input, decoded.ToArray());
    }

    private static byte[] BuildKey()
    {
        return Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    }

    private static byte[] BuildPayload()
    {
        return Encoding.UTF8.GetBytes("hello-secure-transport");
    }
}
