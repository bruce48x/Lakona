using System.Collections.Concurrent;
using System.Diagnostics;
using Lakona.Game.Server.Internal.ActorKernel;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class ActorTraceSanitizationTests
{
    private static readonly ActorCallOptions DefaultCallOptions = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Dispatch_call_activity_uses_safe_tags_without_actor_identity_or_call_chain()
    {
        ConcurrentQueue<Activity> stopped = new();

        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == LakonaActorDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "Lakona.Actor.Actor.Dispatch")
                {
                    stopped.Enqueue(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(listener);

        await using ActorSystem system = new();
        ActorRef<TraceProbeRequest> actor = (await system.SpawnAsync("secret-actor-id", new EchoActor())).Ref;
        string messageType = typeof(TraceProbeRequest).FullName!;

        string response = await actor.Call<string>(new TraceProbeRequest("trace-me"), DefaultCallOptions, TestContext.Current.CancellationToken);
        await Eventually(() => stopped.Any(static activity =>
            string.Equals(activity.GetTagItem("lakona-game.actor.message.type")?.ToString(), typeof(TraceProbeRequest).FullName, StringComparison.Ordinal)));

        Activity activity = stopped.Single(activity =>
            string.Equals(activity.GetTagItem("lakona-game.actor.message.type")?.ToString(), messageType, StringComparison.Ordinal));

        Assert.Equal("trace-me", response);
        Assert.Null(activity.GetTagItem("lakona-actor.actor.id"));
        Assert.Null(activity.GetTagItem("lakona-actor.call.chain"));
        Assert.Null(activity.GetTagItem("lakona-game.actor.actor.id"));
        Assert.Null(activity.GetTagItem("lakona-game.actor.call.chain"));
        Assert.NotNull(activity.GetTagItem("lakona-game.actor.type"));
        Assert.Equal("call", activity.GetTagItem("lakona-game.actor.message.kind"));
    }

    private sealed class EchoActor : IActor<TraceProbeRequest>
    {
        public ValueTask OnMessage(ActorKernelContext<TraceProbeRequest> ctx, TraceProbeRequest message)
        {
            ctx.Respond(message.Value);
            return default;
        }
    }

    private sealed record TraceProbeRequest(string Value);

    private static async Task Eventually(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
