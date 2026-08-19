using Lakona.Rpc.Core;
using System;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Tcp;

var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new MemoryPackRpcSerializer())
    .UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
    .UseAcceptor(new TcpConnectionAcceptor(20000));

using var shutdown = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    await builder.RunAsync(shutdown.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
