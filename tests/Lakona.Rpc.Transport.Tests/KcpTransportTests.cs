using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Text;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Kcp;

namespace Lakona.Rpc.Transport.Tests;

public class KcpTransportTests
{
    [Fact]
    public async Task ClientAndServerTransports_UseDerivedFrameBudgets()
    {
        await using var client = new KcpTransport(IPAddress.Loopback.ToString(), 1);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        await using var server = new KcpServerTransport(
            socket,
            new IPEndPoint(IPAddress.Loopback, 1),
            conv: 1);

        FrameLimitTestAssertions.UsesDerivedLengthPrefixedBudgets(client);
        FrameLimitTestAssertions.UsesDerivedLengthPrefixedBudgets(server);
    }

    [Fact]
    public void HandshakeCodecRoundTripsRequestAckAndReject()
    {
        const uint conversationId = 73;
        const int sessionPort = 23001;

        var request = KcpHandshake.CreateRequest(conversationId);
        var ack = KcpHandshake.CreateAck(conversationId, sessionPort);
        var reject = KcpHandshake.CreateReject(conversationId, KcpHandshakeRejectionReason.ServerBusy);

        Assert.True(KcpHandshake.TryParseRequest(request, out var parsedConversationId));
        Assert.Equal(conversationId, parsedConversationId);
        Assert.True(KcpHandshake.TryParseAck(ack, conversationId, out var parsedSessionPort));
        Assert.Equal(sessionPort, parsedSessionPort);
        Assert.False(KcpHandshake.TryParseAck(ack, conversationId + 1, out _));
        Assert.True(KcpHandshake.TryParseReject(
            reject,
            conversationId,
            out var parsedRejectionReason));
        Assert.Equal(KcpHandshakeRejectionReason.ServerBusy, parsedRejectionReason);
        Assert.False(KcpHandshake.TryParseReject(reject, conversationId + 1, out _));
        Assert.Equal(
            new byte[] { 0x55, 0x4E, 0x41, 0x4B, 0x49, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 },
            reject);
    }

    [Fact]
    public async Task Update_scheduler_honors_the_returned_next_update_time()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var firstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updateCount = 0;
        using var registration = KcpUpdateScheduler.Register(
            now =>
            {
                Interlocked.Increment(ref updateCount);
                firstUpdate.TrySetResult();
                return now.AddMinutes(1);
            },
            static exception => throw new InvalidOperationException("KCP update failed.", exception));

        await WithTimeout(firstUpdate.Task, timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);

