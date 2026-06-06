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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cts.Token).AsTask());
        await WithTimeout(handshakeObserved.Task, observeCts.Token);
        Assert.False(client.IsConnected);
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
    public async Task KcpListener_SessionProcessingFailure_DoesNotStopAcceptingNewConnections()
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

        await WithTimeout(WaitUntilAsync(() => !firstTransport.IsConnected, cts.Token), cts.Token);

        using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, acceptCts.Token);
        var acceptSecondTask = listener.AcceptAsync(linkedCts.Token).AsTask();

        await using var secondClient = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await secondClient.ConnectAsync(linkedCts.Token);

        var acceptedSecond = await acceptSecondTask;
        await acceptedSecond.Transport.DisposeAsync();
    }

    [Fact]
    public async Task KcpListener_DoesNotReturnDisposedQueuedConnection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var listener = new KcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndPoint = (IPEndPoint)listener.LocalEndPoint!;

        await using var firstClient = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await firstClient.ConnectAsync(cts.Token);

        var firstTransport = await GetOnlySessionTransportAsync(listener, cts.Token);
        ForceFrameAccumulatorOverflowOnNextAppend(firstTransport);
        await firstClient.SendFrameAsync(new byte[] { 0x01 }, cts.Token);

        await WithTimeout(WaitUntilAsync(() => !firstTransport.IsConnected, cts.Token), cts.Token);

        using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, acceptCts.Token);
        var acceptTask = listener.AcceptAsync(linkedCts.Token).AsTask();

        await using var secondClient = new KcpTransport(IPAddress.Loopback.ToString(), serverEndPoint.Port);
        await secondClient.ConnectAsync(linkedCts.Token);

        var accepted = await acceptTask;
        Assert.True(accepted.Transport.IsConnected);
        await accepted.Transport.DisposeAsync();
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
    public void KcpServerTransport_Source_DoesNotMaterializeOutputMemoryWithToArray()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lakona.Rpc.Transport.Kcp", "Server", "KcpServerTransport.cs"));

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("mem.ToArray()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KcpTransport_Source_NetstandardReceivePaths_DoNotUseUncancellableReceiveFromAsync()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Lakona.Rpc.Transport.Kcp", "Client", "KcpTransport.cs"));

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain(
            "await _socket.ReceiveFromAsync(new ArraySegment<byte>(buffer), SocketFlags.None, _receiveAny).ConfigureAwait(false);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "received = await socket.ReceiveFromAsync(new ArraySegment<byte>(buffer), SocketFlags.None, any).ConfigureAwait(false);",
            source,
            StringComparison.Ordinal);
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

    private static void ForceFrameAccumulatorOverflowOnNextAppend(KcpServerTransport transport)
    {
        var accumulatorField = typeof(KcpServerTransport).GetField("_accumulator", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(accumulatorField);

        var accumulator = accumulatorField!.GetValue(transport);
        Assert.NotNull(accumulator);

        var countField = accumulator!.GetType().GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(countField);
        countField!.SetValue(accumulator, LengthPrefix.DefaultMaxFrameSize);
    }

    private static async Task<KcpServerTransport> GetOnlySessionTransportAsync(KcpListener listener, CancellationToken ct)
    {
        var sessionsField = typeof(KcpListener).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(sessionsField);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var sessions = sessionsField!.GetValue(listener);
            Assert.NotNull(sessions);

            var valuesProperty = sessions!.GetType().GetProperty("Values");
            Assert.NotNull(valuesProperty);
            var values = (System.Collections.IEnumerable?)valuesProperty!.GetValue(sessions);
            Assert.NotNull(values);

            foreach (var record in values!)
            {
                var transportProperty = record!.GetType().GetProperty("Transport");
                Assert.NotNull(transportProperty);
                var transport = (KcpServerTransport?)transportProperty!.GetValue(record);
                if (transport is not null)
                    return transport;
            }

            await Task.Delay(10, ct);
        }
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
