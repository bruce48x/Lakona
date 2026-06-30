using System.Diagnostics;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

internal interface IHotfixActorTickSchedulerObserver
{
    void OnDispatchAccepted(HotfixActorTickDispatchObservation observation);

    void OnDispatchRejected(HotfixActorTickDispatchObservation observation, ActorTellResult result);

    void OnDispatchSkipped(HotfixActorTickDispatchObservation observation);

    void OnDispatchCoalesced(HotfixActorTickDispatchObservation observation);

    void OnTickEntered(HotfixActorTickEntryObservation observation);
}

internal readonly record struct HotfixActorTickDispatchObservation(
    string SourceKey,
    Type ActorType,
    ActorId ActorId,
    string MethodName,
    TimeSpan Interval,
    TickBacklogPolicy BacklogPolicy,
    long QueuedTimestamp);

internal readonly record struct HotfixActorTickEntryObservation(
    string SourceKey,
    Type ActorType,
    ActorId ActorId,
    string MethodName,
    TimeSpan Interval,
    TickBacklogPolicy BacklogPolicy,
    long QueuedTimestamp,
    long EnteredTimestamp,
    long Sequence)
{
    public TimeSpan QueueLatency => Stopwatch.GetElapsedTime(QueuedTimestamp, EnteredTimestamp);
}

internal sealed class NullHotfixActorTickSchedulerObserver : IHotfixActorTickSchedulerObserver
{
    public static NullHotfixActorTickSchedulerObserver Instance { get; } = new();

    private NullHotfixActorTickSchedulerObserver()
    {
    }

    public void OnDispatchAccepted(HotfixActorTickDispatchObservation observation)
    {
    }

    public void OnDispatchRejected(HotfixActorTickDispatchObservation observation, ActorTellResult result)
    {
    }

    public void OnDispatchSkipped(HotfixActorTickDispatchObservation observation)
    {
    }

    public void OnDispatchCoalesced(HotfixActorTickDispatchObservation observation)
    {
    }

    public void OnTickEntered(HotfixActorTickEntryObservation observation)
    {
    }
}
