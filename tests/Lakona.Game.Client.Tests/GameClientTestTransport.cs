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
    private int _businessRequestCount;
    public TaskCompletionSource ConnectEntered { get; } = Signal();
    public TaskCompletionSource ConnectCanceled { get; } = Signal();
    public TaskCompletionSource HandshakeEntered { get; } = Signal();
    public TaskCompletionSource HeartbeatEntered { get; } = Signal();
    public TaskCompletionSource HeartbeatSuppressed { get; } = Signal();
    public Channel<GameHeartbeatStatus> HeartbeatReplies { get; } = Channel.CreateUnbounded<GameHeartbeatStatus>();
    public TaskCompletionSource BusinessEntered { get; } = Signal();
    public TaskCompletionSource EstablishedAcknowledged { get; } = Signal();
    public TaskCompletionSource Disposed { get; } = Signal();
    public TaskCompletionSource? ConnectRelease { get; init; }
    public TaskCompletionSource? HandshakeRelease { get; init; }
    public TaskCompletionSource? HeartbeatRelease { get; init; }
    public TaskCompletionSource? DisposeRelease { get; init; }
    public Exception? ConnectError { get; init; }
    public Exception? DisposeError { get; init; }
    public bool IgnoreConnectCancellation { get; init; }
    public bool SuppressHeartbeatReplies { get; set; }
    public bool SuppressBusinessReplies { get; init; }
    public bool DisconnectOnHeartbeatCancellation { get; init; }
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromHours(2);
    public TimeSpan ResumeWindow { get; init; } = TimeSpan.FromSeconds(60);
    public GameHeartbeatStatus HeartbeatReplyStatus { get; set; } = GameHeartbeatStatus.Ok;
    public int BusinessRequestCount => Volatile.Read(ref _businessRequestCount);
    public CancellationToken ReceiveCancellation { get; private set; }
    public GameSessionRecoveryStatus RecoveryStatus { get; init; } = GameSessionRecoveryStatus.Resumed;
    public bool IsConnected { get; private set; }
    public int DisposeCount { get; private set; }
    public GameClientHello? Hello { get; private set; }
    public int ProtocolVersion { get; init; } = 1;

    public static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        using var canceled = ct.Register(() => ConnectCanceled.TrySetResult());
        ConnectEntered.TrySetResult();
        if (ConnectRelease is not null)
            await (IgnoreConnectCancellation ? ConnectRelease.Task : ConnectRelease.Task.WaitAsync(ct));
        if (ConnectError is not null) throw ConnectError;
        IsConnected = true;
    }

    public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        using var owned = TransportFrame.CopyOf(frame.Span);
        using var request = RpcEnvelopeCodec.DecodeRequest(owned);
        byte[] payload;
        GameHeartbeatStatus? heartbeatStatus = null;
        if (request.ServiceId == GameHandshakeRpcIds.ServiceId && request.MethodId == GameHandshakeRpcIds.HandshakeMethodId)
        {
            Hello = LakonaInternalCodec.DecodeGameClientHello(request.Payload.Memory);
            HandshakeEntered.TrySetResult();
            if (HandshakeRelease is not null) await HandshakeRelease.Task.WaitAsync(ct);
            payload = LakonaInternalCodec.EncodeGameServerHello(new GameServerHello
            {
                SelectedProtocolVersion = ProtocolVersion,
                SessionResume = new GameSessionResumeHandshakeSettings { Window = ResumeWindow },
                Heartbeat = new GameHeartbeatHandshakeSettings
                {
                    Interval = HeartbeatInterval, Timeout = HeartbeatTimeout
                },
                Recovery = new GameSessionRecoveryHandshakeResult { Status = RecoveryStatus }
            });
        }
        else if (request.ServiceId == GameHeartbeatRpcIds.ServiceId && request.MethodId == GameHeartbeatRpcIds.HeartbeatMethodId)
        {
            HeartbeatEntered.TrySetResult();
            if (SuppressHeartbeatReplies)
            {
                HeartbeatSuppressed.TrySetResult();
                if (DisconnectOnHeartbeatCancellation)
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        Disconnect();
                        throw;
                    }
                }
                return;
            }
            if (HeartbeatRelease is not null) await HeartbeatRelease.Task.WaitAsync(ct);
            heartbeatStatus = HeartbeatReplyStatus;
            payload = LakonaInternalCodec.EncodeGameHeartbeatReply(new GameHeartbeatReply { Status = heartbeatStatus.Value });
        }
        else if (request.ServiceId == GameSessionEstablishedRpcIds.ServiceId && request.MethodId == GameSessionEstablishedRpcIds.AckMethodId)
        {
            EstablishedAcknowledged.TrySetResult();
            payload = Array.Empty<byte>();
        }
        else
        {
            Interlocked.Increment(ref _businessRequestCount);
            BusinessEntered.TrySetResult();
            if (SuppressBusinessReplies) return;
            payload = request.Payload.ToArray();
        }
        _incoming.Writer.TryWrite(RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload));
        if (heartbeatStatus.HasValue) HeartbeatReplies.Writer.TryWrite(heartbeatStatus.Value);
    }

    public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
    {
        // Expose the test peer's receive cancellation as a deterministic runtime-stop barrier.
        if (!ReceiveCancellation.CanBeCanceled) ReceiveCancellation = ct;
        return _incoming.Reader.ReadAsync(ct);
    }

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
        if (DisposeError is not null) throw DisposeError;
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
    private readonly VirtualRecoveryScheduler _clock = new();
    private int _delayCount;
    public TaskCompletionSource Waiting { get; } = GameClientTestTransport.Signal();
    public Channel<int> DelayEntries { get; } = Channel.CreateUnbounded<int>();
    public Channel<TimeSpan> DeadlineEntries => _clock.Delays;
    public int DelayCount => Volatile.Read(ref _delayCount);
    public DateTimeOffset Now { get => _clock.GetUtcNow(); set => _clock.AdvanceTo(value); }
    public DateTimeOffset GetUtcNow() => Now;
    public TimeSpan GetDelay(int attempt) => TimeSpan.FromMilliseconds(1);
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        // Existing tests step only the 1 ms retry; deadline waits follow the virtual clock.
        if (delay > TimeSpan.FromMilliseconds(1))
        {
            await _clock.DelayAsync(delay, cancellationToken);
            return;
        }
        DelayEntries.Writer.TryWrite(Interlocked.Increment(ref _delayCount));
        Waiting.TrySetResult();
        await _steps.WaitAsync(cancellationToken);
    }
    public void Step() => _steps.Release();
}

