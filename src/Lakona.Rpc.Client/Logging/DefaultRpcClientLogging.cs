using Microsoft.Extensions.Logging;

namespace Lakona.Rpc.Client;

internal static class DefaultRpcClientLogging
{
    private static readonly ILoggerFactory FactoryInstance = LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        builder.SetMinimumLevel(LogLevel.Information);
    });

    public static ILogger CreateRequestLogger()
    {
        return FactoryInstance.CreateLogger(RpcClientRequestLogging.Category);
    }
}
