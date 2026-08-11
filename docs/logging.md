# Logging

Lakona emits structured events through `Microsoft.Extensions.Logging`, but it
does not own an application's logging policy. Runtime packages depend on
logging abstractions, accept an application-owned `ILoggerFactory`, and remain
externally silent when the application does not install a provider.

This document is the authority for logging ownership, integration points, and
provider replacement across Lakona RPC and Game applications.

## Ownership Boundary

Lakona owns:

- the event message, level, structured fields, exception, and category;
- propagation of the application's logger factory through Game, RPC, and
  cluster runtime components;
- a null fallback when a standalone RPC client or server receives no factory;
- the separate bounded diagnostics event buffer described below.

The application owns:

- provider selection and provider packages;
- minimum levels and category filters;
- console, engine, file, network, or telemetry sinks;
- formatting, enrichment, rolling, retention, and redaction policy;
- creation and disposal of client-side logger factories.

There is no `Lakona.Logging` package and no Lakona-specific logger interface.
Providers integrate through the standard `ILogger`, `ILoggerFactory`,
`ILoggerProvider`, and `ILoggingBuilder` contracts. This keeps Console,
Serilog, NLog, OpenTelemetry, game-engine adapters, and custom providers behind
one standard application boundary.

## Integration Points

| Application boundary | Logging seam | Lifetime owner |
| --- | --- | --- |
| Game server | `LakonaGameServerBuilder.ConfigureLogging` | Server host |
| Game client | `LakonaGameClientOptions.LoggerFactory` | Client application |
| Standalone RPC server | `RpcServerHostBuilder.UseLoggerFactory` | Server application |
| Standalone RPC client | `RpcClientOptions.LoggerFactory` | Client application |

`LakonaGameClientOptions` derives from `RpcClientOptions`, so one factory
captures both Game and underlying RPC events. Automatic Game client recovery
passes that same factory to each replacement connection generation.

`LakonaGameServerBuilder.ConfigureLogging` configures the root host. Game.Server
passes the resulting factory to framework services, client-facing RPC hosts,
and outbound cluster RPC clients. Applications do not configure those internal
clients separately.

## Generated Project Default

Projects created by Lakona Tool or Hub explicitly install
`Microsoft.Extensions.Logging.Console` and configure `AddSimpleConsole` at the
client and server composition roots. Console logging is a replaceable starter
policy, not a runtime dependency or framework default.

To adopt another provider, change the generated composition root and its
package references. Do not add provider packages to `Lakona.Rpc.Client`,
`Lakona.Rpc.Server`, or another runtime package.

## Game Server

The generated Console configuration has this shape:

```csharp
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.Logging;

return await LakonaGameServer.RunAsync(args, static server => server
    .ConfigureLogging(static logging =>
    {
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        logging.SetMinimumLevel(LogLevel.Information);
    }));
```

The server host owns providers registered through this callback and disposes
them with the host. To combine providers, call multiple `Add...` extensions in
the same callback. To replace Console, remove `AddSimpleConsole` and install the
new provider package in the application project.

### Serilog

Install `Serilog.Extensions.Logging` and the sinks required by the application,
then adapt the configured Serilog logger at the composition root:

```csharp
using Microsoft.Extensions.Logging;
using Serilog;

var serilog = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

return await LakonaGameServer.RunAsync(args, server => server
    .ConfigureLogging(logging =>
        logging.AddSerilog(serilog, dispose: true)));
```

Sink selection, enrichment, filtering, file rolling, and retention stay in the
Serilog configuration.

### NLog

Install `NLog.Extensions.Logging`, keep the NLog configuration in the server
application, and register its provider:

```csharp
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

return await LakonaGameServer.RunAsync(args, static server => server
    .ConfigureLogging(static logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddNLog();
    }));
```

Targets, rules, archives, and retention remain NLog policy rather than Lakona
configuration.

## Game Clients

A client application should create one logger factory at its composition root,
reuse it for every connection, and dispose it only when the application shuts
down:

```csharp
using Microsoft.Extensions.Logging;

using var clientLoggerFactory =
    LoggerFactory.Create(static logging =>
    {
        logging.AddSimpleConsole(options => options.SingleLine = true);
        logging.SetMinimumLevel(LogLevel.Information);
    });

var options = new LakonaGameClientOptions(CreateTransport, serializer)
{
    LoggerFactory = clientLoggerFactory
};
```