internal sealed class VirtualRecoveryScheduler : IGameSessionRecoveryScheduler
{
    private readonly object _gate = new();
    private readonly List<(DateTimeOffset Due, TaskCompletionSource Completion)> _delays = [];
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public Channel<TimeSpan> Delays { get; } = Channel.CreateUnbounded<TimeSpan>();
    public Channel<TimeSpan> CanceledDelays { get; } = Channel.CreateUnbounded<TimeSpan>();

    public DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public TimeSpan GetDelay(int attempt) => RetryDelay;

    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (delay <= TimeSpan.Zero) return;
        var completion = GameClientTestTransport.Signal();
        lock (_gate) _delays.Add((_now + delay, completion));
        Delays.Writer.TryWrite(delay);
        try
        {
            await completion.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CanceledDelays.Writer.TryWrite(delay);
            throw;
        }
        finally
        {
            lock (_gate) _delays.RemoveAll(item => ReferenceEquals(item.Completion, completion));
        }
    }

    public void Advance(TimeSpan elapsed) => AdvanceTo(GetUtcNow() + elapsed);

    public void AdvanceTo(DateTimeOffset now)
    {
        TaskCompletionSource[] ready;
        lock (_gate)
        {
            if (now < _now) throw new ArgumentOutOfRangeException(nameof(now));
            _now = now;
            ready = _delays.Where(item => item.Due <= now).Select(item => item.Completion).ToArray();
        }
        foreach (var completion in ready) completion.TrySetResult();
    }
}
