using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public class TransportSecurityConfigTests
{
    [Fact]
    public void Properties_DefaultValues_AreCorrect()
    {
        var config = new TransportSecurityConfig();

        Assert.False(config.EnableCompression);
        Assert.Equal(1024, config.CompressionThresholdBytes);
        Assert.False(config.EnableEncryption);
        Assert.Null(config.EncryptionKey);
        Assert.Null(config.EncryptionKeyBase64);
        Assert.False(config.IsEnabled);
    }

    [Fact]
    public void Properties_SetAndGet_RoundTrip()
    {
        var key = new byte[] { 1, 2, 3 };
        var config = new TransportSecurityConfig
        {
            EnableCompression = true,
            CompressionThresholdBytes = 512,
            EnableEncryption = true,
            EncryptionKey = key,
            EncryptionKeyBase64 = "AQID"
        };

        Assert.True(config.EnableCompression);
        Assert.Equal(512, config.CompressionThresholdBytes);
        Assert.True(config.EnableEncryption);
        Assert.Same(key, config.EncryptionKey);
        Assert.Equal("AQID", config.EncryptionKeyBase64);
        Assert.True(config.IsEnabled);
    }

    [Fact]
    public void IsEnabled_TrueWhenOnlyCompression()
    {
        var config = new TransportSecurityConfig { EnableCompression = true };
        Assert.True(config.IsEnabled);
    }

    [Fact]
    public void IsEnabled_TrueWhenOnlyEncryption()
    {
        var config = new TransportSecurityConfig { EnableEncryption = true };
        Assert.True(config.IsEnabled);
    }

    [Fact]
    public void ResolveKey_PrefersEncryptionKeyOverBase64()
    {
        var directKey = new byte[] { 10, 20, 30 };
        var config = new TransportSecurityConfig
        {
            EncryptionKey = directKey,
            EncryptionKeyBase64 = Convert.ToBase64String(new byte[] { 40, 50 })
        };

        var resolved = config.ResolveKey();
        Assert.Same(directKey, resolved);
    }

    [Fact]
    public void ResolveKey_FallsBackToBase64()
    {
        var expected = new byte[] { 40, 50, 60 };
        var config = new TransportSecurityConfig
        {
            EncryptionKeyBase64 = Convert.ToBase64String(expected)
        };

        var resolved = config.ResolveKey();
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolveKey_ReturnsNullWhenNoKeySet()
    {
        var config = new TransportSecurityConfig();
        Assert.Null(config.ResolveKey());
    }

    [Fact]
    public void ResolveKey_IgnoresEmptyByteArray()
    {
        var config = new TransportSecurityConfig
        {
            EncryptionKey = Array.Empty<byte>()
        };
        Assert.Null(config.ResolveKey());
    }
}
