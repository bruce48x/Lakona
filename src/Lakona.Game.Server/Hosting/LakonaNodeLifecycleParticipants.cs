using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Modules;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hosting;

internal sealed class LakonaModuleLifecycleParticipant(
    LakonaModuleRuntime modules) : ILakonaNodeLifecycleParticipant
{
    public string Name => "application-modules";
    public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.ApplicationModules;
    public Task StartAsync(CancellationToken cancellationToken) => modules.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => modules.StopAsync(cancellationToken);
}

internal sealed class InitialHotfixLifecycleParticipant(
    IServiceProvider services) : ILakonaNodeLifecycleParticipant
{
    public string Name => "initial-hotfix";
    public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.Hotfix;
    public Task StartAsync(CancellationToken cancellationToken) =>
        LakonaGameServerRunner.LoadInitialHotfixAsync(services, cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var manager = services.GetRequiredService<IHotfixManager>();
        if (manager is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (manager is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

internal sealed class LakonaNodeHostedService(
    LakonaNodeLifecycle lifecycle) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        lifecycle.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        lifecycle.StopAsync(cancellationToken);
}


internal sealed class RpcServersLifecycleParticipant(
    RpcServersHostedService service) : ILakonaNodeLifecycleParticipant
{
    public string Name => "rpc-listeners";
    public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.ClusterTransport;
    public Task StartAsync(CancellationToken cancellationToken) => service.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => service.StopAsync(cancellationToken);
}

internal sealed class MembershipLifecycleParticipant(
    MembershipTableHostedService service) : ILakonaNodeLifecycleParticipant
{
    public string Name => "membership";
    public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.Membership;
    public Task StartAsync(CancellationToken cancellationToken) => service.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => service.StopAsync(cancellationToken);
}

internal sealed class ActorDirectoryLifecycleParticipant(
    DistributedActorDirectory service) : ILakonaNodeLifecycleParticipant
{
    public string Name => "actor-directory";
    public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.ActorDirectory;
    public Task StartAsync(CancellationToken cancellationToken) => service.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => service.StopAsync(cancellationToken);
}

internal sealed class ActorActivationLifecycleParticipant(
    IActorActivationLifecycle activations) : ILakonaNodeLifecycleParticipant
{
    public string Name => "actor-activations";
    public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.ActorActivations;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) =>
        activations.DrainAsync(cancellationToken).AsTask();
}

internal sealed class StartupActorLifecycleParticipant(
    StartupActorHostedService service) : ILakonaNodeLifecycleParticipant
{
    public string Name => "startup-actors";
    public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.StartupActors;
    public Task StartAsync(CancellationToken cancellationToken) => service.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => service.StopAsync(cancellationToken);
}

internal sealed class MembershipStoppingLifecycleParticipant(
    MembershipTableHostedService service) : ILakonaNodeLifecycleParticipant
{
    public string Name => "membership-stopping";
    public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.MembershipStopping;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => service.BeginStoppingAsync(cancellationToken);
}

internal sealed class AdmissionLifecycleParticipant(
    LakonaServerStartupHostedService service) : ILakonaNodeLifecycleParticipant
{
    public string Name => "admission-readiness";
    public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.Admission;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await service.StartingAsync(cancellationToken).ConfigureAwait(false);
        await service.StartAsync(cancellationToken).ConfigureAwait(false);
        await service.StartedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await service.StoppingAsync(cancellationToken).ConfigureAwait(false);
        await service.StopAsync(cancellationToken).ConfigureAwait(false);
        await service.StoppedAsync(cancellationToken).ConfigureAwait(false);
    }
}
