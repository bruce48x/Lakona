using Lakona.Game.Clustering.Postgres;
using Lakona.Game.ProcessTests.App;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

return await LakonaGameServer.RunAsync(args, server => server
    .ConfigureLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true))
    .AddServices((services, configuration) =>
    {
        services.AddLakonaPostgresClustering(configuration);
        // Observe the real directory; do not substitute membership, transport,
        // activation storage, replies, or shutdown ordering in this process test.
        var registration = services.Single(item => item.ServiceType == typeof(IActorDirectory));
        var factory = registration.ImplementationFactory
            ?? throw new InvalidOperationException("Expected the production directory factory.");
        services.Remove(registration);
        services.AddSingleton<IActorDirectory>(provider =>
            new ObservedDirectory((IActorDirectory)factory(provider)));
    }));
