using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hosting;

internal enum LakonaNodeLifecycleStage
{
    ApplicationModules = 100,
    Hotfix = 200,
    ClusterTransport = 300,
    Membership = 400,
    ActorDirectory = 500,
    StartupActors = 600,
    Admission = 700
}

internal interface ILakonaNodeLifecycleParticipant
{
    string Name { get; }
    LakonaNodeLifecycleStage Stage { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

internal sealed class LakonaNodeLifecycle(
    IEnumerable<ILakonaNodeLifecycleParticipant> participants,
    ILogger<LakonaNodeLifecycle> logger)
{
    private readonly IReadOnlyList<ILakonaNodeLifecycleParticipant> _participants = participants
        .Select(static (participant, index) => (Participant: participant, Index: index))
        .OrderBy(static item => item.Participant.Stage)
        .ThenBy(static item => item.Index)
        .Select(static item => item.Participant)
        .ToArray();
    private readonly List<ILakonaNodeLifecycleParticipant> _started = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started.Count != 0)
            {
                return;
            }

            try
            {
                foreach (var participant in _participants)
                {
                    await participant.StartAsync(cancellationToken).ConfigureAwait(false);
                    _started.Add(participant);
                }
            }
            catch
            {
                await StopStartedAsync(CancellationToken.None, preservePrimaryFailure: true)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopStartedAsync(cancellationToken, preservePrimaryFailure: false)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopStartedAsync(
        CancellationToken cancellationToken,
        bool preservePrimaryFailure)
    {
        Exception? firstFailure = null;
        for (var index = _started.Count - 1; index >= 0; index--)
        {
            var participant = _started[index];
            try
            {
                await participant.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
                logger.LogError(
                    exception,
                    "Lakona node lifecycle participant {Participant} failed to stop.",
                    participant.Name);
            }
        }

        _started.Clear();
        if (!preservePrimaryFailure && firstFailure is not null)
        {
            throw firstFailure;
        }
    }
}
