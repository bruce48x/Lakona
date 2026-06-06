using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Health;

public static class LakonaGameLivenessProbe
{
    public static int Run(ClusterOptions? clusterOptions, LakonaGameRuntimeOptions runtimeOptions)
    {
        if (clusterOptions is not null)
        {
            return RunCluster(clusterOptions);
        }

        return RunStandalone(runtimeOptions);
    }

    private static int RunCluster(ClusterOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.NodeId))
        {
            Console.Error.WriteLine("Cluster liveness check failed: NodeId is required.");
            return 1;
        }

        if (options.AdvertisedEndpoints.Count == 0)
        {
            Console.Error.WriteLine("Cluster liveness check failed: at least one advertised endpoint is required.");
            return 1;
        }

        if (options.Services.Count == 0)
        {
            Console.Error.WriteLine("Cluster liveness check failed: at least one cluster service is required.");
            return 1;
        }

        foreach (var endpoint in options.AdvertisedEndpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Key) ||
                string.IsNullOrWhiteSpace(endpoint.Value))
            {
                Console.Error.WriteLine("Cluster liveness check failed: advertised endpoint keys and values are required.");
                return 1;
            }
        }

        Console.WriteLine("cluster=healthy");
        return 0;
    }

    private static int RunStandalone(LakonaGameRuntimeOptions runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.Node.Id))
        {
            Console.Error.WriteLine("Liveness check failed: NodeId is required.");
            return 1;
        }

        if (runtime.Endpoints.Count == 0)
        {
            Console.Error.WriteLine("Liveness check failed: at least one endpoint is required.");
            return 1;
        }

        Console.WriteLine("standalone=healthy");
        return 0;
    }
}
