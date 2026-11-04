using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lakona.Game.Abstractions;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Testing;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Sessions;

public sealed class EndpointEncodedNotificationTests
{
    private const int ServiceId = 731;
    private const int MethodId = 1;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Endpoint_encoding_preserves_the_business_payload_through_delivery_and_replay(
        bool remote, bool reliable, bool customJson)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var ct = deadline.Token;
        var profile = customJson ? "custom-json" : "memorypack-fields";
        var ownerSerializer = new CountingSerializer(CreateSerializer(customJson));
        var remoteSerializer = new CountingSerializer(CreateSerializer(customJson));
        var receiver = CreateSerializer(customJson);
        var builder = new LakonaTestClusterBuilder().AddNode("owner");
        if (remote)
            builder.AddNode("publisher");
        await using var cluster = builder.ConfigureNodes(node => node.ConfigureServices((services, _) =>
            services.AddLakonaEndpointSerializer(profile, () =>
                node.NodeId == "owner" ? ownerSerializer : remoteSerializer))).Build();
        await cluster.StartAsync(ct);

        var owner = cluster.Node("owner").Services;
        var sessions = owner.GetRequiredService<IGameSessionRegistry>();
        var key = await sessions.StartNewSessionAsync("player", ct);
        const string connectionId = "player-connection";
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        await using var client = clientTransport;
        await using var server = TestRpcSession.Create(serverTransport, ownerSerializer, ownsTransport: true, connectionId: connectionId);
        await client.ConnectAsync(ct);
        await server.StartAsync(ct);
        owner.GetRequiredService<GameFrameworkConnectionRegistry>().Set(connectionId, new RpcNotificationChannel(server));
        owner.GetRequiredService<GameSessionCallbackProxyRegistry>().Add(new ProbeBinder());
        await sessions.PrepareProfiledSessionBindingAsync(key, connectionId, profile, ct);
        await sessions.CommitSessionBindingAsync(key, connectionId, ct);
        await sessions.SetReliablePushPolicyAsync(key, reliable, ct);

        var publisher = remote ? cluster.Node("publisher").Services : owner;
        var router = Assert.IsType<ClientNotificationCommandRouter>(publisher.GetRequiredService<IClientNotificationCommandRouter>());
        var payload = new EndpointProbePayload { Value = 73 };
        Assert.Equal(ClientNotificationStatus.Accepted, router.EnqueueGenerated<IEndpointProbeNotifications, EndpointProbePayload>(
            key, ServiceId, MethodId, nameof(IEndpointProbeNotifications.OnValue), payload));
        await router.WaitForIdleAsync(key, ct);
        var first = await ReceiveAsync();
        if (reliable)
        {
            payload.Value = -1;
            await owner.GetRequiredService<IReliablePushRuntime>().ReplayPendingAsync(key, ct);
            Assert.Equal(first, await ReceiveAsync());
        }

        Assert.Equal(remote ? 0 : 1, ownerSerializer.Writes);
        Assert.Equal(remote ? 1 : 0, remoteSerializer.Writes);

        async Task<byte[]> ReceiveAsync()
        {
            using var frame = await client.ReceiveFrameAsync(ct);
            using var push = RpcEnvelopeCodec.DecodePush(frame);
            Assert.Equal(ServiceId, push.ServiceId);
            Assert.Equal(MethodId, push.MethodId);
            Assert.Equal(73, receiver.Deserialize<EndpointProbePayload>(push.Payload.Memory).Value);
            if (reliable)
            {
                var metadata = Assert.IsType<RpcPushMetadata>(push.Metadata);
                Assert.Equal(LakonaInternalCodec.ReliablePushMetadataType, metadata.Type);
                var notice = LakonaInternalCodec.DecodeReliablePushMetadata(metadata.Payload);
                Assert.Equal(key.SessionId, notice.SessionId);
                Assert.Equal(1L, notice.Sequence.Value);
            }
            else
            {
                Assert.Null(push.Metadata);
            }
            return push.Payload.ToArray();
        }
    }

    private static IRpcSerializer CreateSerializer(bool customJson) => customJson
        ? new JsonRpcSerializer(new JsonSerializerOptions { Converters = { new ProbeConverter() } })
        : new MemoryPackRpcSerializer();

    private sealed class ProbeBinder : LakonaRpcServiceBinder
    {
        public override void Bind(LakonaGameServerRpcContext context) { }

        public override bool TryCreateCallback(Type callbackContractType, RpcNotificationChannel notifications, out object? callback)
        {
            callback = callbackContractType == typeof(IEndpointProbeNotifications)
                ? new global::Server.Generated.EndpointProbeNotificationsProxy(notifications) : null;
            return callback is not null;
        }
    }

    private sealed class CountingSerializer(IRpcSerializer inner) : IRpcSerializer
    {
        private int _writes;
        public int Writes => Volatile.Read(ref _writes);
        public void Serialize<T>(IBufferWriter<byte> destination, T value)
        {
            if (typeof(T) == typeof(EndpointProbePayload))
                Interlocked.Increment(ref _writes);
            inner.Serialize(destination, value);
        }
        public T Deserialize<T>(ReadOnlySpan<byte> data) => inner.Deserialize<T>(data);
        public T Deserialize<T>(ReadOnlyMemory<byte> data) => inner.Deserialize<T>(data);
    }

    private sealed class ProbeConverter : JsonConverter<EndpointProbePayload>
    {
        public override EndpointProbePayload Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            new() { Value = reader.GetInt32() - 1000 };

        public override void Write(Utf8JsonWriter writer, EndpointProbePayload payload, JsonSerializerOptions options) =>
            writer.WriteNumberValue(payload.Value + 1000);
    }
}

[MemoryPackable]
public partial class EndpointProbePayload
{
    public int Value;
}

[RpcService(731, NotificationContract = typeof(IEndpointProbeNotifications))]
public interface IEndpointProbeService
{
    [RpcMethod(1)]
    ValueTask<int> PingAsync(int value);
}

[RpcNotificationContract]
public interface IEndpointProbeNotifications
{
    [RpcNotification(1)]
    void OnValue(EndpointProbePayload payload);
}
