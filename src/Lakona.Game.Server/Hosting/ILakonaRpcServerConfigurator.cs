namespace Lakona.Game.Server.Hosting;

public interface IULinkRpcServerConfigurator
{
    string Name { get; }

    void Configure(LakonaGameServerRpcContext context);
}
