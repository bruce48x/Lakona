using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;

var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new MemoryPackRpcSerializer());

builder.UseAcceptor(new KcpConnectionAcceptor(
    20000,
    builder.Limits.MaxPendingAcceptedConnections));

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
