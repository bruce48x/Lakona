namespace Lakona.Game.Server.Hosting;

public interface IRpcServerConfigurator
{
    string Name { get; }

    void Configure(LakonaGameServerRpcContext context);
}
