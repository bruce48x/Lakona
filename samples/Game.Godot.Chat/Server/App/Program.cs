using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Chat;
using Server.App.Hosting;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;

return await LakonaGameServer.RunAsync(args, server => server
    .UseTransport("websocket")
    .UseSerializer(() => new MemoryPackRpcSerializer())
    .UseAcceptor(async opts => await WsConnectionAcceptor.CreateAsync(opts.Port, opts.Path, opts.Host))
    .AddServices(services => services.AddSingleton<ChatConnectionLifecycle>())
    .BindServices(ServiceBindingConfigurator.Bind));
