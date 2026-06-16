using Lakona.Rpc.Server;
using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaGameServerRpcContext
{
    public LakonaGameServerRpcContext(
        string serverName,
        LakonaGameEndpointOptions endpoint,
        RpcServerHostBuilder builder,
        IServiceProvider services,
        string[] commandLineArgs,
        CancellationToken stoppingToken)
    {
        ServerName = serverName;
        Endpoint = endpoint;
        Builder = builder;
        Services = services;
        CommandLineArgs = commandLineArgs;
        StoppingToken = stoppingToken;
    }

    public string ServerName { get; }

    public LakonaGameEndpointOptions Endpoint { get; }

    public RpcServerHostBuilder Builder { get; }

    public IServiceProvider Services { get; }

    public string[] CommandLineArgs { get; }

    public CancellationToken StoppingToken { get; }
}
