using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Health;

public static class LakonaGameLivenessProbe
{
    public static int Run(ClusterOptions? clusterOptions, LakonaGameRuntimeOptions runtimeOptions)
    {
        _ = runtimeOptions;
        if (clusterOptions is null)
        {
            Console.Error.WriteLine("Cluster liveness check failed: ClusterOptions are required.");
            return 1;
        }

        var options = clusterOptions;
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
}
