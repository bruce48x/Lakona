using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Actors.Internal;

internal interface IActorActivationPopulationSource
{
    ActorActivationPopulation ObserveActivationPopulation();
}

internal readonly record struct ActorActivationPopulation(
    int Active,
    int Metadata,
    int Released);

internal sealed class ActorActivationPopulationDiagnostics : IHostedService, IDisposable
{
    private readonly IActorActivationPopulationSource? source;
    private readonly Meter meter = new(
        LakonaActorDiagnostics.MeterName,
        typeof(ActorActivationPopulationDiagnostics).Assembly.GetName().Version?.ToString());
    private readonly ObservableGauge<long> active;
    private readonly ObservableGauge<long> metadata;
    private readonly ObservableGauge<long> released;

    public ActorActivationPopulationDiagnostics(IActorDirectory directory)
    {
        source = directory as IActorActivationPopulationSource;
        active = meter.CreateObservableGauge(
            "lakona-actor.activation.active",
            () => (long)Observe().Active);
        metadata = meter.CreateObservableGauge(
            "lakona-actor.activation.metadata",
            () => (long)Observe().Metadata);
        released = meter.CreateObservableGauge(
            "lakona-actor.activation.released",
            () => (long)Observe().Released);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        meter.Dispose();
    }

    private ActorActivationPopulation Observe()
    {
        return source?.ObserveActivationPopulation() ?? default;
    }
}
