using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;

namespace Lakona.Game.Cluster.Rpc;

/// <summary>
/// Owns the transport, serializer, endpoint validation, and peer negotiation for one cluster RPC channel.
/// </summary>
internal sealed class ClusterRpcChannel
{
    internal const string ProtocolId = "lakona.cluster.memorypack.v2";

    private readonly IClusterRpcTransport _transport;
    private readonly string _protocolId;

    internal ClusterRpcChannel()
        : this(TcpClusterRpcTransport.Default, new MemoryPackRpcSerializer(), ProtocolId)
    {
    }

    internal ClusterRpcChannel(
        IClusterRpcTransport transport,
        IRpcSerializer serializer,
        string protocolId)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _protocolId = protocolId ?? throw new ArgumentNullException(nameof(protocolId));
        ValidateIdentifier(_transport.Scheme, "transport scheme");
        ValidateIdentifier(_protocolId, "serializer protocol id");
    }

    internal string TransportScheme => _transport.Scheme;

    /// <summary>
    /// Gets the fixed MemoryPack serializer used by this channel.
    /// </summary>
    internal IRpcSerializer Serializer { get; }

    /// <summary>
    /// Connects to a peer and verifies its serializer protocol before RPC starts.
    /// </summary>
    internal async ValueTask<ITransport> ConnectAsync(
        RouteLocation target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var endpoint = ValidateEndpoint(ClusterEndpoint.FromRouteLocation(target));
        var connection = await _transport.ConnectAsync(target, endpoint, cancellationToken).ConfigureAwait(false);
        try
        {
            await ClusterRpcProtocolNegotiation.NegotiateClientAsync(
                connection,
                _protocolId,
                cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Starts the local listener and rejects incompatible peers before yielding accepted RPC connections.
    /// </summary>
    internal async ValueTask<IRpcConnectionAcceptor> ListenAsync(
        ClusterEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        endpoint = ValidateEndpoint(endpoint);
        var acceptor = await _transport.ListenAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return new NegotiatingConnectionAcceptor(acceptor, _protocolId);
    }

    /// <summary>
    /// Parses and validates a configured cluster endpoint against the selected transport.
    /// </summary>
    internal ClusterEndpoint ParseEndpoint(string address) => ValidateEndpoint(ClusterEndpoint.Parse(address));

    private ClusterEndpoint ValidateEndpoint(ClusterEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!string.Equals(endpoint.Scheme, _transport.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Configured cluster endpoint uses '{endpoint.Scheme}', but the framework cluster transport uses '{_transport.Scheme}'.");
        }

        return endpoint;
    }

    private static void ValidateIdentifier(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The cluster RPC {kind} must not be empty.");
        }

        if (Encoding.UTF8.GetByteCount(value) > ClusterRpcProtocolNegotiation.MaximumProtocolIdBytes)
        {
            throw new InvalidOperationException(
                $"The cluster RPC {kind} exceeds {ClusterRpcProtocolNegotiation.MaximumProtocolIdBytes} UTF-8 bytes.");
        }
    }

    private sealed class NegotiatingConnectionAcceptor : IRpcConnectionAcceptor
    {
        private readonly IRpcConnectionAcceptor _inner;
        private readonly string _protocolId;

        public NegotiatingConnectionAcceptor(IRpcConnectionAcceptor inner, string protocolId)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _protocolId = protocolId;
        }

        public string ListenAddress => _inner.ListenAddress;

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            var connection = await _inner.AcceptAsync(cancellationToken).ConfigureAwait(false);
            return new RpcAcceptedConnection(
                new ServerNegotiatingTransport(connection.Transport, _protocolId),
                connection.DisplayName,
                connection.RemoteEndPoint);
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class ServerNegotiatingTransport : ITransport
    {
        private readonly ITransport _inner;
        private readonly string _protocolId;
        private readonly object _gate = new();
        private Task? _connectTask;

        public ServerNegotiatingTransport(ITransport inner, string protocolId)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _protocolId = protocolId;
        }

        public bool IsConnected => _inner.IsConnected;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            Task connectTask;
            lock (_gate)
            {
                _connectTask ??= ConnectCoreAsync(cancellationToken);
                connectTask = _connectTask;
            }

            return new ValueTask(connectTask.WaitAsync(cancellationToken));
        }

        public ValueTask SendFrameAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default) =>
            _inner.SendFrameAsync(frame, cancellationToken);

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken cancellationToken = default) =>
            _inner.ReceiveFrameAsync(cancellationToken);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();

        private async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            await _inner.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await ClusterRpcProtocolNegotiation.NegotiateServerAsync(
                _inner,
                _protocolId,
                cancellationToken).ConfigureAwait(false);
        }
    }
}

internal static class ClusterRpcProtocolNegotiation
{
    private const byte Version = 1;
    private const byte Hello = 1;
    private const byte Accepted = 2;
    private const byte Rejected = 3;
    private static readonly byte[] Magic = [(byte)'L', (byte)'C', (byte)'R', (byte)'P'];

    internal const int MaximumProtocolIdBytes = 192;

    public static async ValueTask NegotiateClientAsync(
        ITransport transport,
        string localProtocolId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await transport.SendFrameAsync(Encode(Hello, localProtocolId), cancellationToken).ConfigureAwait(false);
        using var response = await transport.ReceiveFrameAsync(cancellationToken).ConfigureAwait(false);
        var (kind, remoteProtocolId) = Decode(response.Memory.Span);
        if (kind != Accepted || !string.Equals(localProtocolId, remoteProtocolId, StringComparison.Ordinal))
        {
            throw new ClusterRpcProtocolMismatchException(localProtocolId, remoteProtocolId);
        }
    }

    public static async ValueTask NegotiateServerAsync(
        ITransport transport,
        string localProtocolId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        using var request = await transport.ReceiveFrameAsync(cancellationToken).ConfigureAwait(false);
        var (kind, remoteProtocolId) = Decode(request.Memory.Span);
        var accepted = kind == Hello && string.Equals(localProtocolId, remoteProtocolId, StringComparison.Ordinal);
        await transport.SendFrameAsync(
            Encode(accepted ? Accepted : Rejected, localProtocolId),
            cancellationToken).ConfigureAwait(false);
        if (accepted)
        {
            return;
        }

        throw new ClusterRpcProtocolMismatchException(localProtocolId, remoteProtocolId);
    }

    private static byte[] Encode(byte kind, string protocolId)
    {
        var protocolBytes = Encoding.UTF8.GetBytes(protocolId);
        if (protocolBytes.Length > MaximumProtocolIdBytes)
        {
            throw new InvalidOperationException("Cluster RPC protocol id is too long.");
        }

        var frame = new byte[6 + protocolBytes.Length];
        Magic.CopyTo(frame, 0);
        frame[4] = Version;
        frame[5] = kind;
        protocolBytes.CopyTo(frame, 6);
        return frame;
    }

    private static (byte Kind, string ProtocolId) Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 7 || !frame[..4].SequenceEqual(Magic) || frame[4] != Version)
        {
            throw new InvalidOperationException("The remote peer did not send a valid Lakona cluster RPC negotiation frame.");
        }

        var protocolBytes = frame[6..];
        if (protocolBytes.Length > MaximumProtocolIdBytes)
        {
            throw new InvalidOperationException("The remote cluster RPC protocol id is too long.");
        }

        var protocolId = Encoding.UTF8.GetString(protocolBytes);
        if (string.IsNullOrWhiteSpace(protocolId))
        {
            throw new InvalidOperationException("The remote cluster RPC protocol id is empty.");
        }

        return (frame[5], protocolId);
    }
}
