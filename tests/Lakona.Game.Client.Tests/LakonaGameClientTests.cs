using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class LakonaGameClientCoreTests
{
    [Fact]
    public void LakonaGameClientOptions_does_not_expose_heartbeat_policy()
    {
        var propertyNames = typeof(LakonaGameClientOptions)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("HeartbeatEnabled", propertyNames);
        Assert.DoesNotContain("HeartbeatInterval", propertyNames);
        Assert.DoesNotContain("HeartbeatTimeout", propertyNames);
    }

    [Fact]
    public void ApplyServerHello_applies_heartbeat_policy()
    {
        var client = new LakonaGameClientCore();

        client.ApplyServerHello(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            Heartbeat = new GameHeartbeatHandshakeSettings
            {
                Interval = TimeSpan.FromSeconds(8),
                Timeout = TimeSpan.FromSeconds(24)
            }
        });

        Assert.Equal(TimeSpan.FromSeconds(8), client.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(24), client.HeartbeatTimeout);
    }

    [Fact]
    public void ApplyServerHello_rejects_unsupported_protocol_version()
    {
        var client = new LakonaGameClientCore();

        var ex = Assert.Throws<InvalidOperationException>(() => client.ApplyServerHello(new GameServerHello
        {
            SelectedProtocolVersion = 2
        }));

        Assert.Contains("Unsupported Lakona game handshake protocol version", ex.Message, StringComparison.Ordinal);
    }
    [Fact]
    public async Task MainEntryProcessesReliablePushAndAppliesAckOutcome()
    {
        var client = new LakonaGameClientCore();
        var session = "session-a";
        var applied = new List<string>();
        client.StartSession(session);

        var result = await client.ProcessReliablePushAsync(
            ReliablePushSequence.From(1),
            "matched",
            (payload, _) =>
            {
                applied.Add(payload);
                return default;
            },
            (_, _) => new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.StateRefreshRequired()),
            TestContext.Current.CancellationToken);

        Assert.True(result.Decision.ShouldApply);
        Assert.Equal("matched", Assert.Single(applied));
        Assert.Equal(ClientSessionPhase.RefreshRequired, client.Snapshot.Phase);
        Assert.Equal(session, client.Snapshot.SessionId);
        Assert.Equal(1, client.Snapshot.LastReliableSequence);
    }

    [Fact]
    public async Task MainEntryMakesStateLostTerminalUntilNewSession()
    {
        var client = new LakonaGameClientCore();
        var session = "session-a";
        client.StartSession(session);

        await client.ProcessReliablePushAsync(
            ReliablePushSequence.From(1),
            "matched",
            (_, _) => default,
            (_, _) => new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.SessionMismatch()),
            TestContext.Current.CancellationToken);
        client.MarkReconnecting();

        Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);
        Assert.Null(client.Snapshot.SessionId);

        var next = "session-b";
        client.StartSession(next);

        Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
        Assert.Equal(next, client.Snapshot.SessionId);
    }

    [Fact]
    public void MainEntryAppliesSessionTerminationNotice()
    {
        var client = new LakonaGameClientCore();
        var notice = new SessionTerminationNotice(SessionTerminationReason.Policy, "Removed.");
        client.StartSession("session-a", lastReliableSequence: 7);

        client.ApplySessionTerminationNotice(notice);

        Assert.Equal(ClientSessionPhase.Terminated, client.Snapshot.Phase);
        Assert.Null(client.Snapshot.SessionId);
        Assert.Equal(0, client.Snapshot.LastReliableSequence);
        Assert.Same(notice, client.Snapshot.Termination);
    }

    [Fact]
    public async Task RawTerminationNotificationAppliesInternalCodecWithoutEndpointSerializer()
    {
        var client = new LakonaGameClientCore();
        client.StartSession("session-a");
        var transport = new OneShotTerminationNotificationTransport(
            new SessionTerminationNotice(SessionTerminationReason.Policy, "Removed."));
        await using var rpc = new RpcClientRuntime(transport, new NoopSerializer());
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        rpc.RegisterRawNotificationHandler(
            GameSessionNotificationRpcIds.ServiceId,
            GameSessionNotificationRpcIds.TerminatedNotificationId,
            payload =>
            {
                client.ApplySessionTerminationNotice(
                    LakonaInternalCodec.DecodeSessionTerminationNotice(payload));
                handled.TrySetResult();
                return default;
            });

        await rpc.StartAsync(TestContext.Current.CancellationToken);
        await handled.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientSessionPhase.Terminated, client.Snapshot.Phase);
        Assert.Equal(SessionTerminationReason.Policy, client.Snapshot.Termination?.Reason);
        Assert.Equal("Removed.", client.Snapshot.Termination?.Message);
    }

    [Fact]
    public async Task ReliablePushMetadataNotificationAppliesCallbackAndSendsGeneratedAck()
    {
        var client = new LakonaGameClientCore();
        client.StartSession("session-a", sessionGeneration: 9, lastReliableSequence: 0);
        var metadata = new ReliablePushMetadata(
            "session-a",
            9,
            ReliablePushSequence.From(1),
            "test.notification");
        var transport = new OneShotReliablePushNotificationTransport(metadata);
        await using var rpc = new RpcClientRuntime(transport, new NoopSerializer());
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.BindReliablePush(rpc);
        rpc.RegisterRawNotificationHandler(
            42,
            7,
            _ =>
            {
                handled.TrySetResult();
                return default;
            });

        await rpc.StartAsync(TestContext.Current.CancellationToken);
        await handled.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        var ack = await transport.Acknowledgement.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal("session-a", ack.SessionId);
        Assert.Equal(9, ack.SessionGeneration);
        Assert.Equal(1, ack.Sequence.Value);
        await WaitForAsync(
            () => client.Snapshot.LastReliableSequence == 1,
            TestContext.Current.CancellationToken);
        Assert.Equal(1, client.Snapshot.LastReliableSequence);
        Assert.Equal(9, client.Snapshot.SessionGeneration);
    }

    [Fact]
    public void ApplyServerHello_disables_reliable_push_ack_when_server_disables_push()
    {
        var client = new LakonaGameClientCore();

        client.ApplyServerHello(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            ReliablePush = new ReliablePushHandshakeSettings
            {
                Enabled = false,
                AckRequired = false,
            }
        });

        Assert.False(client.ReliablePushEnabled);
        Assert.False(client.ReliablePushAckRequired);
    }

    [Fact]
    public void ApplyServerHello_enables_reliable_push_ack_when_server_enables_push()
    {
        var client = new LakonaGameClientCore();

        client.ApplyServerHello(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            ReliablePush = new ReliablePushHandshakeSettings
            {
                Enabled = true,
                AckRequired = true,
            }
        });

        Assert.True(client.ReliablePushEnabled);
        Assert.True(client.ReliablePushAckRequired);
    }

    [Fact]
    public void Core_marks_ready_after_handshake_pipeline_completes()
    {
        var client = new LakonaGameClientCore();

        client.MarkConnecting();
        client.MarkReady();

        Assert.Equal(ClientSessionPhase.Ready, client.Snapshot.Phase);
        Assert.Null(client.Snapshot.SessionId);
        Assert.Null(client.Snapshot.Failure);
    }

    [Fact]
    public void Core_records_connection_failure_and_keeps_api_instance_terminal()
    {
        var client = new LakonaGameClientCore();
        var failure = new ClientConnectionFailure(
            ClientConnectionFailureKind.HandshakeFailed,
            "handshake rejected");

        client.MarkConnecting();
        client.MarkConnectionFailed(failure);
        client.MarkReady();

        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        Assert.Same(failure, client.Snapshot.Failure);
        Assert.Null(client.Snapshot.SessionId);
    }

    [Fact]
    public void Core_start_session_does_not_promote_connection_failed_client()
    {
        var client = new LakonaGameClientCore();
        var failure = new ClientConnectionFailure(
            ClientConnectionFailureKind.HandshakeFailed,
            "handshake rejected");

        client.MarkConnecting();
        client.MarkConnectionFailed(failure);
        client.StartSession("session-a");

        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        Assert.Same(failure, client.Snapshot.Failure);
        Assert.Null(client.Snapshot.SessionId);
    }

    [Fact]
    public async Task Core_start_session_async_does_not_start_reliable_push_after_connection_failure()
    {
        var client = new LakonaGameClientCore();
        var failure = new ClientConnectionFailure(
            ClientConnectionFailureKind.HandshakeFailed,
            "handshake rejected");
        var applied = new List<string>();

        client.MarkConnecting();
        client.MarkConnectionFailed(failure);
        await client.StartSessionAsync("session-a", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.ProcessReliablePushAsync(
                ReliablePushSequence.From(1),
                "payload",
                (payload, _) =>
                {
                    applied.Add(payload);
                    return default;
                },
                (_, _) => new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.Accepted()),
                TestContext.Current.CancellationToken);
        });

        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        Assert.Same(failure, client.Snapshot.Failure);
        Assert.Null(client.Snapshot.SessionId);
        Assert.Empty(applied);
    }

    [Fact]
    public async Task Core_start_session_async_resets_reliable_push_when_connection_fails_during_cursor_load()
    {
        var cursorStore = new DelayedReliablePushCursorStore();
        var client = new LakonaGameClientCore(cursorStore);
        var failure = new ClientConnectionFailure(
            ClientConnectionFailureKind.HandshakeFailed,
            "handshake rejected");
        var applied = new List<string>();

        client.MarkConnecting();
        var startTask = client.StartSessionAsync("session-a", TestContext.Current.CancellationToken);
        await cursorStore.WaitForLoadAsync(TestContext.Current.CancellationToken);
        client.MarkConnectionFailed(failure);

        cursorStore.ReleaseLoad();
        await startTask;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.ProcessReliablePushAsync(
                ReliablePushSequence.From(1),
                "payload",
                (payload, _) =>
                {
                    applied.Add(payload);
                    return default;
                },
                (_, _) => new ValueTask<ReliablePushAckOutcome>(ReliablePushAckOutcome.Accepted()),
                TestContext.Current.CancellationToken);
        });

        Assert.Equal(ClientSessionPhase.ConnectionFailed, client.Snapshot.Phase);
        Assert.Same(failure, client.Snapshot.Failure);
        Assert.Null(client.Snapshot.SessionId);
        Assert.Empty(applied);
    }

    [Fact]
    public void Core_start_session_promotes_ready_client_to_active_session()
    {
        var client = new LakonaGameClientCore();

        client.MarkConnecting();
        client.MarkReady();
        client.StartSession("session-a", sessionGeneration: 7, lastReliableSequence: 3);

        Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
        Assert.Equal("session-a", client.Snapshot.SessionId);
        Assert.Equal(3, client.Snapshot.LastReliableSequence);
        Assert.Equal(7, client.Snapshot.SessionGeneration);
    }

    [Fact]
    public async Task Core_start_session_async_records_session_generation()
    {
        var client = new LakonaGameClientCore();

        await client.StartSessionAsync(
            "session-a",
            11,
            TestContext.Current.CancellationToken);

        Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
        Assert.Equal("session-a", client.Snapshot.SessionId);
        Assert.Equal(11, client.Snapshot.SessionGeneration);
    }

    [Fact]
    public async Task Heartbeat_state_lost_marks_client_state_lost()
    {
        var client = new LakonaGameClientCore();
        await using var rpc = await HeartbeatRuntimeFixture.CreateAsync(
            new GameHeartbeatReply { Status = GameHeartbeatStatus.StateLost },
            TestContext.Current.CancellationToken);
        var loop = new LakonaGameHeartbeatLoop(
            rpc.Client,
            client,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(45));

        await loop.SendOnceAsync(TestContext.Current.CancellationToken);

        rpc.ThrowIfTransportFailed();
        await WaitForAsync(
            () => client.Snapshot.Phase == ClientSessionPhase.StateLost,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Heartbeat_terminated_marks_client_terminated()
    {
        var client = new LakonaGameClientCore();
        client.StartSession("session-a");
        await using var rpc = await HeartbeatRuntimeFixture.CreateAsync(
            new GameHeartbeatReply { Status = GameHeartbeatStatus.Terminated, Message = "removed" },
            TestContext.Current.CancellationToken);
        var loop = new LakonaGameHeartbeatLoop(
            rpc.Client,
            client,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(45));

        await loop.SendOnceAsync(TestContext.Current.CancellationToken);

        rpc.ThrowIfTransportFailed();
        await WaitForAsync(
            () => client.Snapshot.Phase == ClientSessionPhase.Terminated,
            TestContext.Current.CancellationToken);
        Assert.Equal("removed", client.Snapshot.Termination?.Message);
    }

    [Fact]
    public async Task Heartbeat_rpc_failure_marks_reconnecting()
    {
        var client = new LakonaGameClientCore();
        client.MarkConnecting();
        client.MarkReady();
        await using var rpc = await HeartbeatRuntimeFixture.CreateFailureAsync(TestContext.Current.CancellationToken);
        var loop = new LakonaGameHeartbeatLoop(
            rpc.Client,
            client,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(45));

        await loop.SendOnceAsync(TestContext.Current.CancellationToken);

        rpc.ThrowIfTransportFailed();
        Assert.Equal(ClientSessionPhase.Reconnecting, client.Snapshot.Phase);
    }

    [Fact]
    public async Task Heartbeat_request_carries_active_session_identity()
    {
        var client = new LakonaGameClientCore();
        await client.StartSessionAsync(
            "session-a",
            13,
            TestContext.Current.CancellationToken);
        await using var rpc = await HeartbeatRuntimeFixture.CreateAsync(
            new GameHeartbeatReply { Status = GameHeartbeatStatus.Ok },
            TestContext.Current.CancellationToken);
        var loop = new LakonaGameHeartbeatLoop(
            rpc.Client,
            client,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(45));

        await loop.SendOnceAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(rpc.Request);
        Assert.Equal("session-a", rpc.Request!.SessionId);
        Assert.Equal(13, rpc.Request.SessionGeneration);
    }

    [Fact]
    public async Task ApplyServerHello_rejects_invalid_heartbeat_interval()
    {
        var client = new LakonaGameClientCore();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => client.ApplyServerHello(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            Heartbeat = new GameHeartbeatHandshakeSettings
            {
                Interval = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(45)
            }
        }));

        Assert.Equal("Heartbeat.Interval", ex.ParamName);
    }

    [Fact]
    public async Task ApplyServerHello_rejects_heartbeat_timeout_shorter_than_interval()
    {
        var client = new LakonaGameClientCore();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => client.ApplyServerHello(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            Heartbeat = new GameHeartbeatHandshakeSettings
            {
                Interval = TimeSpan.FromSeconds(45),
                Timeout = TimeSpan.FromSeconds(15)
            }
        }));

        Assert.Equal("Heartbeat.Timeout", ex.ParamName);
    }

    [Fact]
    public async Task StartHeartbeat_allows_only_one_loop_when_called_concurrently()
    {
        var client = new LakonaGameClientCore();
        client.ApplyServerHello(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            Heartbeat = new GameHeartbeatHandshakeSettings
            {
                Interval = TimeSpan.FromHours(1),
                Timeout = TimeSpan.FromHours(2)
            }
        });
        var start = new ManualResetEventSlim();
        var successes = 0;
        var duplicateStarts = 0;
        var otherFailures = new List<Exception>();
        var runtimes = new List<RpcClientRuntime>();

        Task[] tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                var rpc = CreateUnstartedRuntime();
                lock (runtimes)
                {
                    runtimes.Add(rpc);
                }

                try
                {
                    client.StartHeartbeat(rpc);
                    Interlocked.Increment(ref successes);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref duplicateStarts);
                }
                catch (Exception ex)
                {
                    lock (otherFailures)
                    {
                        otherFailures.Add(ex);
                    }
                }
            }, TestContext.Current.CancellationToken))
            .ToArray();

        start.Set();

        await Task.WhenAll(tasks);
        await client.DisposeAsync();
        foreach (var runtime in runtimes)
        {
            await runtime.DisposeAsync();
        }

        Assert.Empty(otherFailures);
        Assert.Equal(1, successes);
        Assert.Equal(tasks.Length - 1, duplicateStarts);
    }

    private static RpcClientRuntime CreateUnstartedRuntime()
    {
        return new RpcClientRuntime(new NoopTransport(), new NoopSerializer());
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached before the deadline.");
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class HeartbeatRuntimeFixture : IAsyncDisposable
    {
        private readonly OneShotHeartbeatTransport _transport;

        private HeartbeatRuntimeFixture(
            RpcClientRuntime client,
            OneShotHeartbeatTransport transport)
        {
            Client = client;
            _transport = transport;
        }

        public RpcClientRuntime Client { get; }

        public GameHeartbeatRequest? Request => _transport.Request;

        public void ThrowIfTransportFailed()
        {
            _transport.ThrowIfFailed();
        }

        public static async Task<HeartbeatRuntimeFixture> CreateAsync(
            GameHeartbeatReply reply,
            CancellationToken cancellationToken)
        {
            return await CreateAsync(RpcStatus.Ok, reply, null, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<HeartbeatRuntimeFixture> CreateFailureAsync(CancellationToken cancellationToken)
        {
            return await CreateAsync(
                RpcStatus.HandlerError,
                null,
                "network closed",
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task<HeartbeatRuntimeFixture> CreateAsync(
            RpcStatus status,
            GameHeartbeatReply? reply,
            string? errorMessage,
            CancellationToken cancellationToken)
        {
            var transport = new OneShotHeartbeatTransport(status, reply, errorMessage);
            var client = new RpcClientRuntime(transport, new NoopSerializer());
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            return new HeartbeatRuntimeFixture(client, transport);
        }
    }

    private sealed class OneShotHeartbeatTransport : ITransport
    {
        private readonly RpcStatus _status;
        private readonly GameHeartbeatReply? _reply;
        private readonly string? _errorMessage;
        private readonly TaskCompletionSource<TransportFrame> _response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception? _failure;
        private int _sent;

        public OneShotHeartbeatTransport(RpcStatus status, GameHeartbeatReply? reply, string? errorMessage)
        {
            _status = status;
            _reply = reply;
            _errorMessage = errorMessage;
        }

        public bool IsConnected { get; private set; }

        public GameHeartbeatRequest? Request { get; private set; }

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return default;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            try
            {
                if (Interlocked.Exchange(ref _sent, 1) != 0)
                {
                    throw new InvalidOperationException("Only one heartbeat request is expected.");
                }

                using var requestFrame = TransportFrame.CopyOf(frame.Span);
                using var request = RpcEnvelopeCodec.DecodeRequest(requestFrame);
                if (request.ServiceId != GameHeartbeatRpcIds.ServiceId)
                {
                    throw new InvalidOperationException(
                        $"Expected heartbeat service {GameHeartbeatRpcIds.ServiceId}, got {request.ServiceId}.");
                }

                if (request.MethodId != GameHeartbeatRpcIds.HeartbeatMethodId)
                {
                    throw new InvalidOperationException(
                        $"Expected heartbeat method {GameHeartbeatRpcIds.HeartbeatMethodId}, got {request.MethodId}.");
                }

                Request = LakonaInternalCodec.DecodeGameHeartbeatRequest(request.Payload.Memory);

                var payload = _status == RpcStatus.Ok
                    ? LakonaInternalCodec.EncodeGameHeartbeatReply(_reply!)
                    : Array.Empty<byte>();
                _response.SetResult(RpcEnvelopeCodec.EncodeResponse(
                    request.RequestId,
                    _status,
                    payload,
                    _errorMessage));
                return default;
            }
            catch (Exception ex)
            {
                _failure = ex;
                throw;
            }
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            return await _response.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return default;
        }

        public void ThrowIfFailed()
        {
            if (_failure is not null)
            {
                throw new InvalidOperationException("Heartbeat test transport failed while sending the request.", _failure);
            }
        }
    }

    private sealed class OneShotTerminationNotificationTransport : ITransport
    {
        private readonly SessionTerminationNotice _notice;
        private readonly TaskCompletionSource<TransportFrame> _notification =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _received;

        public OneShotTerminationNotificationTransport(SessionTerminationNotice notice)
        {
            _notice = notice;
        }

        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            var push = new RpcPushEnvelope
            {
                ServiceId = GameSessionNotificationRpcIds.ServiceId,
                MethodId = GameSessionNotificationRpcIds.TerminatedNotificationId,
                Payload = LakonaInternalCodec.EncodeSessionTerminationNotice(_notice)
            };
            _notification.SetResult(RpcEnvelopeCodec.EncodePush(push));
            return default;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _received, 1) == 0)
            {
                return await _notification.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return default;
        }
    }

    private sealed class OneShotReliablePushNotificationTransport : ITransport
    {
        private readonly ReliablePushMetadata _metadata;
        private readonly TaskCompletionSource<TransportFrame> _notification =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<TransportFrame> _ackResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _received;
        private int _ackReceived;

        public OneShotReliablePushNotificationTransport(ReliablePushMetadata metadata)
        {
            _metadata = metadata;
        }

        public TaskCompletionSource<ReliablePushAckRequest> Acknowledgement { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            var push = new RpcPushEnvelope
            {
                ServiceId = 42,
                MethodId = 7,
                Payload = Array.Empty<byte>(),
                Metadata = new RpcPushMetadata
                {
                    Type = LakonaInternalCodec.ReliablePushMetadataType,
                    Payload = LakonaInternalCodec.EncodeReliablePushMetadata(_metadata)
                }
            };
            _notification.SetResult(RpcEnvelopeCodec.EncodePush(push));
            return default;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            using var requestFrame = TransportFrame.CopyOf(frame.Span);
            using var request = RpcEnvelopeCodec.DecodeRequest(requestFrame);
            Assert.Equal(GameReliablePushRpcIds.ServiceId, request.ServiceId);
            Assert.Equal(GameReliablePushRpcIds.AckMethodId, request.MethodId);
            var ack = LakonaInternalCodec.DecodeReliablePushAckRequest(request.Payload.Memory);
            Acknowledgement.TrySetResult(ack);
            _ackResponse.SetResult(RpcEnvelopeCodec.EncodeResponse(
                request.RequestId,
                RpcStatus.Ok,
                LakonaInternalCodec.EncodeReliablePushAckOutcome(ReliablePushAckOutcome.Accepted())));
            return default;
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _received, 1) == 0)
            {
                return await _notification.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            if (Interlocked.Exchange(ref _ackReceived, 1) == 0)
            {
                return await _ackResponse.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return default;
        }
    }

    private sealed class NoopTransport : ITransport
    {
        public bool IsConnected => false;

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }

    private sealed class NoopSerializer : IRpcSerializer
    {
        public TransportFrame SerializeFrame<T>(T value)
        {
            return TransportFrame.Empty;
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data)
        {
            throw new NotSupportedException();
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class DelayedReliablePushCursorStore : IReliablePushCursorStore
    {
        private readonly TaskCompletionSource _loadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<long> _loadReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<long> LoadAsync(
            string sessionId,
            long sessionGeneration,
            CancellationToken cancellationToken = default)
        {
            _loadStarted.TrySetResult();
            return await _loadReleased.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask SaveAsync(
            string sessionId,
            long sessionGeneration,
            long sequence,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask ClearAsync(
            string sessionId,
            long sessionGeneration,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public async ValueTask WaitForLoadAsync(CancellationToken cancellationToken)
        {
            await _loadStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void ReleaseLoad(long lastReliableSequence = 0)
        {
            _loadReleased.TrySetResult(lastReliableSequence);
        }
    }
}
