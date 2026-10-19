using FrameworkBenchmark.Lakona.Server;
using Lakona.Game.Clustering.Redis;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Tcp;
using Lakona.Rpc.Transport.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var role = ReadOption(args, "--role");
if (role is not ("frontdoor" or "worker-1" or "worker-2"))
    throw new ArgumentException("Expected frontdoor, worker-1, or worker-2.");
var frontdoor = role == "frontdoor";
var worker1Port = ReadPort(args, "--worker-1-port");
var worker2Port = ReadPort(args, "--worker-2-port");
var clientPort = ReadPort(args, "--client-port");
var clusterPort = ReadPort(args, "--cluster-port");
var managementPort = ReadPort(args, "--management-port");
var endpointPort = frontdoor ? clientPort : role == "worker-1" ? worker1Port : worker2Port;
var settings = new Dictionary<string, string?>
{
    ["Lakona:Node:Id"] = frontdoor ? "frontdoor-1" : role,
    ["Lakona:Node:Roles:0"] = frontdoor ? "frontdoor" : "worker",
    ["Lakona:Cluster:Endpoint"] = $"tcp://127.0.0.1:{clusterPort}",
    ["Lakona:Cluster:Membership:Provider"] = "Redis",
    ["Lakona:Cluster:Membership:ConnectionStringName"] = "BenchmarkRedis",
    ["Lakona:Cluster:Membership:Redis:Key"] = ReadOption(args, "--membership-key"),
    ["Lakona:Cluster:Membership:TableRefreshSeconds"] = "1",
    ["ConnectionStrings:BenchmarkRedis"] = $"127.0.0.1:{ReadPort(args, "--redis-port")},abortConnect=false",
    ["Lakona:Management:Http:Host"] = "127.0.0.1",
    ["Lakona:Management:Http:Port"] = managementPort.ToString(),
    ["Lakona:Management:Admin:Enabled"] = "false",
    ["Lakona:Endpoints:0:Transport"] = frontdoor ? "websocket" : "tcp",
    ["Lakona:Endpoints:0:Serializer"] = "memorypack",
    ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
    ["Lakona:Endpoints:0:Port"] = endpointPort.ToString(),
    ["Lakona:Endpoints:0:Path"] = "/ws",
    ["Lakona:Endpoints:0:ReliablePush"] = "false",
    ["Lakona:Endpoints:0:RpcServices:0"] = frontdoor ? "echo" : "worker",
    ["Benchmark:ClientPort"] = clientPort.ToString(),
    ["Benchmark:Worker1Port"] = worker1Port.ToString()
};
// Stdout belongs exclusively to the coordinator lifecycle protocol.
Console.SetOut(Console.Error);
return await LakonaGameServer.RunAsync([], server => server
    .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings))
    .ConfigureLogging(logging => logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace).AddSimpleConsole(options =>
    {
        options.SingleLine = true;
    }).SetMinimumLevel(LogLevel.Warning))
    .AddServices((services, configuration) =>
    {
        services.AddLakonaRedisClustering(configuration);
        services.AddHostedService<BenchmarkReadiness>();
    })
    .RegisterEndpointTransport("tcp", endpoint => new TcpConnectionAcceptor(endpoint.Port, endpoint.Host))
    .RegisterEndpointTransport("websocket", async (endpoint, cancellationToken) =>
        await WsConnectionAcceptor.CreateAsync(endpoint.Port, "/ws", endpoint.Host, cancellationToken))
    .RegisterEndpointSerializer("memorypack", () => new MemoryPackRpcSerializer()));

static string ReadOption(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1]
        : throw new ArgumentException($"{name} <value> is required.");
}

static int ReadPort(string[] arguments, string name) =>
    int.TryParse(ReadOption(arguments, name), out var port) && port is > 0 and <= 65535
        ? port : throw new ArgumentException($"{name} <port> is required.");
