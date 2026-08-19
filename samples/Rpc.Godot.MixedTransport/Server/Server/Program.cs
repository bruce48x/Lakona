using Agar.MixedTransport.Server.Services;
using Shared.Interfaces.Server.Generated;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Transport.Tcp;

var tcpPort = args.Length >= 1 && int.TryParse(args[0], out var parsedTcpPort) ? parsedTcpPort : 20000;
var kcpPort = args.Length >= 2 && int.TryParse(args[1], out var parsedKcpPort) ? parsedKcpPort : tcpPort + 1;

await using var world = new BattleWorld();
var loginTickets = new LoginTicketStore(kcpPort);

// This sample hosts auth and battle RPC on separate transports, so it binds
// each generated service explicitly instead of using entry-assembly auto binding.
var authBuilder = RpcServerHostBuilder.Create()
    .UseSerializer(new MemoryPackRpcSerializer())
    .UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
    .ConfigureServices(registry => AuthServiceBinder.Bind(registry, new AuthService(loginTickets, kcpPort)))
    .UseAcceptor(new TcpConnectionAcceptor(tcpPort));

var battleBuilder = RpcServerHostBuilder.Create()
    .UseSerializer(new MemoryPackRpcSerializer())
    .UseKeepAlive(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
    .ConfigureServices(registry => BattleServiceBinder.BindFactory(
        registry,
        (connection, notifications) => new BattleService(connection, notifications, loginTickets, world)))
    .UseAcceptor(new KcpConnectionAcceptor(
        kcpPort,
        RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections,
        loginTickets.AuthorizeKcpAsync));

using var shutdown = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    await Task.WhenAll(
        authBuilder.RunAsync(shutdown.Token).AsTask(),
        battleBuilder.RunAsync(shutdown.Token).AsTask());
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
