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

        builder.UseSerializer(LakonaEndpointRuntimeDefaults.CreateSerializer(_endpoint));
        builder.UseAcceptor(ct => LakonaEndpointRuntimeDefaults.CreateAcceptorAsync(_endpoint, ct));
        builder.UseSessionRequestGate(new GameHandshakeRpcGate());
        BindGameFrameworkRpcs(builder.ServiceRegistry, context.Services);

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

    private void BindGameFrameworkRpcs(RpcServiceRegistry registry, IServiceProvider services)
    {
        registry.Register(
            GameHandshakeRpcIds.ServiceId,
            GameHandshakeRpcIds.HandshakeMethodId,
            async (session, request, cancellationToken) =>
            {
                GameClientHello hello;
                try
                {
                    hello = LakonaInternalCodec.DecodeGameClientHello(request.Payload.Memory);
                }
                catch (InvalidOperationException ex)
                {
                    return EncodeBadRequest(request.RequestId, ex.Message);
                }

                GameServerHello reply;
                try
                {
                    services.GetRequiredService<GameFrameworkConnectionRegistry>().Set(session);
                    services.GetRequiredService<GameConnectionDeliveryPolicyRegistry>()
                        .Set(session.ContextId, _endpoint.ReliablePush, GetRecoveryScope());
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
                            session,
                            GetRecoveryScope(),
                            _endpoint.ReliablePush,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (GameHandshakeRejectedException ex)
                {
                    return EncodeBadRequest(request.RequestId, ex.Message);
                }

                var payload = LakonaInternalCodec.EncodeGameServerHello(reply);

                var state = session.GetOrAddScopedService(
                    GameHandshakeRpcIds.ServiceId,
                    static _ => new GameHandshakeSessionState());
                state.IsComplete = true;

                return RpcEnvelopeCodec.EncodeResponse(
                    request.RequestId,
                    RpcStatus.Ok,
                    payload);
            });

        registry.Register(
            GameSessionEstablishedRpcIds.ServiceId,
            GameSessionEstablishedRpcIds.AckMethodId,
            (session, request, cancellationToken) =>
            {
                _ = cancellationToken;
                if (!request.Payload.IsEmpty)
                {
                    return new ValueTask<TransportFrame>(EncodeBadRequest(
                        request.RequestId,
                        "Game Session establishment acknowledgement payload must be empty."));
                }

                if (!services.GetRequiredService<GameSessionEstablishedAcknowledgements>()
                    .Acknowledge(session.ContextId))
                {
                    return new ValueTask<TransportFrame>(EncodeBadRequest(
                        request.RequestId,
                        "No Game Session establishment acknowledgement is pending."));
                }

                return new ValueTask<TransportFrame>(RpcEnvelopeCodec.EncodeResponse(
                    request.RequestId,
                    RpcStatus.Ok,
                    ReadOnlyMemory<byte>.Empty));
            });

        registry.Register(
            GameHeartbeatRpcIds.ServiceId,
            GameHeartbeatRpcIds.HeartbeatMethodId,
            async (session, request, cancellationToken) =>
            {
                GameHeartbeatRequest heartbeat;
                try
                {
                    heartbeat = LakonaInternalCodec.DecodeGameHeartbeatRequest(request.Payload.Memory);
                }
                catch (InvalidOperationException ex)
                {
                    return EncodeBadRequest(request.RequestId, ex.Message);
                }

                var service = services.GetRequiredService<IGameHeartbeatService>();
                var reply = await service.HeartbeatAsync(
                    session.ContextId,
                    heartbeat,
                    cancellationToken).ConfigureAwait(false);

                var payload = LakonaInternalCodec.EncodeGameHeartbeatReply(reply);

                return RpcEnvelopeCodec.EncodeResponse(
                    request.RequestId,
                    RpcStatus.Ok,
                    payload);
            });

        registry.Register(
            GameReliablePushRpcIds.ServiceId,
            GameReliablePushRpcIds.AckMethodId,
            async (session, request, cancellationToken) =>
            {
                if (!_endpoint.ReliablePush)
                {
                    return EncodeBadRequest(
                        request.RequestId,
                        "Reliable push acknowledgement is disabled on this endpoint.");
                }

                ReliablePushAckRequest ack;
                try
                {
                    ack = LakonaInternalCodec.DecodeReliablePushAckRequest(request.Payload.Memory);
                }
                catch (InvalidOperationException ex)
                {
                    return EncodeBadRequest(request.RequestId, ex.Message);
                }

                var sessions = services.GetRequiredService<IGameSessionRegistry>();
                var currentSession = await sessions
                    .GetCurrentSessionAsync(session.ContextId, cancellationToken)
                    .ConfigureAwait(false);
                if (currentSession is null)
                {
                    return EncodeBadRequest(
                        request.RequestId,
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
                var payload = LakonaInternalCodec.EncodeReliablePushAckOutcome(outcome);

                return RpcEnvelopeCodec.EncodeResponse(
                    request.RequestId,
                    RpcStatus.Ok,
                    payload);
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

    private static TransportFrame EncodeBadRequest(uint requestId, string message)
    {
        return RpcEnvelopeCodec.EncodeResponse(
            requestId,
            RpcStatus.BadRequest,
            ReadOnlyMemory<byte>.Empty,
            message);
    }
}
