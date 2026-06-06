using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

/// <summary>
///     Builder for a multi-session RPC server host.
/// </summary>
/// <remarks>
///     The builder composes serializer, transport acceptor, generated service binding, keepalive, frame security,
///     logging, and back-pressure limits. <see cref="Build"/> validates required configuration before creating
///     the host.
/// </remarks>
public sealed class RpcServerHostBuilder
{
    private Func<CancellationToken, ValueTask<IRpcConnectionAcceptor>>? _acceptorFactory;
    private RpcKeepAliveOptions _keepAlive = RpcKeepAliveOptions.Disabled;
    private readonly RpcServerLimits _limits = new();
    private ILogger _logger = DefaultRpcLogging.CreateLogger<RpcServerHost>();
    private bool _servicesConfigured;
    private IRpcSerializer? _serializer;

    /// <summary>
    ///     Explicit server port set by command line parsing or <see cref="UsePort"/>.
    /// </summary>
    public int? Port { get; private set; }

    /// <summary>
    ///     Registry used for generated and manually configured service handlers.
    /// </summary>
    /// <remarks>
    ///     Generated-support API. Regular applications should bind generated services through the builder
    ///     instead of mutating the registry directly.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public RpcServiceRegistry ServiceRegistry { get; } = new();

    /// <summary>
    ///     Frame security configuration applied to accepted transports.
    /// </summary>
    public TransportSecurityConfig Security { get; } = new();

    /// <summary>
    ///     Keepalive configuration used for accepted sessions.
    /// </summary>
    public RpcKeepAliveOptions KeepAlive => _keepAlive;

    /// <summary>
    ///     Server back-pressure limits.
    /// </summary>
    public RpcServerLimits Limits => _limits;

    /// <summary>
    ///     Creates a new server host builder.
    /// </summary>
    public static RpcServerHostBuilder Create()
    {
        return new RpcServerHostBuilder();
    }

    /// <summary>
    ///     Applies supported command-line options to the builder.
    /// </summary>
    /// <param name="args">Command-line arguments, or null.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseCommandLine(string[]? args)
    {
        if (args is null || args.Length == 0)
            return this;

        RpcServerHostCommandLineParser.Apply(this, args);
        return this;
    }

    /// <summary>
    ///     Sets the server port used by transport configuration helpers.
    /// </summary>
    /// <param name="port">TCP/UDP port between 1 and 65535.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="port"/> is outside 1..65535.</exception>
    public RpcServerHostBuilder UsePort(int port)
    {
        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        Port = port;
        return this;
    }

    /// <summary>
    ///     Sets the serializer used by all accepted sessions.
    /// </summary>
    /// <param name="serializer">Payload serializer.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serializer"/> is null.</exception>
    public RpcServerHostBuilder UseSerializer(IRpcSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        return this;
    }

    /// <summary>
    ///     Creates and sets a serializer using its public parameterless constructor.
    /// </summary>
    /// <typeparam name="TSerializer">Serializer type.</typeparam>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseSerializer<TSerializer>()
        where TSerializer : IRpcSerializer, new()
    {
        _serializer = new TSerializer();
        return this;
    }

