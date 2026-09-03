using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Loopback;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class SessionTerminationNotificationRpcTests
{
    [Fact]
    public async Task BoundSessionTerminationNotificationUsesInternalCodec()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var endpointSerializer = new SessionTerminationNoticeRejectingSerializer(new JsonRpcSerializer());
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);

        await using var serverSession = new RpcSession(
            serverTransport,
            endpointSerializer,
            ConnectionId);
        await using var client = new RpcClientRuntime(clientTransport, endpointSerializer);
        var received = new TaskCompletionSource<SessionTerminationNotice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.RegisterRawNotificationHandler(
            GameSessionNotificationRpcIds.ServiceId,
            GameSessionNotificationRpcIds.TerminatedNotificationId,
            payload =>
            {
                received.TrySetResult(LakonaInternalCodec.DecodeSessionTerminationNotice(payload));
                return default;
            });

        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership();
        await using var provider = services.BuildServiceProvider();
        var gameServer = provider.GetRequiredService<ILakonaGameServer>();
        await client.StartAsync(cancellationToken);
        await serverSession.StartAsync(cancellationToken);
        var session = await gameServer.StartSessionAsync(
            "player-a",
            ConnectionId,
            cancellationToken);
        provider.GetRequiredService<GameFrameworkConnectionRegistry>()
            .Set(ConnectionId, new RpcNotificationChannel(serverSession));

        await gameServer.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            "Removed.",
            cancellationToken: cancellationToken);

        var notice = await received.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Assert.Equal(SessionTerminationReason.Policy, notice.Reason);
        Assert.Equal("Removed.", notice.Message);
        Assert.Equal(0, endpointSerializer.CallCount);
    }

    private const string ConnectionId = "termination-notification-connection";

    private sealed class SessionTerminationNoticeRejectingSerializer(IRpcSerializer inner) : IRpcSerializer
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public void Serialize<T>(
            System.Buffers.IBufferWriter<byte> destination,
            T value)
        {
            CountAndReject<T>();
            inner.Serialize(destination, value);
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data)
        {
            CountAndReject<T>();
            return inner.Deserialize<T>(data);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            CountAndReject<T>();
            return inner.Deserialize<T>(data);
        }

        private void CountAndReject<T>()
        {
            Interlocked.Increment(ref _callCount);
            if (typeof(T) == typeof(SessionTerminationNotice))
            {
                throw new InvalidOperationException(
                    "Session termination notices must use LakonaInternalCodec.");
            }
        }
    }
}
