using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Loopback;

namespace Lakona.Rpc.Tests;

public class RpcSessionTests
{
    private static byte[] SerializeBytes<T>(IRpcSerializer serializer, T value)
    {
        using var frame = serializer.SerializeFrame(value);
        return frame.ToArray();
    }

    private static (ITransport clientTransport, RpcSession server) CreateServerWithHandler(
        int serviceId, int methodId, RpcHandler handler)
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);
        server.Register(serviceId, methodId, handler);
        return (clientTransport, server);
    }

    [Fact]
    public async Task Register_HandlerReceivesRequest()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);

        RpcRequestEnvelope? receivedReq = null;
        server.Register(1, 1, (req, ct) =>
        {
            receivedReq = req;
            return ValueTask.FromResult(new RpcResponseEnvelope
            {
                RequestId = req.RequestId,
                Status = RpcStatus.Ok,
                Payload = Array.Empty<byte>()
            });
        });

        await server.StartAsync();
        await clientTransport.ConnectAsync();

        var reqEnv = new RpcRequestEnvelope
        {
            RequestId = 42,
            ServiceId = 1,
            MethodId = 1,
            Payload = SerializeBytes(serializer, "hello")
        };
        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(reqEnv));

        using var resp = await ReceiveResponseAsync(clientTransport);

        Assert.Equal(RpcStatus.Ok, resp.Status);
        Assert.NotNull(receivedReq);
        Assert.Equal(42u, receivedReq!.RequestId);

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task UnregisteredHandler_ReturnsNotFound()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);

        await server.StartAsync();
        await clientTransport.ConnectAsync();

        var reqEnv = new RpcRequestEnvelope
        {
            RequestId = 1,
            ServiceId = 999,
            MethodId = 999,
            Payload = Array.Empty<byte>()
        };
        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(reqEnv));

        using var resp = await ReceiveResponseAsync(clientTransport);

        Assert.Equal(RpcStatus.NotFound, resp.Status);
        Assert.Contains("No handler", resp.ErrorMessage);

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task HandlerThrows_ReturnsHandlerError()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var logger = new TestLogger();
        var server = new RpcSession(
            serverTransport,
            serializer,
            registry: null,
            contextId: Guid.NewGuid().ToString("N"),
            ownsTransport: false,
            keepAlive: null,
            logger: logger);

        server.Register(1, 1, (req, ct) => throw new InvalidOperationException("test error"));

        await server.StartAsync();
        await clientTransport.ConnectAsync();

        var reqEnv = new RpcRequestEnvelope
        {
            RequestId = 1,
            ServiceId = 1,
            MethodId = 1,
            Payload = Array.Empty<byte>()
        };
        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(reqEnv));

        using var resp = await ReceiveResponseAsync(clientTransport);

        Assert.Equal(RpcStatus.HandlerError, resp.Status);
        Assert.Equal("RPC handler failed.", resp.ErrorMessage);
        Assert.DoesNotContain("test error", resp.ErrorMessage);
        Assert.DoesNotContain(nameof(InvalidOperationException), resp.ErrorMessage);
        Assert.Contains(logger.Entries, entry =>
            entry.LogLevel == LogLevel.Error &&
            entry.Exception is InvalidOperationException ioe &&
            ioe.Message == "test error" &&
            entry.Message.Contains("request 1", StringComparison.OrdinalIgnoreCase));

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_Throws()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);

        await server.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync().AsTask());

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_AfterStopOnSameSession_Throws()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);

        await server.StartAsync();
        await server.StopAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync().AsTask());

        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_AfterStopOnNewSession_Succeeds()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);

        await server.StartAsync();
        await server.StopAsync();

        LoopbackTransport.CreatePair(out var clientTransport2, out var serverTransport2);
        var server2 = new RpcSession(serverTransport2, serializer);
        await server2.StartAsync();
        await server2.StopAsync();

        await clientTransport.DisposeAsync();
        await clientTransport2.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentRegisterAndServe_HandlerDictionaryIsThreadSafe()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);

        server.Register(1, 1, (req, ct) => ValueTask.FromResult(new RpcResponseEnvelope
        {
            RequestId = req.RequestId,
            Status = RpcStatus.Ok,
            Payload = Array.Empty<byte>()
        }));

        await server.StartAsync();
        await clientTransport.ConnectAsync();

        var registerTask = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                server.Register(100 + i, 1, (req, ct) => ValueTask.FromResult(new RpcResponseEnvelope
                {
                    RequestId = req.RequestId,
                    Status = RpcStatus.Ok,
                    Payload = Array.Empty<byte>()
                }));
            }
        });

        var callTask = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                var reqEnv = new RpcRequestEnvelope
                {
                    RequestId = (uint)(i + 1),
                    ServiceId = 1,
                    MethodId = 1,
                    Payload = Array.Empty<byte>()
                };
                await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(reqEnv));
                using var resp = await ReceiveResponseAsync(clientTransport);
                Assert.Equal(RpcStatus.Ok, resp.Status);
            }
        });

        await Task.WhenAll(registerTask, callTask);

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task SharedRegistry_CanServeMultipleConnections()
    {
        var serializer = new JsonRpcSerializer();
        var registry = new RpcServiceRegistry();
        var handledConnections = 0;

        registry.Register(1, 1, (server, req, ct) =>
        {
            Interlocked.Increment(ref handledConnections);
            using var payload = server.Serializer.SerializeFrame(server.ContextId);
            return ValueTask.FromResult(RpcEnvelopeCodec.EncodeResponse(
                req.RequestId,
                RpcStatus.Ok,
                payload.Memory));
        });

        LoopbackTransport.CreatePair(out var clientTransport1, out var serverTransport1);
        LoopbackTransport.CreatePair(out var clientTransport2, out var serverTransport2);
        var server1 = new RpcSession(serverTransport1, serializer, registry);
        var server2 = new RpcSession(serverTransport2, serializer, registry);

        await server1.StartAsync();
        await server2.StartAsync();
        await clientTransport1.ConnectAsync();
        await clientTransport2.ConnectAsync();

        await clientTransport1.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 1,
            ServiceId = 1,
            MethodId = 1,
            Payload = Array.Empty<byte>()
        }));
        await clientTransport2.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 2,
            ServiceId = 1,
            MethodId = 1,
            Payload = Array.Empty<byte>()
        }));

        using var resp1 = await ReceiveResponseAsync(clientTransport1);
        using var resp2 = await ReceiveResponseAsync(clientTransport2);

        Assert.Equal(RpcStatus.Ok, resp1.Status);
        Assert.Equal(RpcStatus.Ok, resp2.Status);
        Assert.NotEqual(serializer.Deserialize<string>(resp1.Payload), serializer.Deserialize<string>(resp2.Payload));
        Assert.Equal(2, handledConnections);

        await server1.StopAsync();
        await server2.StopAsync();
        await clientTransport1.DisposeAsync();
        await clientTransport2.DisposeAsync();
    }

    [Fact]
    public async Task ServerLifetime_IndependentOfStartCancellationToken()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);

        server.Register(1, 1, (req, ct) => ValueTask.FromResult(new RpcResponseEnvelope
        {
            RequestId = req.RequestId,
            Status = RpcStatus.Ok,
            Payload = Array.Empty<byte>()
        }));

        using var startCts = new CancellationTokenSource();
        await server.StartAsync(startCts.Token);
        await clientTransport.ConnectAsync();

        startCts.Cancel();

        await Task.Delay(100);

        var reqEnv = new RpcRequestEnvelope
        {
            RequestId = 1,
            ServiceId = 1,
            MethodId = 1,
            Payload = Array.Empty<byte>()
        };
        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(reqEnv));

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var resp = await ReceiveResponseAsync(clientTransport, readCts.Token);

        Assert.Equal(RpcStatus.Ok, resp.Status);

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task ScopedServiceCache_IsPerConnectionAndClearedOnStop()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var server = new RpcSession(serverTransport, new JsonRpcSerializer());

        var first = server.GetOrAddScopedService(1, _ => new object());
        var second = server.GetOrAddScopedService(1, _ => new object());
        Assert.Same(first, second);

        await server.StopAsync();

        var third = server.GetOrAddScopedService(1, _ => new object());
        Assert.NotSame(first, third);

        await clientTransport.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public void Constructor_NullTransport_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RpcSession(null!, new JsonRpcSerializer()));
    }

    [Fact]
    public void Constructor_NullSerializer_Throws()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        Assert.Throws<ArgumentNullException>(() => new RpcSession(client, null!));
    }

    [Fact]
    public async Task ConcurrentRequests_FastHandlerNotBlockedBySlowHandler()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);

        server.Register(1, 1, async (req, ct) =>
        {
            await Task.Delay(300, ct);
            return new RpcResponseEnvelope
            {
                RequestId = req.RequestId,
                Status = RpcStatus.Ok,
                Payload = Array.Empty<byte>()
            };
        });

        server.Register(1, 2, (req, ct) =>
            ValueTask.FromResult(new RpcResponseEnvelope
            {
                RequestId = req.RequestId,
                Status = RpcStatus.Ok,
                Payload = Array.Empty<byte>()
            }));

        await server.StartAsync();
        await clientTransport.ConnectAsync();

        var slowReq = new RpcRequestEnvelope { RequestId = 1, ServiceId = 1, MethodId = 1, Payload = Array.Empty<byte>() };
        var fastReq = new RpcRequestEnvelope { RequestId = 2, ServiceId = 1, MethodId = 2, Payload = Array.Empty<byte>() };

        var sw = Stopwatch.StartNew();
        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(slowReq));
        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(fastReq));

        using var firstResp = await ReceiveResponseAsync(clientTransport);
        var fastResponseElapsed = sw.ElapsedMilliseconds;
        using var secondResp = await ReceiveResponseAsync(clientTransport);

        Assert.Equal(2u, firstResp.RequestId);
        Assert.Equal(1u, secondResp.RequestId);
        Assert.True(fastResponseElapsed < 250, $"Fast response was delayed: {fastResponseElapsed}ms");

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task SendNotificationAsync_ConcurrentCalls_AreSerializedOnTransport()
    {
        var transport = new ConcurrentSendDetectTransport();
        var server = new RpcSession(transport, new JsonRpcSerializer());

        var sends = Enumerable.Range(0, 24)
            .Select(i => server.SendNotificationAsync(1, 1, i).AsTask())
            .ToArray();

        await Task.WhenAll(sends);

        Assert.Equal(1, transport.MaxConcurrentSends);
        await server.DisposeAsync();
    }

    [Fact]
    public async Task RequestQueueLimit_RejectsRequestsBeyondBudget()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new RpcSession(
            serverTransport,
            serializer,
            registry: null,
            contextId: "queue-limit-test",
            ownsTransport: false,
            keepAlive: null,
            logger: null,
            limits: new RpcServerLimits
            {
                MaxConcurrentRequestsPerSession = 1,
                MaxQueuedRequestsPerSession = 1,
                MaxPendingAcceptedConnections = 8
            });

        server.Register(1, 1, async (req, ct) =>
        {
            firstRequestStarted.TrySetResult();
            await releaseFirstRequest.Task.WaitAsync(ct);
            return new RpcResponseEnvelope
            {
                RequestId = req.RequestId,
                Status = RpcStatus.Ok,
                Payload = Array.Empty<byte>()
            };
        });

        await server.StartAsync();
        await clientTransport.ConnectAsync();

        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 1,
            ServiceId = 1,
            MethodId = 1,
            Payload = Array.Empty<byte>()
        }));

        await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 2,
            ServiceId = 1,
            MethodId = 1,
            Payload = Array.Empty<byte>()
        }));

        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 3,
            ServiceId = 1,
            MethodId = 1,
            Payload = Array.Empty<byte>()
        }));

        using var overloadResponse = await ReceiveResponseAsync(clientTransport);
        Assert.Equal(3u, overloadResponse.RequestId);
        Assert.Equal(RpcStatus.Overloaded, overloadResponse.Status);
        Assert.Contains("overloaded", overloadResponse.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        releaseFirstRequest.TrySetResult();

        using var remainingResponse1 = await ReceiveResponseAsync(clientTransport);
        using var remainingResponse2 = await ReceiveResponseAsync(clientTransport);
        var remainingResponses = new[]
        {
            (remainingResponse1.RequestId, remainingResponse1.Status),
            (remainingResponse2.RequestId, remainingResponse2.Status)
        }.OrderBy(response => response.RequestId).ToArray();

        Assert.Equal(1u, remainingResponses[0].RequestId);
        Assert.Equal(RpcStatus.Ok, remainingResponses[0].Status);
        Assert.Equal(2u, remainingResponses[1].RequestId);
        Assert.Equal(RpcStatus.Ok, remainingResponses[1].Status);

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task BoundedConnectionAcceptor_DropsConnectionsBeyondQueueLimit()
    {
        var acceptor = new BurstConnectionAcceptor(3);
        var logger = new TestLogger();
        await using var bounded = new BoundedConnectionAcceptor(acceptor, 1, logger);

        await Task.Delay(100);

        var accepted = await bounded.AcceptAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);

        Assert.Equal(1, acceptor.ActiveTransportCount);
        Assert.Equal(2, acceptor.DisposedTransportCount);
        Assert.Contains(logger.Entries, entry =>
            entry.LogLevel == LogLevel.Warning &&
            entry.Message.Contains("pending accepted connection queue is full", StringComparison.OrdinalIgnoreCase));

        await accepted.Transport.DisposeAsync();
    }

    [Fact]
    public async Task BoundedConnectionAcceptor_DoesNotReturnDisconnectedQueuedConnection()
    {
        var staleTransport = new ToggleableConnectionTransport(initiallyConnected: false);
        var liveTransport = new ToggleableConnectionTransport(initiallyConnected: true);
        var acceptor = new StaticConnectionAcceptor(
            new RpcAcceptedConnection(staleTransport, "stale"),
            new RpcAcceptedConnection(liveTransport, "live"));
        var logger = new TestLogger();
        await using var bounded = new BoundedConnectionAcceptor(acceptor, 2, logger);

        await Task.Delay(100);

        var accepted = await bounded.AcceptAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);

        Assert.Same(liveTransport, accepted.Transport);
        Assert.Equal(1, staleTransport.DisposeCount);
        Assert.True(liveTransport.IsConnected);
    }

    [Fact]
    public async Task RemoteClose_ResetsServerStateAndRaisesDisconnected()
    {
        var transport = new ReconnectableEmptyFrameTransport();
        var server = new RpcSession(transport, new JsonRpcSerializer());

        var disconnectedCount = 0;
        server.Disconnected += ex =>
        {
            Assert.Null(ex);
            Interlocked.Increment(ref disconnectedCount);
        };

        await server.StartAsync();
        await server.WaitForCompletionAsync();

        await server.StartAsync();
        await server.WaitForCompletionAsync();

        Assert.Equal(2, transport.ConnectCount);
        Assert.Equal(2, disconnectedCount);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task RemoteClose_CancelsInflightRequestAndCompletesSession()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new RpcSession(serverTransport, serializer);

        server.Register(1, 1, async (req, ct) =>
        {
            requestStarted.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                handlerCancelled.TrySetResult();
                throw;
            }

            return new RpcResponseEnvelope
            {
                RequestId = req.RequestId,
                Status = RpcStatus.Ok,
                Payload = Array.Empty<byte>()
            };
        });

        await server.StartAsync();
        await clientTransport.ConnectAsync();

        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
        {
            RequestId = 1,
            ServiceId = 1,
            MethodId = 1,
            Payload = Array.Empty<byte>()
        }));

        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await clientTransport.DisposeAsync();

        await handlerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await server.WaitForCompletionAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        await server.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhenOwningTransport_DisposesTransportOnce()
    {
        var transport = new ConcurrentSendDetectTransport();
        var server = new RpcSession(transport, new JsonRpcSerializer(), ownsTransport: true);

        await server.DisposeAsync();
        await server.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_DuringPendingReceive_Completes()
    {
        var transport = new IdleSessionTransport();
        var server = new RpcSession(transport, new JsonRpcSerializer());

        await server.StartAsync();

        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SendNotificationAsync_WhenSendFails_PropagatesAndDisposeStillCompletes()
    {
        var transport = new ThrowingSendSessionTransport();
        var server = new RpcSession(transport, new JsonRpcSerializer());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.SendNotificationAsync(1, 1, "send-fail").AsTask());

        Assert.Equal("send failed", ex.Message);

        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task KeepAlivePing_ReceivesPong()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(serverTransport, serializer);

        await server.StartAsync();
        await clientTransport.ConnectAsync();

        var ping = new RpcKeepAlivePingEnvelope
        {
            TimestampTicksUtc = DateTimeOffset.UtcNow.UtcTicks
        };

        await clientTransport.SendFrameAsync(RpcEnvelopeCodec.EncodeKeepAlivePing(ping));
        var frame = await clientTransport.ReceiveFrameAsync();
        var pong = RpcEnvelopeCodec.DecodeKeepAlivePong(frame.Span);

        Assert.Equal(ping.TimestampTicksUtc, pong.TimestampTicksUtc);

        await server.StopAsync();
        await clientTransport.DisposeAsync();
    }

    [Fact]
    public async Task KeepAliveTimeout_DisconnectsIdleSession()
    {
        var transport = new IdleSessionTransport();
        var server = new RpcSession(
            transport,
            new JsonRpcSerializer(),
            registry: null,
            contextId: "keepalive-test",
            ownsTransport: false,
            keepAlive: new RpcKeepAliveOptions
            {
                Enabled = true,
                Interval = TimeSpan.FromMilliseconds(40),
                Timeout = TimeSpan.FromMilliseconds(120),
                MeasureRtt = false
            });

        var disconnectedTcs = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Disconnected += ex => disconnectedTcs.TrySetResult(ex);

        await server.StartAsync();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var registration = timeoutCts.Token.Register(() => disconnectedTcs.TrySetCanceled());
        var error = await disconnectedTcs.Task;

        var timeout = Assert.IsType<TimeoutException>(error);
        Assert.Contains("keepalive", timeout.Message, StringComparison.OrdinalIgnoreCase);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task KeepAlive_WithResponsiveIdleClient_KeepsSessionAlive()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var serializer = new JsonRpcSerializer();
        var server = new RpcSession(
            serverTransport,
            serializer,
            registry: null,
            contextId: "keepalive-responsive",
            ownsTransport: false,
            keepAlive: new RpcKeepAliveOptions
            {
                Enabled = true,
                Interval = TimeSpan.FromMilliseconds(40),
                Timeout = TimeSpan.FromMilliseconds(120),
                MeasureRtt = false
            });

        server.Register(1, 1, (req, ct) => ValueTask.FromResult(new RpcResponseEnvelope
        {
            RequestId = req.RequestId,
            Status = RpcStatus.Ok,
            Payload = SerializeBytes(serializer, "alive")
        }));

        var disconnected = 0;
        server.Disconnected += _ => Interlocked.Increment(ref disconnected);

        await server.StartAsync();

        var client = new RpcClientRuntime(clientTransport, serializer);
        await client.StartAsync();

        await Task.Delay(300);
        var reply = await client.CallAsync(new RpcMethod<string, string>(1, 1), string.Empty);

        Assert.Equal("alive", reply);
        Assert.Equal(0, disconnected);

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_CancellationToken_TerminatesSessionWithActiveConnection()
    {
        // Regression: StartAsync creates a fresh CancellationTokenSource that is NOT linked
        // to the external ct passed to RunAsync/StartAsync. With a no-EOF transport (KCP/UDP),
        // cancelling the external ct has no effect on the internal LoopAsync — RunAsync hangs.
        var transport = new IdleSessionTransport();
        var server = new RpcSession(transport, new JsonRpcSerializer());

        using var cts = new CancellationTokenSource();
        var runTask = server.RunAsync(cts.Token).AsTask();

        await Task.Delay(100); // let the session loop block in ReceiveFrameAsync

        cts.Cancel();

        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(completed == runTask,
            "RunAsync did not complete within 5 seconds after the CancellationToken was cancelled. " +
            "The internal CancellationTokenSource in StartAsync must be linked to the external ct.");
    }

    [Fact]
    public void TrackedTaskCollection_WaitAsync_DoesNotSnapshotTasksWithToArray()
    {
        var trackedTaskCollectionType = typeof(RpcSession).Assembly.GetType("Lakona.Rpc.Server.TrackedTaskCollection");
        Assert.NotNull(trackedTaskCollectionType);

        var waitAsync = trackedTaskCollectionType!.GetMethod(
            "WaitAsync",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(waitAsync);

        var stateMachineAttribute = waitAsync!.GetCustomAttribute<AsyncStateMachineAttribute>();
        Assert.NotNull(stateMachineAttribute);

        var moveNext = stateMachineAttribute!.StateMachineType.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(moveNext);

        var calledMethods = GetCalledMethods(moveNext!);
        Assert.DoesNotContain(
            calledMethods,
            method => method is MethodInfo methodInfo &&
                      methodInfo.Name == nameof(Enumerable.ToArray) &&
                      methodInfo.DeclaringType == typeof(Enumerable));
    }

    private sealed class ConcurrentSendDetectTransport : ITransport
    {
        private int _currentSends;
        private int _disposeCount;
        private int _maxConcurrentSends;

        public int MaxConcurrentSends => Volatile.Read(ref _maxConcurrentSends);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            var current = Interlocked.Increment(ref _currentSends);
            UpdateMaxConcurrentSends(current);

            await Task.Delay(5, ct);
            Interlocked.Decrement(ref _currentSends);
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            return ValueTask.FromResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }

        private void UpdateMaxConcurrentSends(int value)
        {
            while (true)
            {
                var snapshot = MaxConcurrentSends;
                if (value <= snapshot)
                    return;

                if (Interlocked.CompareExchange(ref _maxConcurrentSends, value, snapshot) == snapshot)
                    return;
            }
        }
    }

    private static async Task<RpcResponseFrame> ReceiveResponseAsync(ITransport transport, CancellationToken ct = default)
    {
        using var frame = await transport.ReceiveFrameAsync(ct);
        return RpcEnvelopeCodec.DecodeResponse(frame);
    }

    private static IReadOnlyList<MethodBase> GetCalledMethods(MethodInfo method)
    {
        var body = method.GetMethodBody();
        Assert.NotNull(body);

        var il = body!.GetILAsByteArray();
        Assert.NotNull(il);

        var module = method.Module;
        var called = new List<MethodBase>();
        var index = 0;
        while (index < il!.Length)
        {
            var opCode = ReadOpCode(il, ref index);
            switch (opCode.OperandType)
            {
                case OperandType.InlineMethod:
                {
                    var metadataToken = BitConverter.ToInt32(il, index);
                    index += sizeof(int);
                    called.Add(module.ResolveMethod(metadataToken)!);
                    break;
                }
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    index += 1;
                    break;
                case OperandType.InlineVar:
                    index += 2;
                    break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    index += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    index += 8;
                    break;
                case OperandType.ShortInlineR:
                    index += 4;
                    break;
                case OperandType.InlineSwitch:
                {
                    var count = BitConverter.ToInt32(il, index);
                    index += sizeof(int) + (count * sizeof(int));
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported operand type: {opCode.OperandType}");
            }
        }

        return called;
    }

    private static OpCode ReadOpCode(byte[] il, ref int index)
    {
        var value = il[index++];
        if (value != 0xFE)
            return SingleByteOpCodes[value];

        return MultiByteOpCodes[il[index++]];
    }

    private static readonly OpCode[] SingleByteOpCodes = BuildOpCodeMap(multibyte: false);
    private static readonly OpCode[] MultiByteOpCodes = BuildOpCodeMap(multibyte: true);

    private static OpCode[] BuildOpCodeMap(bool multibyte)
    {
        var opCodes = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
                continue;

            var value = (ushort)opCode.Value;
            if (multibyte)
            {
                if ((value >> 8) == 0xFE)
                    opCodes[value & 0xFF] = opCode;
            }
            else if ((value >> 8) == 0)
            {
                opCodes[value & 0xFF] = opCode;
            }
        }

        return opCodes;
    }

    private sealed class ReconnectableEmptyFrameTransport : ITransport
    {
        public int ConnectCount { get; private set; }
        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            ConnectCount++;
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            return ValueTask.FromResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class IdleSessionTransport : ITransport
    {
        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            return ValueTask.CompletedTask;
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return TransportFrame.Empty;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSendSessionTransport : ITransport
    {
        public bool IsConnected { get; private set; } = true;

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            throw new InvalidOperationException("send failed");
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            return ValueTask.FromResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BurstConnectionAcceptor : IRpcConnectionAcceptor
    {
        private readonly Queue<RpcAcceptedConnection> _connections;
        private int _activeTransportCount;
        private int _disposedTransportCount;

        public BurstConnectionAcceptor(int count)
        {
            _connections = new Queue<RpcAcceptedConnection>(count);
            for (var i = 0; i < count; i++)
            {
                var transport = new DisposableConnectionTransport(this);
                _connections.Enqueue(new RpcAcceptedConnection(transport, $"test-{i}"));
            }
            _activeTransportCount = count;
        }

        public int ActiveTransportCount => Volatile.Read(ref _activeTransportCount);

        public int DisposedTransportCount => Volatile.Read(ref _disposedTransportCount);

        public string ListenAddress => "test://burst";

        public ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (_connections.Count == 0)
                return ValueTask.FromCanceled<RpcAcceptedConnection>(ct.IsCancellationRequested ? ct : new CancellationToken(true));

            return ValueTask.FromResult(_connections.Dequeue());
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private sealed class DisposableConnectionTransport : ITransport
        {
            private readonly BurstConnectionAcceptor _owner;
            private int _disposed;

            public DisposableConnectionTransport(BurstConnectionAcceptor owner)
            {
                _owner = owner;
            }

            public bool IsConnected => Volatile.Read(ref _disposed) == 0;

            public ValueTask ConnectAsync(CancellationToken ct = default)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(TransportFrame.Empty);
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return ValueTask.CompletedTask;

                Interlocked.Decrement(ref _owner._activeTransportCount);
                Interlocked.Increment(ref _owner._disposedTransportCount);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class StaticConnectionAcceptor : IRpcConnectionAcceptor
    {
        private readonly Queue<RpcAcceptedConnection> _connections;

        public StaticConnectionAcceptor(params RpcAcceptedConnection[] connections)
        {
            _connections = new Queue<RpcAcceptedConnection>(connections);
        }

        public string ListenAddress => "test://static";

        public ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (_connections.Count == 0)
                return ValueTask.FromCanceled<RpcAcceptedConnection>(ct.IsCancellationRequested ? ct : new CancellationToken(true));

            return ValueTask.FromResult(_connections.Dequeue());
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ToggleableConnectionTransport : ITransport
    {
        private int _disposeCount;

        public ToggleableConnectionTransport(bool initiallyConnected)
        {
            IsConnected = initiallyConnected;
        }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            return ValueTask.FromResult(TransportFrame.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        public sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
