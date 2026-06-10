using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Kcp
{
    public sealed class KcpListener : IAsyncDisposable
    {
        private readonly Channel<KcpAcceptResult> _accepted;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<RemoteSessionKey, SessionRecord> _sessions = new();
        private readonly Socket _socket;
        private readonly Task _receiveLoop;
        private readonly int _maxPendingAcceptedConnections;
        private readonly KcpHandshakeAdmission? _admission;
        private int _pendingAcceptedConnections;

        public KcpListener(int port)
            : this(port, "127.0.0.1")
        {
        }

        public KcpListener(int port, string host)
            : this(port, host, RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections)
        {
        }

        public KcpListener(int port, string host, int maxPendingAcceptedConnections)
            : this(ResolveEndPoint(host, port), maxPendingAcceptedConnections)
        {
        }

        public KcpListener(int port, string host, int maxPendingAcceptedConnections, KcpHandshakeAdmission? admission)
            : this(ResolveEndPoint(host, port), maxPendingAcceptedConnections, admission)
        {
        }

        public KcpListener(int port, int maxPendingAcceptedConnections)
            : this(port, "127.0.0.1", maxPendingAcceptedConnections)
        {
        }

        public KcpListener(int port, int maxPendingAcceptedConnections, KcpHandshakeAdmission? admission)
            : this(port, "127.0.0.1", maxPendingAcceptedConnections, admission)
        {
        }

        public KcpListener(IPEndPoint endPoint)
            : this(endPoint, RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections)
        {
        }

        public KcpListener(IPEndPoint endPoint, int maxPendingAcceptedConnections)
            : this(endPoint, maxPendingAcceptedConnections, admission: null)
        {
        }

        public KcpListener(IPEndPoint endPoint, int maxPendingAcceptedConnections, KcpHandshakeAdmission? admission)
        {
            if (endPoint is null)
                throw new ArgumentNullException(nameof(endPoint));
            if (maxPendingAcceptedConnections <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maxPendingAcceptedConnections),
                    "Pending accepted connection limit must be positive.");

            _maxPendingAcceptedConnections = maxPendingAcceptedConnections;
            _admission = admission;
            _accepted = Channel.CreateBounded<KcpAcceptResult>(new BoundedChannelOptions(maxPendingAcceptedConnections)
            {
                SingleReader = false,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
            _socket = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(endPoint);
            _receiveLoop = Task.Run(ReceiveLoopAsync);
        }

        private static IPEndPoint ResolveEndPoint(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host is required.", nameof(host));

            // Literal IP addresses parse directly without touching DNS.
            if (IPAddress.TryParse(host, out var parsed))
                return new IPEndPoint(parsed, port);

            // Resolve DNS hostnames. Prefer IPv4 because the socket is
            // AddressFamily.InterNetwork (UDP socket for KCP).
            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(host);
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to resolve host '{host}': {ex.Message}", ex);
            }

            if (addresses.Length == 0)
                throw new InvalidOperationException(
                    $"No IP addresses found for host '{host}'.");

            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                          ?? addresses[0];
            return new IPEndPoint(address, port);
        }

        public EndPoint? LocalEndPoint => _socket.LocalEndPoint;

        public async ValueTask<KcpAcceptResult> AcceptAsync(CancellationToken ct = default)
        {
            while (true)
            {
                var accepted = await _accepted.Reader.ReadAsync(ct).ConfigureAwait(false);
                ReleasePendingSlot();
                if (accepted.Transport.IsConnected)
                    return accepted;

                await accepted.Transport.DisposeAsync().ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _accepted.Writer.TryComplete();
            _socket.Dispose();

            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            while (_accepted.Reader.TryRead(out _))
                ReleasePendingSlot();

            var sessions = _sessions.Values.Select(record => record.Transport).Distinct().ToArray();
            _sessions.Clear();
            foreach (var transport in sessions)
                await transport.DisposeAsync().ConfigureAwait(false);

            _cts.Dispose();
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[2048];
            EndPoint any = new IPEndPoint(
                _socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
            var localPort = ((IPEndPoint)_socket.LocalEndPoint!).Port;

            while (!_cts.IsCancellationRequested)
            {
                SocketReceiveFromResult received;
#if NET8_0_OR_GREATER
                try
                {
                    received = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, any, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
#else
                try
                {
                    received = await _socket.ReceiveFromAsync(new ArraySegment<byte>(buffer), SocketFlags.None, any).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
#endif
                if (received.RemoteEndPoint is not IPEndPoint remoteEndPoint)
                    continue;

                var packet = buffer.AsMemory(0, received.ReceivedBytes);
                var key = new RemoteSessionKey(remoteEndPoint);
                if (!KcpHandshake.TryParseRequest(packet.Span, out var conv))
                {
                    if (_sessions.TryGetValue(key, out var existingSession))
                    {
                        try
                        {
                            existingSession.Transport.ProcessDatagram(packet.Span);
                        }
                        catch when (!_cts.IsCancellationRequested)
                        {
                            await DisposeSessionAsync(key).ConfigureAwait(false);
                        }
                    }

                    continue;
                }

                if (_sessions.TryGetValue(key, out var existing))
                {
                    var ack = KcpHandshake.CreateAck(existing.ConversationId, localPort);
#if NET8_0_OR_GREATER
                    await _socket.SendToAsync(ack, SocketFlags.None, remoteEndPoint, _cts.Token).ConfigureAwait(false);
#else
                    await _socket.SendToAsync(new ArraySegment<byte>(ack), SocketFlags.None, remoteEndPoint).ConfigureAwait(false);
#endif
                    continue;
                }

                if (_admission is not null && !await _admission(conv, remoteEndPoint, _cts.Token).ConfigureAwait(false))
                    continue;

                if (!TryAcquirePendingSlot())
                    continue;

                KcpServerTransport? transport = null;
                try
                {
                    transport = new KcpServerTransport(
                        _socket,
                        remoteEndPoint,
                        conv,
                        onDispose: () => _sessions.TryRemove(key, out _));
                    await transport.ConnectAsync(_cts.Token).ConfigureAwait(false);
                    var sessionAck = KcpHandshake.CreateAck(conv, localPort);
#if NET8_0_OR_GREATER
                    await _socket.SendToAsync(sessionAck, SocketFlags.None, remoteEndPoint, _cts.Token).ConfigureAwait(false);
#else
                    await _socket.SendToAsync(new ArraySegment<byte>(sessionAck), SocketFlags.None, remoteEndPoint).ConfigureAwait(false);
#endif

                    var record = new SessionRecord(conv, transport);
                    _sessions[key] = record;
                    if (!_accepted.Writer.TryWrite(new KcpAcceptResult(transport, remoteEndPoint, conv, localPort)))
                    {
                        ReleasePendingSlot();
                        _sessions.TryRemove(key, out _);
                        await transport.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch
                {
                    ReleasePendingSlot();
                    if (transport is not null)
                        await transport.DisposeAsync().ConfigureAwait(false);

                    if (_cts.IsCancellationRequested)
                        break;
                }

                continue;
            }
        }

        private bool TryAcquirePendingSlot()
        {
            while (true)
            {
                var current = Volatile.Read(ref _pendingAcceptedConnections);
                if (current >= _maxPendingAcceptedConnections)
                    return false;

                if (Interlocked.CompareExchange(ref _pendingAcceptedConnections, current + 1, current) == current)
                    return true;
            }
        }

        private void ReleasePendingSlot()
        {
            Interlocked.Decrement(ref _pendingAcceptedConnections);
        }

        private async ValueTask DisposeSessionAsync(RemoteSessionKey key)
        {
            if (_sessions.TryRemove(key, out var record))
                await record.Transport.DisposeAsync().ConfigureAwait(false);
        }

        private sealed class SessionRecord
        {
            public SessionRecord(uint conversationId, KcpServerTransport transport)
            {
                ConversationId = conversationId;
                Transport = transport;
            }

            public uint ConversationId { get; }
            public KcpServerTransport Transport { get; }
        }

        private readonly record struct RemoteSessionKey(IPAddress Address, int Port)
        {
            public RemoteSessionKey(IPEndPoint endPoint)
                : this(endPoint.Address, endPoint.Port)
            {
            }
        }
    }
}
