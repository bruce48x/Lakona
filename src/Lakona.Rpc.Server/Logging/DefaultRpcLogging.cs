using Microsoft.Extensions.Logging;

namespace Lakona.Rpc.Server;

internal static class DefaultRpcLogging
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

    public static ILogger CreateLogger<T>()
    {
        return FactoryInstance.CreateLogger<T>();
    }
}
