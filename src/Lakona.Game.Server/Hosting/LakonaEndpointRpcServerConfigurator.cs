using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Server;
using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaEndpointRpcServerConfigurator : IRpcServerConfigurator
{
    private readonly LakonaGameEndpointOptions _endpoint;
    private readonly Action<RpcServiceRegistry, IServiceProvider>? _bindServices;

    public LakonaEndpointRpcServerConfigurator(
        LakonaGameEndpointOptions endpoint,
        Action<RpcServiceRegistry, IServiceProvider>? bindServices = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _bindServices = bindServices;
    }

    public string Transport => _endpoint.Transport;

    public void Configure(LakonaGameServerRpcContext context)
    {
        var builder = context.Builder;
        var loggerFactory = context.Services.GetService<ILoggerFactory>();
        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        var runtime = context.Services.GetRequiredService<LakonaEndpointRuntimeRegistry>();
        builder.UseSerializer(runtime.CreateEndpointSerializer(_endpoint));
        builder.UseAcceptor(ct => runtime.CreateAcceptorAsync(_endpoint, ct));
        builder.UseLimits(limits =>
            limits.MaxActiveConnections = _endpoint.ConnectionLimits.MaxActiveConnections);
        var handshakeStates = context.Services.GetService<GameHandshakeConnectionStateRegistry>()
            ?? new GameHandshakeConnectionStateRegistry();
        builder.UseSessionAdmissionGate(new GameHandshakeConnectionAdmissionGate(
            handshakeStates,
            _endpoint.ConnectionLimits,
            loggerFactory?.CreateLogger<GameHandshakeConnectionAdmissionGate>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GameHandshakeConnectionAdmissionGate>.Instance));
        builder.UseSessionRequestGate(new GameHandshakeRpcGate(handshakeStates));
        BindGameFrameworkRpcs(builder.ServiceRegistry, context.Services, handshakeStates);

        foreach (var observer in context.Services.GetServices<IRpcSessionLifecycleObserver>())
        {
            builder.UseSessionLifecycleObserver(observer);
        }

        var catalog = context.Services.GetRequiredService<LakonaRpcServiceCatalog>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var serviceName in _endpoint.RpcServices)
        {
            if (!seen.Add(serviceName))
            {
                throw new InvalidOperationException(
                    $"RPC service '{serviceName}' is configured more than once on endpoint '{_endpoint.Transport}'.");
            }

            if (!catalog.TryGet(serviceName, out var descriptor))
            {
                throw new InvalidOperationException(
                    $"RPC service '{serviceName}' is configured on endpoint '{_endpoint.Transport}' but no binder is registered.");
            }

            var binder = (LakonaRpcServiceBinder)ActivatorUtilities.CreateInstance(context.Services, descriptor.BinderType);
            binder.Bind(context);
            context.Services.GetService<GameSessionCallbackProxyRegistry>()?.Add(binder);
        }

        _bindServices?.Invoke(builder.ServiceRegistry, context.Services);
    }

    private void BindGameFrameworkRpcs(
        RpcServiceRegistry registry,
        IServiceProvider services,
        GameHandshakeConnectionStateRegistry handshakeStates)
    {
        registry.RegisterRaw(
            GameHandshakeRpcIds.ServiceId,
            GameHandshakeRpcIds.HandshakeMethodId,
            async (connection, notifications, payload, cancellationToken) =>
            {
                GameClientHello hello;
                try
                {
                    hello = LakonaInternalCodec.DecodeGameClientHello(payload);
                }
                catch (InvalidOperationException ex)
                {
                    return BadRequest(ex.Message);
                }

                GameServerHello reply;
                try
                {
                    services.GetRequiredService<GameFrameworkConnectionRegistry>()
                        .Set(connection.ConnectionId, notifications);
                    services.GetRequiredService<GameConnectionDeliveryPolicyRegistry>()
                        .Set(connection.ConnectionId, _endpoint.ReliablePush, GetRecoveryScope());
                    var service = services.GetRequiredService<IGameHandshakeService>();
                    reply = await service.HandshakeAsync(
                        hello,
                        _endpoint.Transport,
                        _endpoint.Serializer,
                        _endpoint.ReliablePush,
                        cancellationToken).ConfigureAwait(false);
                    reply.Recovery = await services
                        .GetRequiredService<IGameSessionHandshakeRecoveryService>()
                        .RecoverAsync(
                            hello.ResumeTicket,
                            connection.ConnectionId,
                            GetRecoveryScope(),
                            _endpoint.ReliablePush,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (GameHandshakeRejectedException ex)
                {
                    return BadRequest(ex.Message);
                }

                if (!handshakeStates.MarkComplete(connection.ConnectionId))
                    return BadRequest("Game handshake deadline expired.");
                return RpcRawResult.Ok(LakonaInternalCodec.EncodeGameServerHello(reply));
            });

        registry.RegisterRaw(
            GameSessionEstablishedRpcIds.ServiceId,
            GameSessionEstablishedRpcIds.AckMethodId,
            (connection, _, payload, cancellationToken) =>
            {
                if (!payload.IsEmpty)
                {
                    return new ValueTask<RpcRawResult>(BadRequest(
                        "Game Session establishment acknowledgement payload must be empty."));
                }

                if (!services.GetRequiredService<GameSessionEstablishedAcknowledgements>()
                    .Acknowledge(connection.ConnectionId))
                {
                    return new ValueTask<RpcRawResult>(BadRequest(
                        "No Game Session establishment acknowledgement is pending."));
                }

                return new ValueTask<RpcRawResult>(RpcRawResult.Ok(ReadOnlyMemory<byte>.Empty));
            });

        registry.RegisterRaw(
            GameHeartbeatRpcIds.ServiceId,
            GameHeartbeatRpcIds.HeartbeatMethodId,
            async (connection, _, payload, cancellationToken) =>
            {
                GameHeartbeatRequest heartbeat;
                try
                {
                    heartbeat = LakonaInternalCodec.DecodeGameHeartbeatRequest(payload);
                }
                catch (InvalidOperationException ex)
                {
                    return BadRequest(ex.Message);
                }

                var service = services.GetRequiredService<IGameHeartbeatService>();
                var reply = await service.HeartbeatAsync(
                    connection.ConnectionId,
                    heartbeat,
                    cancellationToken).ConfigureAwait(false);

                return RpcRawResult.Ok(LakonaInternalCodec.EncodeGameHeartbeatReply(reply));
            });

        registry.RegisterRaw(
            GameReliablePushRpcIds.ServiceId,
            GameReliablePushRpcIds.AckMethodId,
            async (connection, _, payload, cancellationToken) =>
            {
                if (!_endpoint.ReliablePush)
                {
                    return BadRequest(
                        "Reliable push acknowledgement is disabled on this endpoint.");
                }

                ReliablePushAckRequest ack;
                try
                {
                    ack = LakonaInternalCodec.DecodeReliablePushAckRequest(payload);
                }
                catch (InvalidOperationException ex)
                {
                    return BadRequest(ex.Message);
                }

                var sessions = services.GetRequiredService<IGameSessionRegistry>();
                var currentSession = await sessions
                    .GetCurrentSessionAsync(connection.ConnectionId, cancellationToken)
                    .ConfigureAwait(false);
                if (currentSession is null)
                {
                    return BadRequest(
                        "Reliable push acknowledgement requires an active game session.");
                }

                var acknowledgedSession = new GameSessionKey(
                    currentSession.Value.OwnerKey,
                    ack.SessionId);
                var reliablePush = services.GetRequiredService<IReliablePushRuntime>();
                var outcome = await reliablePush
                    .AckAsync(
                        currentSession.Value,
                        acknowledgedSession,
                        ack.Sequence.Value,
                        cancellationToken)
                    .ConfigureAwait(false);
                return RpcRawResult.Ok(LakonaInternalCodec.EncodeReliablePushAckOutcome(outcome));
            });
    }

    private string GetRecoveryScope()
    {
        return string.Join("|",
            _endpoint.Transport.Trim().ToLowerInvariant(),
            _endpoint.Serializer.Trim().ToLowerInvariant(),
            _endpoint.Host.Trim().ToLowerInvariant(),
            _endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _endpoint.Path.Trim(),
            _endpoint.ReliablePush ? "reliable" : "best-effort",
            string.Join(",", _endpoint.RpcServices.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
    }

    private static RpcRawResult BadRequest(string message)
    {
        return RpcRawResult.Failure(RpcStatus.BadRequest, message);
    }
}
