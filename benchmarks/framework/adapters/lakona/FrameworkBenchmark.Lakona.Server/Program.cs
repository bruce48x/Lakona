using System.Text.Json;
using FrameworkBenchmark.Lakona.Server;
using FrameworkBenchmark.Lakona.Server.Generated;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;

var port = ReadPort(args);
await using var acceptor = await WsConnectionAcceptor.CreateAsync(port, "/ws", "127.0.0.1");
var builder = RpcServerHostBuilder.Create()
    .UseSerializer(new MemoryPackRpcSerializer())
    .UseLogger(message => Console.Error.WriteLine(message))
    .UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
    .ConfigureServices(registry => EchoServiceBinder.Bind(registry, new EchoService()))
    .UseAcceptor(acceptor);

var runTask = builder.RunAsync();
Console.WriteLine(JsonSerializer.Serialize(new
{
    @event = "ready",
    role = "frontdoor",
    nodeId = "frontdoor-1",
    endpoints = new Dictionary<string, string> { ["client"] = $"ws://127.0.0.1:{port}/ws" }
}));
await runTask;

static int ReadPort(string[] arguments)
{
    var index = Array.IndexOf(arguments, "--port");
    if (index < 0 || index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out var port))
    {
        throw new ArgumentException("--port <number> is required.");
    }

    return port;
}
