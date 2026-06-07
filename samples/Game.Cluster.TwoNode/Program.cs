using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Tcp;

var options = SampleOptions.Parse(args);
return options.Mode switch
{
    "directory" => await RunDirectoryAsync(options),
    "worker" => await RunWorkerAsync(options),
    "driver" => await RunDriverAsync(),
    _ => Usage()
};

static async Task<int> RunDirectoryAsync(SampleOptions options)
{
    if (options.Port is null)
    {
        return Usage();
    }

    StartupDiagnostics.ValidatePort(options.Port.Value);
    Console.WriteLine($"startup-check directory port={options.Port.Value} ok");

    var nodeDirectory = new InMemoryNodeDirectory();
    var routeDirectory = new InMemoryRouteDirectory();
    var serializer = new JsonSampleSerializer();
    var builder = RpcServerHostBuilder.Create()
        .UseSerializer(serializer)
        .UseAcceptor(new TcpConnectionAcceptor(options.Port.Value));
    ULinkRpcNodeDirectoryBinder.Bind(builder.ServiceRegistry, nodeDirectory);
    ULinkRpcRouteDirectoryBinder.Bind(builder.ServiceRegistry, routeDirectory);

    var serverTask = builder.RunAsync(CancellationToken.None).AsTask();
    await WaitForTcpEndpointAsync(
        IPAddress.Loopback,
        options.Port.Value,
        serverTask,
        TimeSpan.FromSeconds(10),
        CancellationToken.None);

    Console.WriteLine($"node-directory-ready tcp://127.0.0.1:{options.Port.Value}");
    Console.WriteLine($"directory-ready tcp://127.0.0.1:{options.Port.Value}");
    await serverTask;
    return 0;
}

static async Task<int> RunWorkerAsync(SampleOptions options)
{
    if (options.Port is null ||
        options.DirectoryEndpoint is null)
    {
        return Usage();
    }

    StartupDiagnostics.ValidatePort(options.Port.Value);
    StartupDiagnostics.ValidateDirectoryEndpoint(options.DirectoryEndpoint);
    Console.WriteLine($"startup-check worker port={options.Port.Value} directory={options.DirectoryEndpoint} ok");

    var serializer = new JsonSampleSerializer();
    var handler = new WorkerHandler();
    var builder = RpcServerHostBuilder.Create()
        .UseSerializer(serializer)
        .UseAcceptor(new TcpConnectionAcceptor(options.Port.Value));
    ULinkRpcClusterMessageBinder.Bind(builder.ServiceRegistry, handler);

    var serverTask = builder.RunAsync(CancellationToken.None).AsTask();
    await WaitForTcpEndpointAsync(
        IPAddress.Loopback,
        options.Port.Value,
        serverTask,
        TimeSpan.FromSeconds(10),
        CancellationToken.None);

    await using var clientFactory = new ULinkRpcClusterClientFactory(
        new TcpULinkRpcClusterTransportFactory(),
        serializer);
    var directoryClient = await clientFactory.GetClientAsync(
        DirectoryLocation(options.DirectoryEndpoint),
        CancellationToken.None);
    var nodeDirectory = new ULinkRpcNodeDirectory(directoryClient);
    var routeDirectory = new ULinkRpcRouteDirectory(directoryClient);
    var registration = await nodeDirectory.RegisterAsync(
        WorkerRegistration(options.Port.Value),
        DateTimeOffset.UtcNow,
        CancellationToken.None);
    if (registration.Status != NodeRegistrationStatus.Registered ||
        registration.Record is null)
    {
        Console.Error.WriteLine($"worker-node-register={registration.Status}");
        return 2;
    }

    var assignedNodeEpoch = registration.Record.NodeEpoch;
    var generation = assignedNodeEpoch;
    var route = new RouteLocation(
        WorkerRoute(),
        "worker",
        new NodeEndpoint($"tcp://127.0.0.1:{options.Port.Value}"),
        DateTimeOffset.UtcNow.AddMinutes(5),
        nodeEpoch: assignedNodeEpoch,
        generation: generation,
        metadata: WorkerRouteMetadata());
    var status = await routeDirectory.RegisterAsync(route);
    if (status != RouteRegistrationStatus.Registered)
    {
        Console.Error.WriteLine($"worker-register={status}");
        return 3;
    }

    Console.WriteLine($"node-registered node=worker epoch={assignedNodeEpoch}");
    Console.WriteLine($"worker-ready epoch={assignedNodeEpoch} tcp://127.0.0.1:{options.Port.Value}");
    await serverTask;
    return 0;
}

