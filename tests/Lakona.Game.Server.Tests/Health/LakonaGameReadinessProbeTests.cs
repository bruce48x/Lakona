using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Xunit;

namespace Lakona.Game.Server.Tests.Health;

public sealed class LakonaGameReadinessProbeTests
{
    [Fact]
    public void Run_DoesNotRequireClusterEndpointWhenClusterIsNotConfigured()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "dev-1" },
            Endpoints =
            [
                new LakonaGameEndpointOptions
                {
                    Transport = "websocket",
                    Serializer = "json",
                    Host = "127.0.0.1",
                    Port = 20000,
                    Path = "/ws",
                    RpcServices = ["login"]
                }
            ],
            Cluster = null
        };

        var output = new StringWriter();
        var errors = new StringWriter();
        var originalOutput = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(output);
            Console.SetError(errors);

            _ = LakonaGameReadinessProbe.Run(runtime, runtime.ToClusterOptions(), ["--json"]);
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }

        var text = output.ToString() + errors.ToString();
        Assert.DoesNotContain("ULINK040", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona:Cluster:Endpoint is required", text, StringComparison.Ordinal);
    }
}
