namespace Lakona.Game.Server.Hosting;

public interface IRpcServerConfigurator
{
    string Transport { get; }

    void Configure(LakonaGameServerRpcContext context);
}