static async Task<int> RunDriverAsync()
{
    var directoryPort = GetFreePort();
    var workerPort = GetFreePort();
    var restartedWorkerPort = GetFreePort();
    var directoryEndpoint = $"tcp://127.0.0.1:{directoryPort}";
    var serializer = new JsonSampleSerializer();
    var childProcesses = new List<Process>();
    try
    {
        var directoryProcess = StartTrackedChild(
            childProcesses,
            "--mode",
            "directory",
            "--port",
            directoryPort.ToString());
        var nodeDirectoryReady = await WaitForLineAsync(directoryProcess, "node-directory-ready", TimeSpan.FromSeconds(10));
        Console.WriteLine(nodeDirectoryReady);
        await WaitForLineAsync(directoryProcess, "directory-ready", TimeSpan.FromSeconds(10));

        var worker = StartTrackedChild(
            childProcesses,
            "--mode", "worker",
            "--port", workerPort.ToString(),
            "--directory", directoryEndpoint);
        var workerRegistered = await WaitForLineAsync(worker, "node-registered", TimeSpan.FromSeconds(10));
        Console.WriteLine(workerRegistered);

        await using var clientFactory = new ULinkRpcClusterClientFactory(
            new TcpULinkRpcClusterTransportFactory(),
            serializer);
        var directoryClient = await clientFactory.GetClientAsync(
            DirectoryLocation(directoryEndpoint),
            CancellationToken.None);
        var directory = new ULinkRpcRouteDirectory(directoryClient);
        var now = DateTimeOffset.UtcNow;
        var localRoute = new RouteLocation(
            "control/local",
            "driver",
            new NodeEndpoint("in-memory://driver"),
            now.AddMinutes(5),
            nodeEpoch: 1,
            generation: 1);
        var localRegister = await directory.RegisterAsync(localRoute);
        var staleRegister = await directory.RegisterAsync(
            new RouteLocation(
                WorkerRoute(),
                "worker",
                new NodeEndpoint($"tcp://127.0.0.1:{workerPort}"),
                now.AddMinutes(5),
                nodeEpoch: 1,
                generation: 0,
                metadata: WorkerRouteMetadata()));

        var router = new ClusterRouter(
            "driver",
            directory,
            new DriverHandler(),
            new ULinkRpcClusterNodeMessenger(
                clientFactory,
                new ULinkRpcClusterNodeMessengerOptions
                {
                    SendTimeout = TimeSpan.FromSeconds(2)
                }),
            () => DateTimeOffset.UtcNow);

        var local = await router.SendAsync(NewMessage("control/local", "local-ping", now.AddMinutes(1)));
        var remote = await router.SendAsync(NewActorMessage("remote-ping", now.AddMinutes(1)));
        var missing = await router.SendAsync(NewMessage("missing/route", "missing", now.AddMinutes(1)));
        var expired = await router.SendAsync(NewMessage("control/local", "expired", now.AddSeconds(-1)));
        var timeout = await router.SendAsync(NewActorMessage("timeout", now.AddMinutes(1)));
        var backpressure = await router.SendAsync(NewActorMessage("busy", now.AddMinutes(1)));
        var handlerUnavailable = await router.SendAsync(NewActorMessage("unavailable", now.AddMinutes(1)));

        StopChild(worker);
        var clearedOldEpoch = await directory.ClearByNodeEpochAsync("worker", 1);
        var oldRoute = await directory.ResolveAsync(WorkerRoute(), DateTimeOffset.UtcNow);

        var restartedWorker = StartTrackedChild(
            childProcesses,
            "--mode", "worker",
            "--port", restartedWorkerPort.ToString(),
            "--directory", directoryEndpoint);
        var restartedRegistered = await WaitForLineAsync(restartedWorker, "node-registered", TimeSpan.FromSeconds(10));
        var restartedEpoch = ParseWorkerEpoch(restartedRegistered);
        Console.WriteLine($"node-restarted node=worker epoch={restartedEpoch}");
        var afterRestart = await router.SendAsync(NewActorMessage("after-restart", DateTimeOffset.UtcNow.AddMinutes(1)));

        StopChild(restartedWorker);

        Console.WriteLine($"localRegister={localRegister}");
        Console.WriteLine($"staleRegister={staleRegister}");
        Console.WriteLine($"local={local}");
        Console.WriteLine($"remote={remote}");
        Console.WriteLine($"missing={missing}");
        Console.WriteLine($"expired={expired}");
        Console.WriteLine($"timeout={timeout}");
        Console.WriteLine($"backpressure={backpressure}");
        Console.WriteLine($"handlerUnavailable={handlerUnavailable}");
        Console.WriteLine($"clearedOldEpoch={clearedOldEpoch}");
        Console.WriteLine($"oldRouteAfterClear={(oldRoute is null ? "null" : "present")}");
        Console.WriteLine($"afterRestart={afterRestart}");

        return localRegister == RouteRegistrationStatus.Registered &&
            staleRegister == RouteRegistrationStatus.StaleLocation &&
            local == ClusterSendStatus.Accepted &&
            remote == ClusterSendStatus.Accepted &&
            missing == ClusterSendStatus.RouteNotFound &&
            expired == ClusterSendStatus.Expired &&
            timeout == ClusterSendStatus.Timeout &&
            backpressure == ClusterSendStatus.Backpressure &&
            handlerUnavailable == ClusterSendStatus.HandlerUnavailable &&
            clearedOldEpoch == 1 &&
            oldRoute is null &&
            afterRestart == ClusterSendStatus.Accepted
                ? 0
                : 1;
    }
    finally
    {
        StopChildren(childProcesses);
    }
}

