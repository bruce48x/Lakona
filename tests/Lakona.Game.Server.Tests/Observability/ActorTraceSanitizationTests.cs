using System.Collections.Concurrent;
using System.Diagnostics;
using Lakona.Game.Server.Internal.ActorKernel;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class ActorTraceSanitizationTests
{
    private static readonly ActorCallOptions DefaultCallOptions = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

    [Fact]
    public void Actor_diagnostic_source_names_are_public_game_names()
    {
        Assert.Equal("Lakona.Game.Actor", LakonaActorDiagnostics.ActivitySourceName);
        Assert.Equal("Lakona.Game.Actor", LakonaActorDiagnostics.MeterName);
    }

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

    [Fact]
    public async Task Dispatch_error_activity_does_not_export_raw_exception_or_request_text()
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
        ActorRef<ThrowSecretRequest> actor = (await system.SpawnAsync("secret-actor-id", new ThrowSecretActor())).Ref;
        string messageType = typeof(ThrowSecretRequest).FullName!;

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await actor.Call<string>(
                new ThrowSecretRequest("secret-request-payload"),
                DefaultCallOptions,
                TestContext.Current.CancellationToken));
        await Eventually(() => stopped.Any(activity =>
            string.Equals(activity.GetTagItem("lakona-game.actor.message.type")?.ToString(), messageType, StringComparison.Ordinal)));

        Activity activity = stopped.Single(activity =>
            string.Equals(activity.GetTagItem("lakona-game.actor.message.type")?.ToString(), messageType, StringComparison.Ordinal));

        Assert.Contains("secret-exception-message", exception.Message, StringComparison.Ordinal);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.True(string.IsNullOrEmpty(activity.StatusDescription));
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("exception.type"));
        Assert.Null(activity.GetTagItem("exception.message"));
        AssertActivityTextDoesNotContainSecret(activity, "secret-actor-id");
        AssertActivityTextDoesNotContainSecret(activity, "secret-request-payload");
        AssertActivityTextDoesNotContainSecret(activity, "secret-exception-message");
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

    private sealed class ThrowSecretActor : IActor<ThrowSecretRequest>
    {
        public ValueTask OnMessage(ActorKernelContext<ThrowSecretRequest> ctx, ThrowSecretRequest message)
        {
            throw new InvalidOperationException(
                $"secret-exception-message actor=secret-actor-id request={message.Value}");
        }
    }

    private sealed record ThrowSecretRequest(string Value);

    private static void AssertActivityTextDoesNotContainSecret(Activity activity, string secret)
    {
        foreach (string text in EnumerateActivityText(activity))
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> EnumerateActivityText(Activity activity)
    {
        yield return activity.OperationName;
        yield return activity.DisplayName;
        yield return activity.StatusDescription ?? string.Empty;
        yield return activity.ToString() ?? string.Empty;

        foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
        {
            yield return tag.Key;
            yield return tag.Value?.ToString() ?? string.Empty;
        }

        foreach (ActivityEvent activityEvent in activity.Events)
        {
            yield return activityEvent.Name;

            foreach (KeyValuePair<string, object?> tag in activityEvent.Tags)
            {
                yield return tag.Key;
                yield return tag.Value?.ToString() ?? string.Empty;
            }
        }
    }

    private static async Task Eventually(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
