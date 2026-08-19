# Lakona.Rpc.Transport.Tcp

TCP transport implementations for Lakona.Rpc.

## Install

```bash
dotnet add package Lakona.Rpc.Transport.Tcp
```

## Documentation

Design boundary: https://bruce48x.github.io/Lakona/concepts/design-boundary/

## Includes

- `TcpTransport` (client)
- `TcpServerTransport` (server)
- `TcpConnectionAcceptor` (server)

## Server Usage

```csharp
var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new MemoryPackRpcSerializer())
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
```
