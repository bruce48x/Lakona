using System.Text.Json;
using FrameworkBenchmark.Lakona.Contracts;
using FrameworkBenchmark.Lakona.Server;
using FrameworkBenchmark.Lakona.Server.Generated;
using Lakona.Game.Cluster;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Tcp;
using Lakona.Rpc.Transport.WebSocket;
using Microsoft.Extensions.Logging;

var role = ReadOption(args, "--role");
var worker1Port = ReadPort(args, "--worker-1-port");
var worker2Port = ReadPort(args, "--worker-2-port");
using var shutdown = new CancellationTokenSource();
using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.LogToStandardErrorThreshold = LogLevel.Trace;
}));
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    if (role is "worker-1" or "worker-2")
    {
        var workerPort = role == "worker-1" ? worker1Port : worker2Port;
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(new MemoryPackRpcSerializer())
            .UseLoggerFactory(loggerFactory)
            .UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
            .ConfigureServices(registry => WorkerServiceBinder.Bind(registry, new WorkerService(role)))
            .UseAcceptor(new TcpConnectionAcceptor(workerPort, "127.0.0.1"));
        var runTask = builder.RunAsync(shutdown.Token);
        WriteReady(role, role, new Dictionary<string, string>());
        await runTask;
        return;
    }

    if (role != "frontdoor")
    {
        throw new ArgumentException("--role must be 'frontdoor', 'worker-1', or 'worker-2'.");
    }

    var clientPort = ReadPort(args, "--client-port");
    await using var worker1Client = CreateWorkerClient(worker1Port);
    await using var worker2Client = CreateWorkerClient(worker2Port);
    await Task.WhenAll(worker1Client.ConnectAsync().AsTask(), worker2Client.ConnectAsync().AsTask());
    var routeDirectory = new InMemoryRouteDirectory();
    for (var index = 0; index < BenchmarkRouting.TargetCount; index++)
    {
        var targetKey = BenchmarkRouting.TargetKey(index);
        var owner = BenchmarkRouting.Owner(targetKey);
        var port = owner == "worker-1" ? worker1Port : worker2Port;
        await routeDirectory.RegisterAsync(new RouteLocation(
            targetKey,
            owner,
            new NodeEndpoint($"tcp://127.0.0.1:{port}"),
            DateTimeOffset.UtcNow.AddHours(1)));
    }

    var acceptor = await WsConnectionAcceptor.CreateAsync(clientPort, "/ws", "127.0.0.1");
    var frontdoorBuilder = RpcServerHostBuilder.Create()
        .UseSerializer(new MemoryPackRpcSerializer())
        .UseLoggerFactory(loggerFactory)
        .UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45))
        .ConfigureServices(registry => EchoServiceBinder.Bind(
            registry,
            new EchoService(worker1Client, worker2Client, routeDirectory)))
        .UseAcceptor(acceptor);
    var frontdoorRunTask = frontdoorBuilder.RunAsync(shutdown.Token);
    WriteReady(
        "frontdoor",
        "frontdoor-1",
        new Dictionary<string, string> { ["client"] = $"ws://127.0.0.1:{clientPort}/ws" });
    await frontdoorRunTask;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

static RpcClient CreateWorkerClient(int port) => new(new RpcClientOptions(
    new TcpTransport("127.0.0.1", port),
    new MemoryPackRpcSerializer()));

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
