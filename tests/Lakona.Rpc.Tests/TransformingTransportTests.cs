using System.Security.Cryptography;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Loopback;

namespace Lakona.Rpc.Tests;

public class TransformingTransportTests
{
    [Fact]
    public async Task RoundTrip_WithCompression()
    {
        LoopbackTransport.CreatePair(out var rawClient, out var rawServer);

        var config = new TransportSecurityConfig
        {
            EnableCompression = true,
            CompressionThresholdBytes = 0
        };

        var client = new TransformingTransport(rawClient, config);
        var server = new TransformingTransport(rawServer, config);

        await client.ConnectAsync();
        await server.ConnectAsync();

        var data = new byte[1024];
        Array.Fill(data, (byte)0x42);

        await client.SendFrameAsync(data);
        var received = await server.ReceiveFrameAsync();

        Assert.Equal(data, received.ToArray());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task RoundTrip_WithEncryption()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var config = new TransportSecurityConfig
        {
            EnableEncryption = true,
            EncryptionKey = key
        };

        LoopbackTransport.CreatePair(out var rawClient, out var rawServer);
        var client = new TransformingTransport(rawClient, config);
        var server = new TransformingTransport(rawServer, config);

        await client.ConnectAsync();
        await server.ConnectAsync();

        var data = new byte[] { 10, 20, 30, 40, 50 };
        await client.SendFrameAsync(data);
        var received = await server.ReceiveFrameAsync();

        Assert.Equal(data, received.ToArray());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task RoundTrip_NoSecurity_DataPreserved()
    {
        var config = new TransportSecurityConfig();

        LoopbackTransport.CreatePair(out var rawClient, out var rawServer);
        var client = new TransformingTransport(rawClient, config);
        var server = new TransformingTransport(rawServer, config);

        await client.ConnectAsync();
        await server.ConnectAsync();

        var data = new byte[] { 0xFF, 0x00, 0xAB };
        await client.SendFrameAsync(data);
        var received = await server.ReceiveFrameAsync();

        Assert.Equal(data, received.ToArray());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task IsConnected_DelegatesToInner()
    {
        LoopbackTransport.CreatePair(out var rawClient, out var rawServer);
        var config = new TransportSecurityConfig();
        var wrapped = new TransformingTransport(rawClient, config);

        Assert.False(wrapped.IsConnected);
        await wrapped.ConnectAsync();
        Assert.True(wrapped.IsConnected);

        await wrapped.DisposeAsync();
        await rawServer.DisposeAsync();
    }

    [Fact]
    public async Task EmptyFrame_PassedThrough()
    {
        var config = new TransportSecurityConfig();
        LoopbackTransport.CreatePair(out var rawClient, out var rawServer);

        await rawClient.ConnectAsync();
        await rawServer.ConnectAsync();

        var client = new TransformingTransport(rawClient, config);
        var server = new TransformingTransport(rawServer, config);

        await rawClient.DisposeAsync();

        var received = await server.ReceiveFrameAsync();
        Assert.True(received.IsEmpty);

        await server.DisposeAsync();
    }

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TransformingTransport(null!, new TransportSecurityConfig()));
    }

    [Fact]
    public void Constructor_NullConfig_Throws()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        Assert.Throws<ArgumentNullException>(() =>
            new TransformingTransport(client, null!));
    }
}