        Assert.Equal(1, Volatile.Read(ref updateCount));
    }

    [Fact]
    public async Task Update_scheduler_honors_an_earlier_rescheduled_deadline()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var firstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updateCount = 0;
        using var registration = KcpUpdateScheduler.Register(
            now =>
            {
                var count = Interlocked.Increment(ref updateCount);
                (count == 1 ? firstUpdate : secondUpdate).TrySetResult();
                return now.AddMinutes(1);
            },
            static exception => throw new InvalidOperationException("KCP update failed.", exception));
        await WithTimeout(firstUpdate.Task, timeout.Token);

        registration.Reschedule(DateTimeOffset.UtcNow);

        await WithTimeout(secondUpdate.Task, timeout.Token);
        Assert.Equal(2, Volatile.Read(ref updateCount));
    }

    [Fact]
    public async Task Update_scheduler_isolates_a_blocked_registration()
    {
        using var timeout = new CancellationTokenSource();
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var blocked = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var fastTicks = 0;
        using var slow = KcpUpdateScheduler.Register(now =>
        {
            entered.Set();
            blocked.Wait(timeout.Token);
            return now;
        }, static exception => throw new InvalidOperationException("Slow KCP update failed.", exception));
        using var fast = KcpUpdateScheduler.Register(
            now =>
            {
                Interlocked.Increment(ref fastTicks);
                return now;
            },
            static exception => throw new InvalidOperationException("Fast KCP update failed.", exception));

        Assert.True(entered.Wait(TimeSpan.FromSeconds(1), timeout.Token));
        await WithTimeout(
            WaitUntilAsync(() => Volatile.Read(ref fastTicks) >= 3, timeout.Token),
            timeout.Token);
        blocked.Set();

        Assert.True(Volatile.Read(ref fastTicks) >= 3);
    }

    [Fact]
    public async Task KcpTransport_HandshakeHonorsCancellation()
    {
        using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)serverSocket.LocalEndPoint!;
        var handshakeObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            var buffer = new byte[32];
            EndPoint any = new IPEndPoint(IPAddress.Any, 0);
            var received = await serverSocket.ReceiveFromAsync(buffer, SocketFlags.None, any);
            if (received.ReceivedBytes > 0)
                handshakeObserved.TrySetResult();
        });

        await using var client = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        using var observeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ConnectAsync(cts.Token).AsTask());
        await WithTimeout(handshakeObserved.Task, observeCts.Token);
        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task KcpTransport_HandshakeRetriesAfterDroppedRequest()
    {
        const uint conversationId = 73;
        using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)serverSocket.LocalEndPoint!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var server = Task.Run(async () =>
        {
            var buffer = new byte[32];
            EndPoint any = new IPEndPoint(IPAddress.Any, 0);
            for (var requestNumber = 1; requestNumber <= 2; requestNumber++)
            {
                var received = await serverSocket.ReceiveFromAsync(
                    buffer,
                    SocketFlags.None,
                    any,
                    timeout.Token);
                Assert.True(KcpHandshake.TryParseRequest(
                    buffer.AsSpan(0, received.ReceivedBytes),
                    out var receivedConversationId));
                Assert.Equal(conversationId, receivedConversationId);

                if (requestNumber == 2)
                {
                    await serverSocket.SendToAsync(
                        KcpHandshake.CreateAck(conversationId, serverEndPoint.Port),
                        SocketFlags.None,
                        received.RemoteEndPoint,
                        timeout.Token);
                }
            }
        }, timeout.Token);

        await using var client = new KcpTransport(
            IPAddress.Loopback.ToString(),
            serverEndPoint.Port,
            conversationId,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(40));

        await client.ConnectAsync(timeout.Token);
        await server;
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task KcpTransport_SilentListenerTimesOutAfterBoundedRetries()
    {
        using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)serverSocket.LocalEndPoint!;
        using var receiveCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var requestCount = 0;
        var receiver = Task.Run(async () =>
        {
            var buffer = new byte[32];
            EndPoint any = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                while (true)
                {
                    await serverSocket.ReceiveFromAsync(
                        buffer,
                        SocketFlags.None,
                        any,
                        receiveCancellation.Token);
                    Interlocked.Increment(ref requestCount);
                }
            }
            catch (OperationCanceledException) when (receiveCancellation.IsCancellationRequested)
            {
            }
        }, receiveCancellation.Token);

        await using var client = new KcpTransport(
            IPAddress.Loopback.ToString(),
            serverEndPoint.Port,
            conversationId: 73,
            connectTimeout: TimeSpan.FromMilliseconds(180),
            handshakeRetryInterval: TimeSpan.FromMilliseconds(40));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => client.ConnectAsync().AsTask());
        receiveCancellation.Cancel();
        await receiver;

        Assert.Contains("00:00:00.180", exception.Message, StringComparison.Ordinal);
        Assert.True(Volatile.Read(ref requestCount) >= 2);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task KcpListener_RejectsConnectionWhenPendingCapacityIsFull()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var listener = new KcpListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            maxPendingAcceptedConnections: 1);
        var serverEndPoint = (IPEndPoint)listener.LocalEndPoint!;

        await using var firstClient = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await firstClient.ConnectAsync(timeout.Token);

        await using var rejectedClient = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        var exception = await Assert.ThrowsAsync<KcpConnectionRejectedException>(
            () => rejectedClient.ConnectAsync(timeout.Token).AsTask());

        Assert.Contains("busy", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(rejectedClient.IsConnected);

        var accepted = await listener.AcceptAsync(timeout.Token);
        await accepted.Transport.DisposeAsync();
    }

    [Fact]
    public async Task KcpTransport_Roundtrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var listener = new KcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)listener.LocalEndPoint!;
        var acceptTask = listener.AcceptAsync(cts.Token).AsTask();

        await using var client = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await client.ConnectAsync(cts.Token);

        var accepted = await WithTimeout(acceptTask, cts.Token);
        await using var serverTransport = accepted.Transport;

        var payload = Encoding.UTF8.GetBytes("ping-kcp");
        await client.SendFrameAsync(payload, cts.Token);
        var serverReceived = await WithTimeout(serverTransport.ReceiveFrameAsync(cts.Token), cts.Token);
        Assert.Equal(payload, serverReceived.ToArray());

        var reply = Encoding.UTF8.GetBytes("pong-kcp");
        await serverTransport.SendFrameAsync(reply, cts.Token);
        var clientReceived = await WithTimeout(client.ReceiveFrameAsync(cts.Token), cts.Token);
        Assert.Equal(reply, clientReceived.ToArray());
    }

    [Fact]
    public async Task KcpListener_SameEndpointConversationsAreIndependent()
    {
        const uint firstConversationId = 41;
        const uint secondConversationId = 82;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var listener = new KcpListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            maxPendingAcceptedConnections: 2);
        var serverEndPoint = (IPEndPoint)listener.LocalEndPoint!;

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var packetBuffer = new byte[2048];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);

        socket.SendTo(CreateHandshakeRequest(firstConversationId), serverEndPoint);
        var firstAck = await socket.ReceiveFromAsync(packetBuffer, SocketFlags.None, any, timeout.Token);
        Assert.True(KcpHandshake.TryParseAck(
            packetBuffer.AsSpan(0, firstAck.ReceivedBytes),
            firstConversationId,
            out _));
        var firstAccepted = await listener.AcceptAsync(timeout.Token);
        await using var firstServerTransport = firstAccepted.Transport;

        socket.SendTo(CreateHandshakeRequest(firstConversationId), serverEndPoint);
        var duplicateAck = await socket.ReceiveFromAsync(packetBuffer, SocketFlags.None, any, timeout.Token);
        Assert.True(KcpHandshake.TryParseAck(
            packetBuffer.AsSpan(0, duplicateAck.ReceivedBytes),
            firstConversationId,
            out _));
        using (var duplicateCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => listener.AcceptAsync(duplicateCancellation.Token).AsTask());
        }

        socket.SendTo(CreateHandshakeRequest(secondConversationId), serverEndPoint);
        var secondAck = await socket.ReceiveFromAsync(packetBuffer, SocketFlags.None, any, timeout.Token);
        Assert.True(KcpHandshake.TryParseAck(
            packetBuffer.AsSpan(0, secondAck.ReceivedBytes),
            secondConversationId,
            out _));
        var secondAccepted = await listener.AcceptAsync(timeout.Token);
        await using var secondServerTransport = secondAccepted.Transport;

        Assert.Equal(firstConversationId, firstAccepted.ConversationId);
        Assert.Equal(secondConversationId, secondAccepted.ConversationId);
        Assert.Equal(firstAccepted.RemoteEndPoint, secondAccepted.RemoteEndPoint);

        var emittedConversations = new ConcurrentBag<uint>();
        var peer = new SocketKcpPeer(socket, serverEndPoint, packet =>
        {
            Assert.True(KcpPacket.TryReadConversationId(packet.Span, out var conversationId));
            emittedConversations.Add(conversationId);
        });
        using var firstSender = new SimpleSegManager.Kcp(firstConversationId, peer, peer);
        using var secondSender = new SimpleSegManager.Kcp(secondConversationId, peer, peer);
        var firstPayload = new byte[] { 0x41, 0x01 };
        var secondPayload = new byte[] { 0x82, 0x02 };

        SendKcpFrame(firstSender, firstPayload);
        SendKcpFrame(secondSender, secondPayload);
        Assert.Contains(firstConversationId, emittedConversations);
        Assert.Contains(secondConversationId, emittedConversations);

        using var firstFrame = await firstServerTransport.ReceiveFrameAsync(timeout.Token);
        using var secondFrame = await secondServerTransport.ReceiveFrameAsync(timeout.Token);
        Assert.Equal(firstPayload, firstFrame.ToArray());
        Assert.Equal(secondPayload, secondFrame.ToArray());

        await secondServerTransport.DisposeAsync();
        socket.SendTo(CreateHandshakeRequest(firstConversationId), serverEndPoint);
        while (true)
        {
            var received = await socket.ReceiveFromAsync(packetBuffer, SocketFlags.None, any, timeout.Token);
            if (KcpHandshake.TryParseAck(
                    packetBuffer.AsSpan(0, received.ReceivedBytes),
                    firstConversationId,
                    out _))
            {
                break;
            }
        }

        using (var survivingCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => listener.AcceptAsync(survivingCancellation.Token).AsTask());
        }

        Assert.True(firstServerTransport.IsConnected);
    }

    [Fact]
    public async Task KcpListener_HandshakeCapacityPressure_DoesNotBlockExistingConnection()
    {
        const int handshakeBurstSize = 32;
        var payload = new byte[] { 0xCA, 0xFE };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var listener = new KcpListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            maxPendingAcceptedConnections: 1);
        var serverEndPoint = (IPEndPoint)listener.LocalEndPoint!;
        var acceptTask = listener.AcceptAsync(timeout.Token).AsTask();

        await using var establishedClient = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await establishedClient.ConnectAsync(timeout.Token);
        var accepted = await WithTimeout(acceptTask, timeout.Token);
        await using var establishedServer = accepted.Transport;

        var handshakeClients = new List<Socket>();
        try
        {
            for (var i = 0; i < handshakeBurstSize; i++)
            {
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                handshakeClients.Add(client);
                client.SendTo(CreateHandshakeRequest(unchecked((uint)(100 + i))), serverEndPoint);
            }

            await establishedClient.SendFrameAsync(payload, timeout.Token);
            using var frame = await WithTimeout(
                establishedServer.ReceiveFrameAsync(timeout.Token),
                timeout.Token);

            Assert.Equal(payload, frame.ToArray());
        }
        finally
        {
            foreach (var client in handshakeClients)
                client.Dispose();
        }
    }

    [Fact]
    public async Task KcpServerTransport_UpdateFailure_IsTerminal()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var remote = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        remote.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        await using var transport = new KcpServerTransport(socket, remote.LocalEndPoint!, conv: 73);
        await transport.ConnectAsync();
        await transport.SendFrameAsync(new byte[] { 0x01 });
        socket.Dispose();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await transport.ReceiveFrameAsync(timeout.Token)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(3), timeout.Token));

        Assert.True(
            failure is ObjectDisposedException or SocketException,
            $"Unexpected terminal KCP failure: {failure}");
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task KcpServerTransport_SlowConsumer_ClosesWindowWithoutBlockingOtherConnections()
    {
        const uint conversationId = 73;
        const int frameCount = 256;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var listener = new KcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)listener.LocalEndPoint!;
        var acceptTask = listener.AcceptAsync(cts.Token).AsTask();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.SendTo(CreateHandshakeRequest(conversationId), serverEndPoint);

        var packetBuffer = new byte[2048];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        var handshakeAck = await socket.ReceiveFromAsync(packetBuffer, SocketFlags.None, any, cts.Token);
        Assert.Equal(12, handshakeAck.ReceivedBytes);
        Assert.True(packetBuffer.AsSpan(0, 4).SequenceEqual("UACK"u8));

        var accepted = await acceptTask;
        await using var serverTransport = accepted.Transport;
        var peer = new SocketKcpPeer(socket, serverEndPoint);
        using var sender = new SimpleSegManager.Kcp(conversationId, peer, peer);

        for (var i = 0; i < frameCount; i++)
        {
            using var packed = LengthPrefix.Pack([unchecked((byte)i)]);
            sender.Send(packed.Span, null!);
        }

        ushort minimumAdvertisedWindow = ushort.MaxValue;
        var observationDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < observationDeadline && minimumAdvertisedWindow != 0)
        {
            var now = DateTimeOffset.UtcNow;
            sender.Update(in now);

            while (socket.Available > 0)
            {
                var received = socket.ReceiveFrom(packetBuffer, SocketFlags.None, ref any);
                minimumAdvertisedWindow = Math.Min(
                    minimumAdvertisedWindow,
                    ReadMinimumAdvertisedWindow(packetBuffer.AsSpan(0, received)));
                sender.Input(packetBuffer.AsSpan(0, received));
            }

            await Task.Delay(2);
        }

        Assert.True(
            minimumAdvertisedWindow == 0,
            $"Expected the slow consumer to advertise a closed KCP receive window, but the minimum was {minimumAdvertisedWindow}.");

        var secondAcceptTask = listener.AcceptAsync(cts.Token).AsTask();
        await using var secondClient = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await secondClient.ConnectAsync(cts.Token);
        var secondAccepted = await secondAcceptTask;
        await using var secondServerTransport = secondAccepted.Transport;

        var secondPayload = new byte[] { 0xCA, 0xFE };
        await secondClient.SendFrameAsync(secondPayload, cts.Token);
        using var secondFrame = await secondServerTransport.ReceiveFrameAsync(cts.Token);
        Assert.Equal(secondPayload, secondFrame.ToArray());
    }

    [Fact]
    public async Task KcpServerTransport_DelayedConsumer_ReceivesFramesInOrder()
    {
        const int frameCount = 32;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var listener = new KcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)listener.LocalEndPoint!;
        var acceptTask = listener.AcceptAsync(cts.Token).AsTask();

        await using var client = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await client.ConnectAsync(cts.Token);
        var accepted = await acceptTask;
        await using var serverTransport = accepted.Transport;
        using var clientPumpCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var clientPump = client.ReceiveFrameAsync(clientPumpCts.Token).AsTask();

        for (var i = 0; i < frameCount; i++)
            await client.SendFrameAsync(new byte[] { unchecked((byte)i) }, cts.Token);

        await Task.Delay(100, cts.Token);

        for (var i = 0; i < frameCount; i++)
        {
            TransportFrame frame;
            try
            {
                frame = await serverTransport.ReceiveFrameAsync(cts.Token);
            }
            catch (OperationCanceledException exception)
            {
                throw new InvalidOperationException($"Timed out while receiving delayed frame {i} of {frameCount}.", exception);
            }

            using (frame)
            {
                Assert.Equal(new byte[] { unchecked((byte)i) }, frame.ToArray());
            }
        }

        clientPumpCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clientPump);
    }

    [Fact]
    public async Task KcpServerTransport_ReceiveFailure_DoesNotStopListenerAcceptingNewConnections()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var listener = new KcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)listener.LocalEndPoint!;
        var acceptFirstTask = listener.AcceptAsync(cts.Token).AsTask();

        await using var firstClient = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await firstClient.ConnectAsync(cts.Token);

        var acceptedFirst = await WithTimeout(acceptFirstTask, cts.Token);
        await using var firstTransport = acceptedFirst.Transport;

        ForceFrameAccumulatorOverflowOnNextAppend(firstTransport);
        await firstClient.SendFrameAsync(new byte[] { 0x01 }, cts.Token);
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => firstTransport.ReceiveFrameAsync(cts.Token).AsTask());
        await firstTransport.DisposeAsync();

        using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, acceptCts.Token);
        var acceptSecondTask = listener.AcceptAsync(linkedCts.Token).AsTask();

        await using var secondClient = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await secondClient.ConnectAsync(linkedCts.Token);

        var acceptedSecond = await acceptSecondTask;
        await acceptedSecond.Transport.DisposeAsync();
    }

    [Fact]
    public async Task KcpListener_DoesNotBufferBeyondDefaultPendingLimit()
    {
        const int expectedMaxPendingConnections = 128;
        const int burstConnectionCount = expectedMaxPendingConnections + 12;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var listener = new KcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)listener.LocalEndPoint!;
        var clients = new List<Socket>();
        var acceptedConnections = new List<KcpAcceptResult>();

        try
        {
            for (var i = 0; i < burstConnectionCount; i++)
            {
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                clients.Add(client);

                var conv = unchecked((uint)(i + 1));
                var handshake = CreateHandshakeRequest(conv);
                client.SendTo(handshake, serverEndPoint);
            }

            await Task.Delay(250, cts.Token);

            while (true)
            {
                using var acceptCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, acceptCts.Token);

                try
                {
                    acceptedConnections.Add(await listener.AcceptAsync(linkedCts.Token));
                }
                catch (OperationCanceledException) when (acceptCts.IsCancellationRequested)
                {
                    break;
                }
            }

            Assert.True(
                acceptedConnections.Count <= expectedMaxPendingConnections,
                $"Accepted {acceptedConnections.Count} pending KCP connections before draining, expected at most {expectedMaxPendingConnections}.");
        }
        finally
        {
            foreach (var accepted in acceptedConnections)
                await accepted.Transport.DisposeAsync();

            foreach (var client in clients)
                client.Dispose();
        }
    }

    [Fact]
    public void KcpListener_ReceiveLoop_DoesNotCallToStringForSessionLookup()
    {
        var receiveLoop = typeof(KcpListener).GetMethod(
            "ReceiveLoopAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(receiveLoop);

        var stateMachineAttribute = receiveLoop!.GetCustomAttribute<AsyncStateMachineAttribute>();
        Assert.NotNull(stateMachineAttribute);

        var moveNext = stateMachineAttribute!.StateMachineType.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(moveNext);

        var calledMethods = GetCalledMethods(moveNext!);
        Assert.DoesNotContain(
            calledMethods,
            method => method is MethodInfo methodInfo &&
                      methodInfo.Name == nameof(object.ToString) &&
                      methodInfo.ReturnType == typeof(string));
    }

    [Fact]
    public void KcpServerTransport_ReceiveFrameAsync_DoesNotCreateLinkedCancellationTokenSource()
    {
        var receiveFrameAsync = typeof(KcpServerTransport).GetMethod(
            nameof(KcpServerTransport.ReceiveFrameAsync),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(receiveFrameAsync);

        var stateMachineAttribute = receiveFrameAsync!.GetCustomAttribute<AsyncStateMachineAttribute>();
        Assert.NotNull(stateMachineAttribute);

        var moveNext = stateMachineAttribute!.StateMachineType.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(moveNext);

        var calledMethods = GetCalledMethods(moveNext!);
        Assert.DoesNotContain(
            calledMethods,
            method => method is MethodInfo methodInfo &&
                      methodInfo.DeclaringType == typeof(CancellationTokenSource) &&
                      methodInfo.Name == nameof(CancellationTokenSource.CreateLinkedTokenSource));
    }

    [Fact]
    public void KcpServerTransport_Output_DoesNotCallToArray()
    {
        var output = typeof(KcpServerTransport).GetMethod(
            "System.Net.Sockets.Kcp.IKcpCallback.Output",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(output);

        var calledMethods = GetCalledMethods(output!);
        Assert.DoesNotContain(
            calledMethods,
            method => method is MethodInfo methodInfo &&
                      methodInfo.Name == "ToArray" &&
                      methodInfo.ReturnType == typeof(byte[]));
    }

    [Fact]
    public async Task KcpServerTransport_DisposeAsync_CanBeCalledMultipleTimes()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        await using var transport = new KcpServerTransport(
            socket,
            new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)socket.LocalEndPoint!).Port),
            conv: 1);

        await transport.DisposeAsync();
        await transport.DisposeAsync();
    }

    private static async Task WithTimeout(Task task, CancellationToken ct)
    {
        var delay = Task.Delay(Timeout.InfiniteTimeSpan, ct);
        var completed = await Task.WhenAny(task, delay);
        if (completed != task)
            throw new TimeoutException("Operation timed out.");

        await task;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
            await Task.Delay(10, ct);
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, CancellationToken ct)
    {
        var delay = Task.Delay(Timeout.InfiniteTimeSpan, ct);
        var completed = await Task.WhenAny(task, delay);
        if (completed != task)
            throw new TimeoutException("Operation timed out.");

        return await task;
    }

    private static async ValueTask<T> WithTimeout<T>(ValueTask<T> task, CancellationToken ct)
    {
        return await WithTimeout(task.AsTask(), ct);
    }

    private static byte[] CreateHandshakeRequest(uint conv)
    {
        var buffer = new byte[8];
        "UKCP"u8.CopyTo(buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), conv);
        return buffer;
    }

    private static void SendKcpFrame(SimpleSegManager.Kcp sender, ReadOnlySpan<byte> payload)
    {
        var now = DateTimeOffset.UtcNow;
        sender.Update(in now);
        using var packed = LengthPrefix.Pack(payload);
        sender.Send(packed.Span, null!);
        now = now.AddMilliseconds(100);
        sender.Update(in now);
    }

    private static ushort ReadMinimumAdvertisedWindow(ReadOnlySpan<byte> packet)
    {
        const int headerLength = 24;
        var minimum = ushort.MaxValue;
        var offset = 0;
        while (offset + headerLength <= packet.Length)
        {
            minimum = Math.Min(
                minimum,
                BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(offset + 6, sizeof(ushort))));
            var payloadLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                packet.Slice(offset + 20, sizeof(uint))));
            offset = checked(offset + headerLength + payloadLength);
        }

        return minimum;
    }

    private sealed class SocketKcpPeer : IKcpCallback, IRentable
    {
        private readonly Socket _socket;
        private readonly EndPoint _remote;
        private readonly Action<ReadOnlyMemory<byte>>? _onOutput;

        public SocketKcpPeer(
            Socket socket,
            EndPoint remote,
            Action<ReadOnlyMemory<byte>>? onOutput = null)
        {
            _socket = socket;
            _remote = remote;
            _onOutput = onOutput;
        }

        void IKcpCallback.Output(IMemoryOwner<byte> buffer, int avalidLength)
        {
            try
            {
                var packet = buffer.Memory.Slice(0, avalidLength);
                _onOutput?.Invoke(packet);
                _socket.SendTo(packet.Span, SocketFlags.None, _remote);
            }
            finally
            {
                buffer.Dispose();
            }
        }

        IMemoryOwner<byte> IRentable.RentBuffer(int size)
        {
            return MemoryPool<byte>.Shared.Rent(size);
        }
    }

    private static void ForceFrameAccumulatorOverflowOnNextAppend(KcpServerTransport transport)
    {
        var accumulatorField = typeof(KcpServerTransport).GetField("_accumulator", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(accumulatorField);

        var accumulator = accumulatorField!.GetValue(transport);
        Assert.NotNull(accumulator);

        var countField = accumulator!.GetType().GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(countField);
        countField!.SetValue(accumulator, RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize);
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

}
