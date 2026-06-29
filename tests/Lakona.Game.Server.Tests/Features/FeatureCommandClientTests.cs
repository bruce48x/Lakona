using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hotfix.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests.Features;

public sealed class FeatureCommandClientTests
{
    [Fact]
    public async Task SendAsyncUsesFeatureCommandIdAsMessageKindAndDeserializesReply()
    {
        var serializer = new TestSerializer();
        var bus = new RecordingFeatureMessageBus(serializer);
        var client = new FeatureCommandClient(bus, serializer);

        var reply = await client.SendAsync<JoinRoomCommand, JoinRoomReply>(
            "battle-runtime",
            new JoinRoomCommand("room-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal("battle-runtime", bus.Feature.Value);
        Assert.Equal("17", bus.Kind);
        Assert.Equal("room-1", bus.Request?.RoomId);
        Assert.Equal("joined", reply.Status);
    }

    [Fact]
    public async Task SendAsyncRequiresFeatureCommandAttribute()
    {
        var serializer = new TestSerializer();
        var client = new FeatureCommandClient(
            new RecordingFeatureMessageBus(serializer),
            serializer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client
            .SendAsync<MissingCommandAttribute, JoinRoomReply>(
                "battle-runtime",
                new MissingCommandAttribute("room-1"),
                TestContext.Current.CancellationToken)
            .AsTask());

        Assert.Contains(nameof(FeatureCommandAttribute), exception.Message);
    }

    private sealed class RecordingFeatureMessageBus : IFeatureMessageBus
    {
        private readonly IFeatureMessageSerializer _serializer;

        public RecordingFeatureMessageBus(IFeatureMessageSerializer serializer)
        {
            _serializer = serializer;
        }

        public FeatureName Feature { get; private set; }

        public string Kind { get; private set; } = "";

        public JoinRoomCommand? Request { get; private set; }

        public ValueTask<FeatureMessageReply> SendToFeatureAsync<TRequest, TReply>(
            FeatureName feature,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<FeatureMessageReply> SendToFeatureAsync<TRequest, TReply>(
            FeatureName feature,
            string kind,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Feature = feature;
            Kind = kind;
            Request = Assert.IsType<JoinRoomCommand>(request);
            var payload = _serializer.Serialize(new JoinRoomReply("joined"));
            return new ValueTask<FeatureMessageReply>(
                new FeatureMessageReply(ClusterSendStatus.Accepted, payload));
        }

        public ValueTask<FeatureMessageReply> SendToNodeAsync<TRequest, TReply>(
            ClusterNodeDescriptor target,
            FeatureName feature,
            string kind,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestSerializer : IFeatureMessageSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            return JsonSerializer.SerializeToUtf8Bytes(value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            return JsonSerializer.Deserialize<T>(payload.Span)!;
        }
    }

    [FeatureCommand(17)]
    private sealed record JoinRoomCommand(string RoomId);

    private sealed record MissingCommandAttribute(string RoomId);

    private sealed record JoinRoomReply(string Status);
}
