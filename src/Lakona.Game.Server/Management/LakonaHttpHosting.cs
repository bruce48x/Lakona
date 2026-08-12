using System.Net;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Http;
using Lakona.Game.Server.LocalAdmin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Management;

internal static class LakonaHttpHosting
{
    internal static void Configure(
        WebApplicationBuilder builder,
        LakonaGameRuntimeOptions runtime)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(runtime);

        builder.Services.TryAddSingleton<LakonaApplicationHttpEndpointRegistry>();
        builder.Services.AddSingleton<IHotfixRuntimePublicationParticipant>(
            static provider =>
                provider.GetRequiredService<LakonaApplicationHttpEndpointRegistry>());

        var managementEnabled = runtime.Health.Enabled || runtime.Management.Admin.Enabled;
        ValidateBindings(runtime, managementEnabled);

        if (!managementEnabled && runtime.Http.Listeners.Count == 0)
        {
            builder.Services.Replace(
                ServiceDescriptor.Singleton<IServer, LakonaNoopHttpServer>());
            return;
        }

        // An empty URL setting prevents ASP.NET Core's localhost:5000 default.
        // Every Lakona HTTP socket is declared below through Kestrel Listen APIs.
        builder.WebHost.UseUrls([]);
        builder.WebHost.ConfigureKestrel(options =>
        {
            foreach (var listener in runtime.Http.Listeners)
            {
                Listen(options, listener.Host, listener.Port);
            }

            if (managementEnabled)
            {
                Listen(
                    options,
                    runtime.Management.Http.Host,
                    runtime.Management.Http.Port);
            }
        });
    }

    internal static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var runtime = app.Services.GetRequiredService<LakonaGameRuntimeOptions>();
        var applicationEndpoints = app.Services
            .GetRequiredService<LakonaApplicationHttpEndpointRegistry>();

        foreach (var listener in runtime.Http.Listeners)
        {
            MapApplicationListener(
                app,
                listener,
                applicationEndpoints.GetSource(listener.Id));
        }

        if (runtime.Health.Enabled || runtime.Management.Admin.Enabled)
        {
            app.MapWhen(
                context => IsManagementRequest(context, runtime),
                branch =>
                {
                    branch.UseRouting();
                    branch.UseEndpoints(endpoints =>
                        MapManagementEndpoints(endpoints, app.Services, runtime));
                });
        }
    }

    private static void MapManagementEndpoints(
        IEndpointRouteBuilder endpoints,
        IServiceProvider services,
        LakonaGameRuntimeOptions runtime)
    {
        if (runtime.Health.Enabled)
        {
            foreach (var route in services.GetServices<ILakonaHealthHttpRoute>())
            {
                endpoints.MapMethods(route.Path, [route.Method], async context =>
                {
                    var remoteIsLoopback = IsRemoteLoopback(context);
                    if (runtime.Health.RequireLoopback && !remoteIsLoopback)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }

                    var response = await route.HandleAsync(
                        new LakonaHealthHttpRequest(
                            context.Request.Method,
                            context.Request.Path,
                            remoteIsLoopback,
                            runtime.Health.RequireLoopback),
                        context.RequestAborted);
                    await WriteAsync(context, response.StatusCode, response.ContentType, response.Body);
                });
            }
        }

        if (runtime.Management.Admin.Enabled)
        {
            foreach (var route in services.GetServices<ILakonaLocalAdminRoute>())
            {
                endpoints.MapMethods(route.Path, [route.Method], async context =>
                {
                    var remoteIsLoopback = IsRemoteLoopback(context);
                    if (runtime.Management.Admin.RequireLoopback && !remoteIsLoopback)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }

                    var response = await route.HandleAsync(
                        new LakonaLocalAdminRequest(
                            context.Request.Method,
                            context.Request.Path,
                            context.Request.Body,
                            remoteIsLoopback,
                            runtime.Management.Admin.RequireLoopback),
                        context.RequestAborted);
                    await WriteAsync(context, response.StatusCode, response.ContentType, response.Body);
                });
            }
        }
    }

    private static void MapApplicationListener(
        WebApplication app,
        LakonaHttpListenerOptions listener,
        LakonaApplicationHttpEndpointDataSource dataSource)
    {
        app.MapWhen(
            context => MatchesBinding(
                listener.Host,
                listener.Port,
                context.Connection.LocalIpAddress,
                context.Connection.LocalPort),
            branch =>
            {
                branch.UseRouting();
                branch.UseEndpoints(endpoints => endpoints.DataSources.Add(dataSource));
            });
    }

    internal static async Task DispatchApplicationAsync(
        HttpContext context,
        LakonaHttpListenerOptions listener,
        int endpointSlot)
    {
        var admissionGate = context.RequestServices
            .GetRequiredService<IDistributedWorkAdmissionGate>();
        if (!admissionGate.TryEnter(out var admission))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(listener.RequestTimeoutSeconds));
            try
            {
                var body = await ReadBodyAsync(
                    context,
                    listener.MaximumBodyBytes,
                    deadline.Token);
                if (body is null)
                {
                    return;
                }

                var accessor = context.RequestServices
                    .GetRequiredService<IHotfixRuntimeAccessor>();
                using var lease = accessor.AcquireCurrent();
                var call = CreateCall(
                    context,
                    body,
                    lease.Snapshot.Services,
                    deadline.Token);
                var response = await lease.Invoker
                    .InvokeHttpAsync<LakonaHttpCall, LakonaHttpResponse>(
                        endpointSlot,
                        call,
                        deadline.Token);
                await WriteAsync(context, response, deadline.Token);
            }
            catch (OperationCanceledException)
                when (!context.RequestAborted.IsCancellationRequested
                    && deadline.IsCancellationRequested)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                }
            }
        }
        finally
        {
            admissionGate.Exit(admission);
        }
    }

    private static async Task<byte[]?> ReadBodyAsync(
        HttpContext context,
        int maximumBodyBytes,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > maximumBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return null;
        }

        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = maximumBodyBytes;
        }

        await using var body = new MemoryStream(
            context.Request.ContentLength is > 0 and <= int.MaxValue
                ? (int)Math.Min(context.Request.ContentLength.Value, 81920)
                : 0);
        var buffer = new byte[Math.Min(81920, maximumBodyBytes)];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return body.ToArray();
            }

            if (body.Length + read > maximumBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return null;
            }

            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static LakonaHttpCall CreateCall(
        HttpContext context,
        byte[] body,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return new LakonaHttpCall(
            new LakonaHttpRequest(
                body,
                context.Request.Headers.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Select(static value => value ?? "").ToArray(),
                    StringComparer.OrdinalIgnoreCase),
                context.Request.Query.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Select(static value => value ?? "").ToArray(),
                    StringComparer.OrdinalIgnoreCase),
                context.Request.RouteValues.ToDictionary(
                    static pair => pair.Key,
                    static pair => Convert.ToString(pair.Value) ?? "",
                    StringComparer.OrdinalIgnoreCase),
                context.User.Identity?.IsAuthenticated == true
                    ? context.User.Identity.Name
                    : null,
                context.Connection.RemoteIpAddress is { } address
                    ? new IPEndPoint(address, context.Connection.RemotePort)
                    : null,
                context.TraceIdentifier),
            services,
            cancellationToken);
    }

    private static async Task WriteAsync(
        HttpContext context,
        LakonaHttpResponse response,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = response.StatusCode;
        context.Response.ContentType = response.ContentType;
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }

        context.Response.ContentLength = response.Body.Length;
        await context.Response.Body.WriteAsync(response.Body, cancellationToken);
    }

    private static void ValidateBindings(
        LakonaGameRuntimeOptions runtime,
        bool managementEnabled)
    {
        LakonaHttpOptions.Validate(runtime.Http.Listeners);
        var bindings = new List<(string Name, string Host, int Port)>();
        foreach (var listener in runtime.Http.Listeners)
        {
            EnsureValidBinding($"Lakona:Http:Listeners:{listener.Id}", listener.Host, listener.Port);
            foreach (var existing in bindings)
            {
                if (BindingsConflict(existing.Host, existing.Port, listener.Host, listener.Port))
                {
                    throw new InvalidOperationException(
                        $"HTTP listener '{listener.Id}' conflicts with '{existing.Name}' on {listener.Host}:{listener.Port}.");
                }
            }

            bindings.Add((listener.Id, listener.Host, listener.Port));
        }

        if (!managementEnabled)
        {
            return;
        }

        var management = runtime.Management.Http;
        EnsureValidBinding("Lakona:Management:Http", management.Host, management.Port);
        foreach (var application in bindings)
        {
            if (BindingsConflict(application.Host, application.Port, management.Host, management.Port))
            {
                throw new InvalidOperationException(
                    $"Management HTTP conflicts with application listener '{application.Name}' on {management.Host}:{management.Port}.");
            }
        }
    }

    private static void EnsureValidBinding(string name, string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException($"{name}:Host must not be empty.");
        }

        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{name}:Port must be between 1 and 65535.");
        }

        if (!IsWildcard(host)
            && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && !IPAddress.TryParse(host, out _))
        {
            throw new InvalidOperationException(
                $"{name}:Host must be localhost, an IP address, or a wildcard address.");
        }
    }

    private static bool BindingsConflict(
        string firstHost,
        int firstPort,
        string secondHost,
        int secondPort)
    {
        if (firstPort != secondPort)
        {
            return false;
        }

        return IsWildcard(firstHost)
            || IsWildcard(secondHost)
            || firstHost.Equals(secondHost, StringComparison.OrdinalIgnoreCase)
            || (IsLoopbackHost(firstHost) && IsLoopbackHost(secondHost));
    }

    private static bool IsWildcard(string host)
    {
        return host is "*" or "+" or "0.0.0.0" or "::";
    }

    private static bool IsLoopbackHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
    }

    private static void Listen(KestrelServerOptions options, string host, int port)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            options.ListenLocalhost(port);
            return;
        }

        if (IsWildcard(host))
        {
            options.ListenAnyIP(port);
            return;
        }

        options.Listen(IPAddress.Parse(host), port);
    }

    private static bool IsManagementRequest(HttpContext context, LakonaGameRuntimeOptions runtime)
    {
        return MatchesBinding(
            runtime.Management.Http.Host,
            runtime.Management.Http.Port,
            context.Connection.LocalIpAddress,
            context.Connection.LocalPort);
    }

    private static bool MatchesBinding(
        string configuredHost,
        int configuredPort,
        IPAddress? localAddress,
        int localPort)
    {
        return localPort == configuredPort
            && AddressMatches(configuredHost, localAddress);
    }

    private static bool AddressMatches(string configuredHost, IPAddress? localAddress)
    {
        if (localAddress is null || IsWildcard(configuredHost))
        {
            return localAddress is not null;
        }

        if (configuredHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.IsLoopback(localAddress);
        }

        return IPAddress.Parse(configuredHost).Equals(localAddress);
    }

    private static bool IsRemoteLoopback(HttpContext context)
    {
        return context.Connection.RemoteIpAddress is { } address
            && IPAddress.IsLoopback(address);
    }

    private static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string contentType,
        string body)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = contentType;
        await context.Response.WriteAsync(body, context.RequestAborted);
    }

    private sealed class LakonaNoopHttpServer : IServer
    {
        public LakonaNoopHttpServer()
        {
            Features.Set<IServerAddressesFeature>(new ServerAddressesFeature());
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(
            IHttpApplication<TContext> application,
            CancellationToken cancellationToken)
            where TContext : notnull
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
