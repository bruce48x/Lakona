using System.Diagnostics;
using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;

namespace Lakona.Game.ProcessTests.App;

// Test infrastructure only. One append-only journal per OS process, flushed
// before a barrier can be observed by the driver. Stopwatch is system-wide
// on the same machine, so intervals can be compared across these processes.
public static class ProcessEvidence
{
    private static readonly object Gate = new();
    public static string Root => Environment.GetEnvironmentVariable("PROBE_DIRECTORY")
        ?? throw new InvalidOperationException("PROBE_DIRECTORY is required.");

    public static void Write(string kind, object value)
    {
        lock (Gate)
        {
            File.AppendAllText(Path.Combine(Root, "events.jsonl"), JsonSerializer.Serialize(new
            {
                Kind = kind, Ticks = Stopwatch.GetTimestamp(), Pid = Environment.ProcessId, Value = value
            }) + Environment.NewLine);
        }
    }

    public static async Task WaitAsync(string barrier, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(60));
        while (!File.Exists(Path.Combine(Root, "release-" + barrier)))
            await Task.Delay(20, budget.Token);
    }
}

internal sealed class ObservedDirectory(IActorDirectory inner) : IActorDirectory
{
    public ValueTask<ActorDirectoryRecord?> ResolveAsync(ActorId id, CancellationToken token = default) =>
        inner.ResolveAsync(id, token);

    public ValueTask<ActorActivationAcquireResult> AcquireAsync(
        ActorId id, NodeReference owner, ActorActivationId activation, CancellationToken token = default) =>
        inner.AcquireAsync(id, owner, activation, token);

    public async ValueTask<bool> ReleaseAsync(ActorId id, ActorActivationId activation, CancellationToken token = default)
    {
        ProcessEvidence.Write("release-enter", new { Actor = id.Value, Activation = activation.ToString() });
        var released = await inner.ReleaseAsync(id, activation, token);
        ProcessEvidence.Write("release-exit", new { Actor = id.Value, Activation = activation.ToString(), Released = released });
        return released;
    }
}
