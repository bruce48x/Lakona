using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaGameServerRpcContext
{
    public LakonaGameServerRpcContext(
        string serverName,
        RpcServerHostBuilder builder,
        IServiceProvider services,
        string[] commandLineArgs,
        CancellationToken stoppingToken)
    {
        ServerName = serverName;
        Builder = builder;
        Services = services;
        CommandLineArgs = commandLineArgs;
        StoppingToken = stoppingToken;
    }

    public string ServerName { get; }

    public RpcServerHostBuilder Builder { get; }

    public IServiceProvider Services { get; }

    public string[] CommandLineArgs { get; }

    public CancellationToken StoppingToken { get; }
}