Do not create a factory per connection or reconnect attempt. The factory owns
its providers, and the generated recoverable client carries it across
connection generations.

In a Console or Godot application, dispose the factory from the application
shutdown path. In Unity, keep it in the application-level composition object
and dispose it from `OnApplicationQuit` or the equivalent owned shutdown path;
do not attach its lifetime to a reconnecting scene or client instance.

Serilog and NLog use the same client seam:

```csharp
ILoggerFactory serilogFactory = LoggerFactory.Create(logging =>
    logging.AddSerilog(serilog, dispose: true));

ILoggerFactory nlogFactory = LoggerFactory.Create(static logging =>
    logging.AddNLog());
```

Choose provider versions that support the client target. This is especially
important for Unity's .NET Standard 2.1 and C# 9.0 environment; a provider that
assumes a newer runtime is not made Unity-compatible by the Lakona adapter.

### Unity Or Another Engine Logger

When logs should appear in an engine-native console, implement or select an
engine-compatible `ILoggerProvider` and register it at the same root:

```csharp
private static readonly ILoggerFactory ClientLoggerFactory =
    LoggerFactory.Create(static logging =>
    {
        logging.AddProvider(new UnityLoggerProvider());
        logging.SetMinimumLevel(LogLevel.Information);
    });
```

The provider should map `LogLevel` to the engine's severity methods, preserve
the category and exception, render structured state without losing field
names, and avoid calling engine APIs from unsupported threads. If the engine
requires main-thread logging, the provider owns that dispatch policy; the
Lakona runtime does not marshal log calls onto the render thread.

## Standalone RPC Applications

Standalone RPC applications use the same application-owned factory directly:

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(static logging =>
    logging.AddSimpleConsole());

var clientOptions = new RpcClientOptions(transport, serializer)
{
    LoggerFactory = loggerFactory
};

var server = RpcServerHostBuilder.Create()
    .UseLoggerFactory(loggerFactory)
    .UseSerializer(serializer)
    .UseAcceptor(acceptor);
```

Omitting `LoggerFactory` or `UseLoggerFactory` selects the null fallback. The
RPC runtimes do not create a Console provider on the application's behalf.

## Levels, Categories, And Structured State

Configure broad defaults and targeted overrides through the selected logging
stack. Lakona categories use their runtime category or type name, so a
`"Lakona"` prefix filter can control framework events without suppressing
application categories:

```csharp
logging.SetMinimumLevel(LogLevel.Warning);
logging.AddFilter("Lakona", LogLevel.Information);
```

A Game server can also use the standard application-level `Logging` section:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Lakona": "Information"
    }
  }
}
```

This section is owned by the .NET host and must not be nested beneath
`Lakona:Observability`. Provider-specific sections such as `Serilog`, or files
such as `NLog.config`, likewise belong to the application.

`LoggerFactory.Create` in a standalone client does not automatically read an
application configuration source. A client that wants configuration-driven
Microsoft logging filters must explicitly add that source, for example with
`logging.AddConfiguration(configuration.GetSection("Logging"))`, and install a
compatible `Microsoft.Extensions.Logging.Configuration` package. Provider-level
rules can filter events again after Microsoft logging filters, so applications
should make both layers intentional instead of assuming one overrides the
other.

Treat category names and structured fields as operational diagnostics, not as
wire-protocol or business-domain contracts. Applications should avoid filters
that depend on an internal implementation type remaining unchanged.

Provider configuration must also enforce the application's data policy across
both framework and application events. Filters, enrichers, and custom providers
must not export secrets, credentials, session tickets, or personal data.

## Diagnostics Event Buffer Is Separate

`Lakona:Observability:Diagnostics:EventBuffer` is a bounded, process-local
diagnostics feature used by Game.Server. Its `MinimumLevel` controls which
events enter that buffer. It does not enable an external sink, select a logging
provider, or change provider filters.

Consequently, a Game server can remain externally silent while retaining
eligible events in its internal diagnostics buffer. Configure provider policy
through `ConfigureLogging`; configure the diagnostics buffer through the
Lakona observability section. The two thresholds are intentionally independent.
