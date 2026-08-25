using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hosting;

internal enum LakonaNodeLifecycleStage
{
    ApplicationModules = 100,
    Hotfix = 200,
    ClusterTransport = 300,
    Membership = 400,
    ActorDirectory = 500,
    ActorActivations = 550,
    StartupActors = 600,
    MembershipStopping = 650,
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
    private readonly IReadOnlyList<ILakonaNodeLifecycleParticipant> _participants =
        ValidateAndOrderParticipants(participants);
    private readonly List<ILakonaNodeLifecycleParticipant> _entered = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _startAttempted;
    private bool _stopped;

    private static IReadOnlyList<ILakonaNodeLifecycleParticipant> ValidateAndOrderParticipants(
        IEnumerable<ILakonaNodeLifecycleParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var snapshot = participants.ToArray();
        var duplicate = snapshot
            .GroupBy(static participant => participant.Stage)
            .FirstOrDefault(static group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Lakona node lifecycle stage '{duplicate.Key}' has multiple participants: " +
                string.Join(", ", duplicate.Select(static participant => participant.Name)) + ".");
        }

        return snapshot
            .OrderBy(static participant => participant.Stage)
            .ToArray();
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopped)
            {
                throw new InvalidOperationException("Lakona node lifecycle has already stopped.");
            }

            if (_startAttempted)
            {
                throw new InvalidOperationException("Lakona node lifecycle has already started.");
            }

            _startAttempted = true;

            try
            {
                foreach (var participant in _participants)
                {
                    _entered.Add(participant);
                    await participant.StartAsync(cancellationToken).ConfigureAwait(false);
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
        // Cleanup ownership cannot be canceled before it begins. The caller's token is
        // still passed to every participant so each cleanup can respect its deadline.
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
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
        var failures = new List<Exception>();
        for (var index = _entered.Count - 1; index >= 0; index--)
        {
            var participant = _entered[index];
            try
            {
                await participant.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                logger.LogError(
                    exception,
                    "Lakona node lifecycle participant {Participant} failed to stop.",
                    participant.Name);
            }
        }

        _entered.Clear();
        if (preservePrimaryFailure || failures.Count == 0)
        {
            return;
        }

        if (failures.All(static failure => failure is OperationCanceledException)
            && cancellationToken.IsCancellationRequested)
        {
            throw failures[0];
        }

        throw new AggregateException(
            "One or more Lakona node lifecycle participants failed to stop.",
            failures);
    }
}