static int Usage()
{
    Console.Error.WriteLine("Usage: --mode driver | --mode directory --port <port> | --mode worker --port <port> --directory <endpoint> [--epoch <compatibility-epoch>]");
    return 64;
}

static Process StartChild(params string[] arguments)
{
    var dll = Assembly.GetEntryAssembly()?.Location ??
        throw new InvalidOperationException("Cannot locate sample assembly.");
    var startInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    startInfo.ArgumentList.Add(dll);
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start child process.");
    return process;
}

static Process StartTrackedChild(List<Process> childProcesses, params string[] arguments)
{
    var process = StartChild(arguments);
    childProcesses.Add(process);
    return process;
}

static async Task<string> WaitForLineAsync(Process process, string expectedPrefix, TimeSpan timeout)
{
    using var timeoutCts = new CancellationTokenSource(timeout);
    while (!timeoutCts.IsCancellationRequested)
    {
        var lineTask = process.StandardOutput.ReadLineAsync(timeoutCts.Token).AsTask();
        var line = await lineTask;
        if (line is null)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"Process exited before '{expectedPrefix}'. stderr: {error}");
        }

        if (line.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return line;
        }
    }

    throw new TimeoutException($"Timed out waiting for '{expectedPrefix}'.");
}

static async Task WaitForTcpEndpointAsync(
    IPAddress address,
    int port,
    Task serverTask,
    TimeSpan timeout,
    CancellationToken cancellationToken)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (serverTask.IsCompleted)
        {
            await serverTask;
            throw new InvalidOperationException("Server task completed before the TCP endpoint became ready.");
        }

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(address, port, cancellationToken);
            return;
        }
        catch (SocketException)
        {
        }

        await Task.Delay(25, cancellationToken);
    }

    if (serverTask.IsFaulted)
    {
        await serverTask;
    }

    throw new TimeoutException($"Timed out waiting for tcp://{address}:{port}.");
}

static long ParseWorkerEpoch(string line)
{
    const string marker = " epoch=";
    var start = line.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0)
    {
        throw new InvalidOperationException($"Worker registration did not include an epoch: {line}");
    }

    start += marker.Length;
    var end = line.IndexOf(' ', start);
    var value = end < 0 ? line[start..] : line[start..end];
    return long.Parse(value);
}

static void StopChild(Process process)
{
    if (process.HasExited)
    {
        return;
    }

    process.Kill(entireProcessTree: true);
    process.WaitForExit(5000);
}

