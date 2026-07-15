using System.Text.Json;
using FrameworkBenchmark.Lakona.Server;
using FrameworkBenchmark.Lakona.Server.Generated;
using Lakona.Rpc.Core;
using Lakona.Rpc.Client;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Tcp;
using Lakona.Rpc.Transport.WebSocket;

var role = ReadOption(args, "--role");
var workerPort = ReadPort(args, "--worker-port");
if (role == "worker")
{
    var builder = RpcServerHostBuilder.Create()
        .UseSerializer(new MemoryPackRpcSerializer())
        .UseLogger(message => Console.Error.WriteLine(message))
        .UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
        .ConfigureServices(registry => WorkerServiceBinder.Bind(registry, new WorkerService()))
        .UseAcceptor(new TcpConnectionAcceptor(workerPort, "127.0.0.1"));
    var runTask = builder.RunAsync();
    WriteReady("worker", "worker-1", new Dictionary<string, string>());
    await runTask;
    return;
}

if (role != "frontdoor")
{
    throw new ArgumentException("--role must be 'frontdoor' or 'worker'.");
}

var clientPort = ReadPort(args, "--client-port");
await using var workerClient = new RpcClient(new RpcClientOptions(
    new TcpTransport("127.0.0.1", workerPort),
    new MemoryPackRpcSerializer()));
await workerClient.ConnectAsync();
await using var acceptor = await WsConnectionAcceptor.CreateAsync(clientPort, "/ws", "127.0.0.1");
var frontdoorBuilder = RpcServerHostBuilder.Create()
    .UseSerializer(new MemoryPackRpcSerializer())
    .UseLogger(message => Console.Error.WriteLine(message))
    .UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
    .ConfigureServices(registry => EchoServiceBinder.Bind(registry, new EchoService(workerClient)))
    .UseAcceptor(acceptor);
var frontdoorRunTask = frontdoorBuilder.RunAsync();
WriteReady(
    "frontdoor",
    "frontdoor-1",
    new Dictionary<string, string> { ["client"] = $"ws://127.0.0.1:{clientPort}/ws" });
await frontdoorRunTask;

static string ReadOption(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    if (index < 0 || index + 1 >= arguments.Length)
    {
        throw new ArgumentException($"{name} <value> is required.");
    }

    return arguments[index + 1];
}

static int ReadPort(string[] arguments, string name)
{
    return int.TryParse(ReadOption(arguments, name), out var port) && port is > 0 and <= 65535
        ? port
        : throw new ArgumentException($"{name} <number> is required.");
}

static void WriteReady(string role, string nodeId, Dictionary<string, string> endpoints)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        @event = "ready",
        role,
        nodeId,
        endpoints
    }));
}