    /// <summary>
    ///     Configures frame compression or encryption for accepted sessions.
    /// </summary>
    /// <param name="configure">Configuration callback.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    public RpcServerHostBuilder UseSecurity(Action<TransportSecurityConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Security);
        return this;
    }

    /// <summary>
    ///     Uses a delegate-backed logger.
    /// </summary>
    /// <param name="logger">Log sink.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseLogger(Action<string> logger)
    {
        _logger = new DelegateLogger(logger ?? throw new ArgumentNullException(nameof(logger)));
        return this;
    }

    /// <summary>
    ///     Uses an <see cref="ILogger"/> instance.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseLogger(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        return this;
    }

    /// <summary>
    ///     Sets keepalive options for accepted sessions.
    /// </summary>
    /// <param name="keepAlive">Keepalive options.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseKeepAlive(RpcKeepAliveOptions keepAlive)
    {
        _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
        return this;
    }

    /// <summary>
    ///     Enables keepalive with an interval and timeout.
    /// </summary>
    /// <param name="interval">Maximum idle receive time before sending a ping.</param>
    /// <param name="timeout">Maximum idle receive time after a ping before disconnecting.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseKeepAlive(TimeSpan interval, TimeSpan timeout)
    {
        _keepAlive = new RpcKeepAliveOptions
        {
            Enabled = true,
            Interval = interval,
            Timeout = timeout,
            MeasureRtt = false
        };
        return this;
    }

    /// <summary>
    ///     Mutates server back-pressure limits.
    /// </summary>
    /// <param name="configure">Limit configuration callback.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseLimits(Action<RpcServerLimits> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_limits);
        return this;
    }

    /// <summary>
    ///     Copies server back-pressure limits from another instance.
    /// </summary>
    /// <param name="limits">Limits to copy.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseLimits(RpcServerLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits.MaxConcurrentRequestsPerSession = limits.MaxConcurrentRequestsPerSession;
        _limits.MaxQueuedRequestsPerSession = limits.MaxQueuedRequestsPerSession;
        _limits.MaxPendingAcceptedConnections = limits.MaxPendingAcceptedConnections;
        return this;
    }

    /// <summary>
    ///     Manually configures service handlers.
    /// </summary>
    /// <param name="configure">Registry configuration callback.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    ///     Generated-support and advanced runtime configuration. Regular applications should prefer generated
    ///     service binding through <see cref="BindGeneratedServicesFromAssembly"/>.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public RpcServerHostBuilder ConfigureServices(Action<RpcServiceRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(ServiceRegistry);
        _servicesConfigured = true;
        return this;
    }

    /// <summary>
    ///     Binds generated services from an assembly.
    /// </summary>
    /// <param name="assembly">Assembly containing generated binder metadata.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder BindGeneratedServicesFromAssembly(Assembly assembly)
    {
        RpcGeneratedServiceBinder.BindFromAssembly(assembly, ServiceRegistry);
        _servicesConfigured = true;
        return this;
    }

    /// <summary>
    ///     Binds generated services from the process entry assembly.
    /// </summary>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the entry assembly cannot be resolved.</exception>
    public RpcServerHostBuilder BindGeneratedServicesFromEntryAssembly()
    {
        var assembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("Unable to resolve the entry assembly for generated RPC service binding.");

        return BindGeneratedServicesFromAssembly(assembly);
    }

    /// <summary>
    ///     Binds generated services from the assembly containing <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type whose assembly contains generated service binders.</typeparam>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder BindGeneratedServicesFromAssemblyContaining<T>()
    {
        return BindGeneratedServicesFromAssembly(typeof(T).Assembly);
    }

    /// <summary>
    ///     Builds a server host.
    /// </summary>
    /// <returns>A configured server host.</returns>
    /// <exception cref="InvalidOperationException">Thrown when serializer or transport configuration is missing.</exception>
    public RpcServerHost Build()
    {
        if (_serializer is null)
            throw new InvalidOperationException("RPC serializer is not configured.");

        if (_acceptorFactory is null)
            throw new InvalidOperationException("RPC transport is not configured.");

        _limits.Validate();

        if (!_servicesConfigured && ServiceRegistry.IsEmpty)
            BindGeneratedServicesFromEntryAssembly();

        return new RpcServerHost(_serializer, ServiceRegistry, Security, _keepAlive, _acceptorFactory, _logger, _limits.Clone());
    }

    /// <summary>
    ///     Builds and runs the server host.
    /// </summary>
    /// <param name="ct">Cancellation token for the host run loop.</param>
    /// <returns>A task that completes when the host stops.</returns>
    public ValueTask RunAsync(CancellationToken ct = default)
    {
        return Build().RunAsync(ct);
    }

    /// <summary>
    ///     Resolves the configured port or returns a default.
    /// </summary>
    /// <param name="defaultPort">Default port used when no port was configured.</param>
    /// <returns>The configured port or <paramref name="defaultPort"/>.</returns>
    public int ResolvePort(int defaultPort)
    {
        return Port ?? defaultPort;
    }

    /// <summary>
    ///     Sets a factory for creating the transport acceptor.
    /// </summary>
    /// <param name="acceptorFactory">Factory invoked by the host run loop.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseAcceptor(Func<CancellationToken, ValueTask<IRpcConnectionAcceptor>> acceptorFactory)
    {
        _acceptorFactory = acceptorFactory ?? throw new ArgumentNullException(nameof(acceptorFactory));
        return this;
    }

    /// <summary>
    ///     Sets a pre-created transport acceptor.
    /// </summary>
    /// <param name="acceptor">Connection acceptor.</param>
    /// <returns>This builder.</returns>
    public RpcServerHostBuilder UseAcceptor(IRpcConnectionAcceptor acceptor)
    {
        ArgumentNullException.ThrowIfNull(acceptor);
        _acceptorFactory = _ => ValueTask.FromResult(acceptor);
        return this;
    }
}
