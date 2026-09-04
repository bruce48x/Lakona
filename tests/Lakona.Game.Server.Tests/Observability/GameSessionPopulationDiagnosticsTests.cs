using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

[Collection(GameSessionPopulationMetricsCollectionNames.Diagnostics)]
public sealed class GameSessionPopulationDiagnosticsTests
{
    [Fact]
    public void Session_registry_exposes_low_cardinality_population_gauges()
    {
        using var listener = new MeterListener();
        var measurements = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == LakonaGameServerTelemetry.SessionMeterName)
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements[instrument.Name] = value);
        listener.Start();

        using var provider = new ServiceCollection()
            .AddLakonaGameServerSessions()
            .BuildServiceProvider();
        _ = provider.GetRequiredService<IEnumerable<Microsoft.Extensions.Hosting.IHostedService>>().ToArray();
        listener.RecordObservableInstruments();

        Assert.Equal(0, measurements["lakona.game.session.total"]);
        Assert.Equal(0, measurements["lakona.game.session.active"]);
        Assert.Equal(0, measurements["lakona.game.session.connection.active"]);
        Assert.Equal(0, measurements["lakona.game.session.disconnected"]);
        Assert.Equal(0, measurements["lakona.game.session.terminated"]);
        Assert.Equal(0, measurements["lakona.game.session.resumable"]);
    }
}
