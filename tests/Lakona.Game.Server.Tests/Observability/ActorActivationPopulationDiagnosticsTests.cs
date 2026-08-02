using System.Diagnostics.Metrics;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Actors.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

[Collection(ActorPopulationMetricsCollectionNames.Diagnostics)]
public sealed class ActorActivationPopulationDiagnosticsTests
{
    [Fact]
    public async Task Local_directory_reports_active_retained_and_released_population_without_tags()
    {
        using var metrics = new ActorPopulationMetricCollector();
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(TestContext.Current.CancellationToken);
        }

        var directory = Assert.IsAssignableFrom<IActorActivationDirectory>(
            provider.GetRequiredService<IActorDirectory>());
        var owner = new NodeReference(
            new ClusterIncarnationId(Guid.Parse("10000000-0000-0000-0000-000000000000")),
            new NodeId("node-a"),
            new NodeIncarnationId(Guid.Parse("20000000-0000-0000-0000-000000000000")));
        var first = await directory.AcquireAsync(
            ActorId.From("player:first"),
            owner,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);
        await directory.AcquireAsync(
            ActorId.From("player:second"),
            owner,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);
        Assert.True(await directory.ReleaseAsync(
            first.Record.ActorId,
            first.Record.ActivationId!.Value,
            first.Record.Version,
            TestContext.Current.CancellationToken));

        var population = metrics.Observe();

        AssertMeasurement(population, "lakona-actor.activation.active", 1);
        AssertMeasurement(population, "lakona-actor.activation.metadata", 2);
        AssertMeasurement(population, "lakona-actor.activation.released", 1);
    }

    private static void AssertMeasurement(
        IReadOnlyDictionary<string, MetricMeasurement> population,
        string name,
        long expected)
    {
        var measurement = population[name];
        Assert.Equal(expected, measurement.Value);
        Assert.Empty(measurement.Tags);
    }

    private sealed class ActorPopulationMetricCollector : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly Dictionary<string, MetricMeasurement> measurements = new(StringComparer.Ordinal);

        public ActorPopulationMetricCollector()
        {
            listener.InstrumentPublished = (instrument, currentListener) =>
            {
                if (instrument.Meter.Name == LakonaActorDiagnostics.MeterName)
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
                measurements[instrument.Name] = new MetricMeasurement(measurement, tags.ToArray()));
            listener.Start();
        }

        public IReadOnlyDictionary<string, MetricMeasurement> Observe()
        {
            listener.RecordObservableInstruments();
            return measurements;
        }

        public void Dispose()
        {
            listener.Dispose();
        }
    }

    private sealed record MetricMeasurement(
        long Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);
}
