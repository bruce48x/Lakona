using Lakona.Rpc.Core;

namespace Lakona.Rpc.Client;

/// <summary>
///     Configuration used to create a client runtime or generated client facade.
/// </summary>
/// <remarks>
///     The options object owns the selected transport and serializer references. If security is configured,
///     <see cref="CreateConfiguredTransport"/> wraps the transport in a <see cref="TransformingTransport"/>.
/// </remarks>
public sealed class RpcClientOptions
{
    private TransportSecurityConfig? _security;

    /// <summary>
    ///     Creates client runtime options.
    /// </summary>
    /// <param name="transport">Concrete transport used by the client.</param>
    /// <param name="serializer">Serializer used for RPC method payloads.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transport"/> or <paramref name="serializer"/> is null.</exception>
    public RpcClientOptions(ITransport transport, IRpcSerializer serializer)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    ///     Optional keepalive configuration. Disabled by default.
    /// </summary>
    public RpcKeepAliveOptions KeepAlive { get; set; } = RpcKeepAliveOptions.Disabled;

    /// <summary>
    ///     Serializer used for request, response, and push payloads.
    /// </summary>
    public IRpcSerializer Serializer { get; }

    /// <summary>
    ///     Mutable frame security configuration.
    /// </summary>
    public TransportSecurityConfig Security => _security ??= new TransportSecurityConfig();

    /// <summary>
    ///     Underlying transport before optional security wrapping.
    /// </summary>
    public ITransport Transport { get; }

    /// <summary>
    ///     Configures compression or encryption for the client transport.
    /// </summary>
    /// <param name="configure">Configuration callback.</param>
    /// <returns>This options instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    public RpcClientOptions UseSecurity(Action<TransportSecurityConfig> configure)
    {
        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        configure(Security);
        return this;
    }

    /// <summary>
    ///     Returns the transport that should be passed to the runtime.
    /// </summary>
    /// <returns>
    ///     The original transport when security is disabled; otherwise a <see cref="TransformingTransport"/>
    ///     wrapping the original transport.
    /// </returns>
    public ITransport CreateConfiguredTransport()
    {
        return _security is { IsEnabled: true }
            ? new TransformingTransport(Transport, _security)
            : Transport;
    }
}
