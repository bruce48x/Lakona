using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Rpc.Analyzers.Tests;

internal static class NotificationProxyTestHost
{
    public static async Task VerifyAsync(
        Func<RpcNotificationChannel, CancellationToken, ValueTask> invoke, bool acceptsToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stopping = new CancellationTokenSource();
        var transport = new RecordingTransport();
        var channelReady = new TaskCompletionSource<RpcNotificationChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new SingleConnectionAcceptor(transport))
            .ConfigureServices(registry => registry.RegisterRaw(1, 1, (_, channel, _, _) =>
            {
                channelReady.TrySetResult(channel);
                return new ValueTask<RpcRawResult>(RpcRawResult.Ok(ReadOnlyMemory<byte>.Empty));
            })).Build();
        var running = host.RunAsync(stopping.Token).AsTask();
        try
        {
            var channel = await channelReady.Task.WaitAsync(deadline.Token);
            using var caller = new CancellationTokenSource();
            await invoke(channel, caller.Token);
            Assert.Equal(acceptsToken ? caller.Token : CancellationToken.None, transport.LastToken);
            using var frame = TransportFrame.CopyOf(transport.Push!);
            using var push = RpcEnvelopeCodec.DecodePush(frame);
            Assert.Equal(42, push.ServiceId);
            Assert.Equal(2, push.MethodId);
            Assert.Equal("{\"Value\":7}", System.Text.Encoding.UTF8.GetString(push.Payload.Memory.Span));

            caller.Cancel();
            if (acceptsToken)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await invoke(channel, caller.Token));
            else
                await invoke(channel, caller.Token); // No token parameter must keep the default send behavior.

            var expected = new IOException("send failed");
            transport.SendError = expected;
            Assert.Same(expected, await Assert.ThrowsAsync<IOException>(async () =>
                await invoke(channel, CancellationToken.None)));
        }
        finally
        {
            stopping.Cancel();
            await running.WaitAsync(deadline.Token);
        }
    }

    private sealed class SingleConnectionAcceptor(ITransport transport) : IRpcConnectionAcceptor
    {
        private int _accepted;
        public string ListenAddress => "notification-test";
        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _accepted, 1) == 0)
                return new RpcAcceptedConnection(transport, "notification-test");
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unexpected accept completion.");
        }
        public ValueTask DisposeAsync() => default;
    }

    private sealed class RecordingTransport : ITransport
    {
        private int _received;
        public bool IsConnected { get; private set; } = true;
        public CancellationToken LastToken { get; private set; }
        public byte[]? Push { get; private set; }
        public Exception? SendError { get; set; }
        public ValueTask ConnectAsync(CancellationToken ct = default) => default;
        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (RpcEnvelopeCodec.PeekFrameType(frame.Span) == RpcFrameType.Push)
            {
                LastToken = ct;
                ct.ThrowIfCancellationRequested();
                if (SendError is not null) throw SendError;
                Push = frame.ToArray();
            }
            return default;
        }
        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _received, 1) == 0)
                return RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
                {
                    RequestId = 1, ServiceId = 1, MethodId = 1, Payload = ReadOnlyMemory<byte>.Empty
                });
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return TransportFrame.Empty;
        }
        public ValueTask DisposeAsync() { IsConnected = false; return default; }
    }
}
