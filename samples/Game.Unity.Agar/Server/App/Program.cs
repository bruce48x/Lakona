using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Observability;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Transport.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

return await LakonaGameServer.RunAsync(args, static server => server
    .AddServices(static (services, configuration) =>
    {
        var serviceName = ResolveServiceName("lakona-game-unity-agar");
        var serviceInstanceId = configuration["Lakona:Node:Id"];

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName,
                serviceInstanceId: serviceInstanceId))
            .WithMetrics(metrics => metrics
                .AddMeter(LakonaGameServerTelemetry.MeterNames.ToArray())
                .AddRuntimeInstrumentation()
                .AddOtlpExporter())
            .WithTracing(tracing => tracing
                .AddSource(LakonaGameServerTelemetry.ActivitySourceNames.ToArray())
                .AddAspNetCoreInstrumentation()
                .AddOtlpExporter());
    })
    .ConfigureLogging(static logging => logging
        .AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        })
        .AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.AddOtlpExporter();
        }))
    .RegisterEndpointTransport("websocket", static async (endpoint, cancellationToken) =>
        await WsConnectionAcceptor.CreateAsync(
            endpoint.Port,
            string.IsNullOrWhiteSpace(endpoint.Path) ? endpoint.GetDefaultPath() : endpoint.Path,
            endpoint.Host,
            cancellationToken).ConfigureAwait(false))
    .RegisterEndpointTransport("kcp", static endpoint =>
        new KcpConnectionAcceptor(endpoint.Port, endpoint.Host))
    .RegisterEndpointSerializer("memorypack", static () => new MemoryPackRpcSerializer()));

static string ResolveServiceName(string fallback)
{
    var configured = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");
    return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
}