static void StopChildren(List<Process> childProcesses)
{
    for (var i = childProcesses.Count - 1; i >= 0; i--)
    {
        var process = childProcesses[i];
        try
        {
            StopChild(process);
        }
        finally
        {
            process.Dispose();
        }
    }
}

static int GetFreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    finally
    {
        listener.Stop();
    }
}

static RouteLocation DirectoryLocation(string endpoint)
{
    return new RouteLocation(
        "directory",
        "directory",
        new NodeEndpoint(endpoint),
        DateTimeOffset.UtcNow.AddMinutes(5),
        nodeEpoch: 1,
        generation: 1);
}

static RouteKey WorkerRoute()
{
    return ClusterActorRouteKeys.ForActor("worker/demo");
}

static NodeRegistration WorkerRegistration(int port)
{
    return new NodeRegistration(
        "local",
        "worker",
        new Dictionary<string, NodeEndpoint>
        {
            ["cluster"] = new NodeEndpoint($"tcp://127.0.0.1:{port}")
        },
        new[]
        {
            new NodeServiceDescriptor(
                "room",
                "worker-room",
                new Dictionary<string, string>
                {
                    ["sample"] = "cluster-two-node"
                })
        },
        DateTimeOffset.UtcNow.AddMinutes(5),
        NodeState.Ready);
}

static Dictionary<string, string> WorkerRouteMetadata()
{
    return new Dictionary<string, string>
    {
        ["service.kind"] = "room",
        ["service.name"] = "worker-room"
    };
}

static ClusterMessage NewMessage(
    RouteKey route,
    string kind,
    DateTimeOffset expiresAt)
{
    return new ClusterMessage(
        route,
        kind,
        ReadOnlyMemory<byte>.Empty,
        expiresAt,
        "driver",
        correlationId: $"sample-{kind}",
        traceId: "sample-trace",
        orderedBy: route.Value);
}

static ClusterMessage NewActorMessage(string kind, DateTimeOffset expiresAt)
{
    return new ClusterActorEnvelope(
        WorkerRoute(),
        "worker/demo",
        kind,
        Encoding.UTF8.GetBytes(kind),
        expiresAt,
        "driver",
        correlationId: $"sample-{kind}",
        traceId: "sample-trace",
        orderedBy: "worker/demo").ToClusterMessage();
}

sealed class DriverHandler : IClusterMessageHandler
{
    public ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ClusterSendStatus.Accepted);
    }
}

sealed class WorkerHandler : IClusterMessageHandler
{
    public ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = message.Kind switch
        {
            "busy" => ClusterSendStatus.Backpressure,
            "timeout" => ClusterSendStatus.Timeout,
            "unavailable" => ClusterSendStatus.HandlerUnavailable,
            _ => ClusterSendStatus.Accepted
        };

        return ValueTask.FromResult(status);
    }
}

sealed class JsonSampleSerializer : IRpcSerializer
{
    public TransportFrame SerializeFrame<T>(T value)
    {
        return TransportFrame.CopyOf(JsonSerializer.SerializeToUtf8Bytes(value));
    }

    public T Deserialize<T>(ReadOnlySpan<byte> payload)
    {
        return JsonSerializer.Deserialize<T>(payload)!;
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        return Deserialize<T>(payload.Span);
    }
}

sealed record SampleOptions
{
    public string Mode { get; init; } = "driver";

    public int? Port { get; init; }

    public string? DirectoryEndpoint { get; init; }

    public long? NodeEpoch { get; init; }

    public static SampleOptions Parse(string[] args)
    {
        var options = new SampleOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (key)
            {
                case "--mode" when value is not null:
                    options = options with { Mode = value };
                    i++;
                    break;
                case "--port" when value is not null && int.TryParse(value, out var port):
                    options = options with { Port = port };
                    i++;
                    break;
                case "--directory" when value is not null:
                    options = options with { DirectoryEndpoint = value };
                    i++;
                    break;
                case "--epoch" when value is not null && long.TryParse(value, out var epoch):
                    options = options with { NodeEpoch = epoch };
                    i++;
                    break;
            }
        }

        return options;
    }
}
