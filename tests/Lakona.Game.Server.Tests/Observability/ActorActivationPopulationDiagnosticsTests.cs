using System.Diagnostics.Metrics;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Actors.Internal;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

[Collection(ActorPopulationMetricsCollectionNames.Diagnostics)]
public sealed class ActorActivationPopulationDiagnosticsTests
{
    [Fact]
    public void Cluster_endpoint_publishes_actor_activation_population_gauges()
    {
        using var listener = new MeterListener();
        var expectedInstruments = new HashSet<string>(StringComparer.Ordinal)
        {
            "lakona.game.actor.activation.active",
            "lakona.game.actor.activation.metadata",
            "lakona.game.actor.activation.released"
        };
        var instruments = new HashSet<string>(StringComparer.Ordinal);
        var measurements = new Dictionary<string, long>(StringComparer.Ordinal);
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == LakonaGameServerTelemetry.ActorMeterName
                && expectedInstruments.Contains(instrument.Name))
            {
                instruments.Add(instrument.Name);
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements[instrument.Name] = value);
        listener.Start();

        var services = new ServiceCollection()
            .AddTestEndpointRuntimes()
            .AddLakonaGameServerActors()
            .UseReadySingleNodeMembership();
        services.AddLakonaGameClusterEndpoint();

        using var provider = services.BuildServiceProvider();
        _ = provider.GetServices<IHostedService>().ToArray();
        provider.GetRequiredService<ActorActivationRegistry>().Set(new ActorDirectoryRecord(
            ActorId.From("metrics/one"),
            new NodeReference(
                new ClusterIncarnationId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                new NodeId("dev-1"),
                new NodeIncarnationId(Guid.Parse("22222222-2222-2222-2222-222222222222"))),
            ActorActivationId.New(),
            DateTimeOffset.UtcNow));
        listener.RecordObservableInstruments();

        Assert.Equal(
            expectedInstruments.OrderBy(static value => value, StringComparer.Ordinal),
            instruments.OrderBy(static value => value, StringComparer.Ordinal));
        Assert.Equal(1, measurements["lakona.game.actor.activation.active"]);
        Assert.Equal(1, measurements["lakona.game.actor.activation.metadata"]);
        Assert.Equal(0, measurements["lakona.game.actor.activation.released"]);
    }
}
