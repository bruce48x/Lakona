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
        session => new BattleService(session, new BattleNotificationsProxy(session), loginTickets, world)))
    .UseAcceptor(new KcpConnectionAcceptor(
        kcpPort,
        RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections,
        loginTickets.AuthorizeKcpAsync));

await Task.WhenAll(
    authBuilder.RunAsync().AsTask(),
    battleBuilder.RunAsync().AsTask());
