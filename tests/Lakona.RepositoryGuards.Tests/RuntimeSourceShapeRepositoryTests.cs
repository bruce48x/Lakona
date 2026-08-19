using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class RuntimeSourceShapeRepositoryTests
{
    [Fact]
    public void Kcp_server_transport_does_not_materialize_output_memory_with_to_array()
    {
        var source = ReadSource("Lakona.Rpc.Transport.Kcp", "Server", "KcpServerTransport.cs");

        Assert.DoesNotContain("mem.ToArray()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Kcp_transport_netstandard_receive_paths_are_cancellable()
    {
        var source = ReadSource("Lakona.Rpc.Transport.Kcp", "Client", "KcpTransport.cs");

        Assert.DoesNotContain(
            "await _socket.ReceiveFromAsync(new ArraySegment<byte>(buffer), SocketFlags.None, _receiveAny).ConfigureAwait(false);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "received = await socket.ReceiveFromAsync(new ArraySegment<byte>(buffer), SocketFlags.None, any).ConfigureAwait(false);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Websocket_acceptor_does_not_use_an_unbounded_pending_connection_queue()
    {
        var source = ReadSource("Lakona.Rpc.Transport.WebSocket", "Server", "WsConnectionAcceptor.cs");

        Assert.DoesNotContain(
            "Channel.CreateUnbounded<RpcAcceptedConnection>()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rpc_server_host_does_not_own_process_signals()
    {
        var source = ReadSource("Lakona.Rpc.Server", "Hosting", "RpcServerHost.cs");

        Assert.DoesNotContain("Console.CancelKeyPress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsoleCancelEventHandler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PosixSignalRegistration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessExit", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyLoadContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SIGINT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SIGTERM", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Game_server_facade_does_not_handle_cli_health_check_arguments()
    {
        var source = ReadSource("Lakona.Game.Server", "Hosting", "LakonaGameServer.cs");

        Assert.DoesNotContain("--readiness-check", source, StringComparison.Ordinal);
        Assert.DoesNotContain("--liveness-check", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameReadinessProbe.Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameLivenessProbe.Run", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Game_server_facade_delegates_build_and_runtime_lifecycle()
    {
        var source = ReadSource("Lakona.Game.Server", "Hosting", "LakonaGameServer.cs");

        Assert.Contains("LakonaGameServerBootstrapper", source, StringComparison.Ordinal);
        Assert.Contains("LakonaGameServerRunner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadInitialHotfixAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.Services", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaModuleDiscovery", source, StringComparison.Ordinal);
        Assert.DoesNotContain("modules.StartAsync", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] relativePath)
    {
        var path = Path.Combine(
            [GitChangeSetReader.FindRepositoryRoot(), "src", .. relativePath]);
        return File.ReadAllText(path);
    }
}
