using System.Buffers;
using System.Threading.Channels;
using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Client;
using Lakona.Rpc.Core;

namespace Lakona.Tests;

public sealed class TestNumber
{
    public int Value { get; set; }
}

// A protocol peer with explicit barriers, shared by runtime and generated-client tests.
internal sealed class GameClientTestTransport : ITransport
{
    private readonly Channel<TransportFrame> _incoming = Channel.CreateUnbounded<TransportFrame>();
    public TaskCompletionSource ConnectEntered { get; } = Signal();
    public TaskCompletionSource HandshakeEntered { get; } = Signal();
    public TaskCompletionSource HeartbeatEntered { get; } = Signal();
    public TaskCompletionSource EstablishedAcknowledged { get; } = Signal();
    public TaskCompletionSource Disposed { get; } = Signal();
    public TaskCompletionSource? ConnectRelease { get; init; }
    public TaskCompletionSource? HandshakeRelease { get; init; }
    public TaskCompletionSource? HeartbeatRelease { get; init; }
    public TaskCompletionSource? DisposeRelease { get; init; }
    public Exception? ConnectError { get; init; }
    public GameSessionRecoveryStatus RecoveryStatus { get; init; } = GameSessionRecoveryStatus.Resumed;
    public bool IsConnected { get; private set; }
    public int DisposeCount { get; private set; }
    public GameClientHello? Hello { get; private set; }
    public int ProtocolVersion { get; init; } = 1;

    public static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        ConnectEntered.TrySetResult();
        if (ConnectRelease is not null) await ConnectRelease.Task.WaitAsync(ct);
        if (ConnectError is not null) throw ConnectError;
        IsConnected = true;
    }

    public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        using var owned = TransportFrame.CopyOf(frame.Span);
        using var request = RpcEnvelopeCodec.DecodeRequest(owned);
        byte[] payload;
        if (request.ServiceId == GameHandshakeRpcIds.ServiceId && request.MethodId == GameHandshakeRpcIds.HandshakeMethodId)
        {
            Hello = LakonaInternalCodec.DecodeGameClientHello(request.Payload.Memory);
            HandshakeEntered.TrySetResult();
            if (HandshakeRelease is not null) await HandshakeRelease.Task.WaitAsync(ct);
            payload = LakonaInternalCodec.EncodeGameServerHello(new GameServerHello
            {
                SelectedProtocolVersion = ProtocolVersion,
                Heartbeat = new GameHeartbeatHandshakeSettings
                {
                    Interval = TimeSpan.FromHours(1), Timeout = TimeSpan.FromHours(2)
                },
                Recovery = new GameSessionRecoveryHandshakeResult { Status = RecoveryStatus }
            });
        }
        else if (request.ServiceId == GameHeartbeatRpcIds.ServiceId && request.MethodId == GameHeartbeatRpcIds.HeartbeatMethodId)
        {
            HeartbeatEntered.TrySetResult();
            if (HeartbeatRelease is not null) await HeartbeatRelease.Task.WaitAsync(ct);
            payload = LakonaInternalCodec.EncodeGameHeartbeatReply(new GameHeartbeatReply { Status = GameHeartbeatStatus.Ok });
        }
        else if (request.ServiceId == GameSessionEstablishedRpcIds.ServiceId && request.MethodId == GameSessionEstablishedRpcIds.AckMethodId)
        {
            EstablishedAcknowledged.TrySetResult();
            payload = Array.Empty<byte>();
        }
        else
        {
            payload = request.Payload.ToArray();
        }
        _incoming.Writer.TryWrite(RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload));
    }

    public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default) => _incoming.Reader.ReadAsync(ct);

    public void EstablishSession()
    {
        Push(GameSessionNotificationRpcIds.ServiceId, GameSessionNotificationRpcIds.EstablishedNotificationId,
            LakonaInternalCodec.EncodeGameSessionEstablished(new GameSessionEstablished
            {
                SessionId = "session", ResumeTicket = "ticket"
            }));
    }

    public void Push(int serviceId, int methodId, byte[] payload)
    {
        _incoming.Writer.TryWrite(RpcEnvelopeCodec.EncodePush(new RpcPushEnvelope
        {
            ServiceId = serviceId, MethodId = methodId, Payload = payload
        }));
    }

    public void Disconnect() => _incoming.Writer.TryComplete(new IOException("Connection lost."));

    public async ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsConnected = false;
        Disposed.TrySetResult();
        if (DisposeRelease is not null) await DisposeRelease.Task;
        _incoming.Writer.TryComplete();
        while (_incoming.Reader.TryRead(out var frame)) frame.Dispose();
    }
}

internal sealed class IntegerSerializer : IRpcSerializer
{
    public void Serialize<T>(IBufferWriter<byte> destination, T value)
    {
        var number = value is TestNumber dto ? dto.Value : (int)(object)value!;
        var bytes = BitConverter.GetBytes(number);
        bytes.CopyTo(destination.GetSpan(bytes.Length));
        destination.Advance(bytes.Length);
    }
    public T Deserialize<T>(ReadOnlySpan<byte> data)
    {
        var number = BitConverter.ToInt32(data);
        return typeof(T) == typeof(TestNumber) ? (T)(object)new TestNumber { Value = number } : (T)(object)number;
    }
    public T Deserialize<T>(ReadOnlyMemory<byte> data) => Deserialize<T>(data.Span);
}

internal sealed class ControlledRecoveryScheduler : IGameSessionRecoveryScheduler
{
    private readonly SemaphoreSlim _steps = new(0);
    public TaskCompletionSource Waiting { get; } = GameClientTestTransport.Signal();
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    public DateTimeOffset GetUtcNow() => Now;
    public TimeSpan GetDelay(int attempt) => TimeSpan.FromMilliseconds(1);
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        Waiting.TrySetResult();
        await _steps.WaitAsync(cancellationToken);
    }
    public void Step() => _steps.Release();
}
