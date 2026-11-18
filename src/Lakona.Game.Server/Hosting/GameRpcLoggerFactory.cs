using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hosting;

// Borrows the application factory; the host remains its sole lifetime owner.
internal sealed class GameRpcLoggerFactory(ILoggerFactory inner, string category) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => inner.CreateLogger(categoryName switch
    {
        "Lakona.Rpc.Client.Request" or "Lakona.Rpc.Server.Request" or
        "Lakona.Rpc.Client.Request.Payload" or "Lakona.Rpc.Server.Request.Payload" => category,
        _ => categoryName
    });

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
    public void Dispose() { }
}
