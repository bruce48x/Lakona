using System.Collections.Concurrent;
using System.Diagnostics;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Actors.Internal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class ActorTraceSanitizationTests
{
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
        using ActivityListener listener = CreateListener(stopped);
        ActivitySource.AddActivityListener(listener);
        using Activity testTrace = StartTestTrace();

        await using ServiceProvider provider = CreateProvider();
        ActorActivationCatalog hosting = provider.GetRequiredService<ActorActivationCatalog>();
        IActorRuntime runtime = provider.GetRequiredService<IActorRuntime>();
        ActorId id = ActorId.From("secret-actor-id");
        await hosting.CreateAsync<TraceProbeActor>(id, TestContext.Current.CancellationToken);
        stopped.Clear();

        string response = await runtime.AskAsync<TraceProbeActor, string>(
            id,
            static (_, _) => new ValueTask<string>("trace-me"),
            TestContext.Current.CancellationToken);
        await Eventually(() => stopped.Any(activity =>
            IsCallbackDispatch(activity) && activity.TraceId == testTrace.TraceId));

        Activity activity = Assert.Single(stopped, activity =>
            IsCallbackDispatch(activity) && activity.TraceId == testTrace.TraceId);
        Assert.Equal("trace-me", response);
        Assert.Null(activity.GetTagItem("lakona-actor.actor.id"));
        Assert.Null(activity.GetTagItem("lakona-actor.call.chain"));
        Assert.Null(activity.GetTagItem("lakona.game.actor.actor.id"));
        Assert.Null(activity.GetTagItem("lakona.game.actor.call.chain"));
        Assert.Equal(typeof(TraceProbeActor).FullName, activity.GetTagItem("lakona.game.actor.type"));
        Assert.NotNull(activity.GetTagItem("lakona.game.actor.message.type"));
        Assert.Equal("call", activity.GetTagItem("lakona.game.actor.message.kind"));
        AssertActivityTextDoesNotContainSecret(activity, id.Value);
    }

    [Fact]
    public async Task Dispatch_error_activity_does_not_export_raw_exception_or_request_text()
    {
        ConcurrentQueue<Activity> stopped = new();
        using ActivityListener listener = CreateListener(stopped);
        ActivitySource.AddActivityListener(listener);
        using Activity testTrace = StartTestTrace();

        await using ServiceProvider provider = CreateProvider();
        ActorActivationCatalog hosting = provider.GetRequiredService<ActorActivationCatalog>();
        IActorRuntime runtime = provider.GetRequiredService<IActorRuntime>();
        ActorId id = ActorId.From("secret-actor-id");
        await hosting.CreateAsync<TraceProbeActor>(id, TestContext.Current.CancellationToken);
        stopped.Clear();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.AskAsync<TraceProbeActor, string>(
                id,
                static (_, _) => throw new InvalidOperationException(
                    "secret-exception-message request=secret-request-payload"),
                TestContext.Current.CancellationToken));
        await Eventually(() => stopped.Any(activity =>
            IsCallbackDispatch(activity) &&
            activity.TraceId == testTrace.TraceId &&
            activity.Status == ActivityStatusCode.Error));

        Activity activity = Assert.Single(stopped, activity =>
            IsCallbackDispatch(activity) &&
            activity.TraceId == testTrace.TraceId &&
            activity.Status == ActivityStatusCode.Error);
        Assert.Contains("secret-exception-message", exception.Message, StringComparison.Ordinal);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.True(string.IsNullOrEmpty(activity.StatusDescription));
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("exception.type"));
        Assert.Null(activity.GetTagItem("exception.message"));
        AssertActivityTextDoesNotContainSecret(activity, id.Value);
        AssertActivityTextDoesNotContainSecret(activity, "secret-request-payload");
        AssertActivityTextDoesNotContainSecret(activity, "secret-exception-message");
    }

    private static ActivityListener CreateListener(ConcurrentQueue<Activity> stopped)
    {
        return new ActivityListener
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
    }

    private static ServiceProvider CreateProvider()
    {
        return new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
    }

    private static Activity StartTestTrace()
    {
        var activity = new Activity(nameof(ActorTraceSanitizationTests));
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        return activity;
    }

    private sealed class TraceProbeActor : GameActor;

    private static bool IsCallbackDispatch(Activity activity)
    {
        return activity.GetTagItem("lakona.game.actor.message.type")?.ToString()
            ?.StartsWith("System.Func", StringComparison.Ordinal) == true;
    }

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
