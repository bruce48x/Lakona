using System.Diagnostics;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Actors.Internal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

public sealed class ActorPostCallChainTests
{
    [Theory]
    [InlineData("a,b")]
    [InlineData("a,b,c")]
    [InlineData("a,a,b")]
    [InlineData("a,b,a,c")]
    public async Task Posts_break_wait_dependencies_but_preserve_trace_ancestry(string path)
    {
        var token = TestContext.Current.CancellationToken;
        using var listener = ListenToActorActivities();
        using var trace = new Activity(nameof(ActorPostCallChainTests)).SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        ActorId[] route = path.Split(',').Select(id => ActorId.From("post/" + id)).ToArray();
        foreach (var id in route.Distinct()) await hosting.CreateAsync<ProbeActor>(id, token);

        var activities = new Activity[route.Length];
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask DispatchAsync(int index)
        {
            try
            {
                activities[index] = Assert.IsType<Activity>(Activity.Current);
                await Task.Yield();
                Assert.Same(activities[index], Activity.Current);
                if (index < route.Length - 1)
                {
                    Assert.Equal(ActorTellResult.Accepted, runtime.TryTell<ProbeActor>(
                        route[index + 1], (_, _) => DispatchAsync(index + 1), token));
                    return;
                }

                await release.Task.WaitAsync(token);
                var replies = 0;
                foreach (var sender in route.SkipLast(1).Distinct())
                {
                    replies += await runtime.AskAsync<ProbeActor, int>(sender, (_, _) =>
                    {
                        var replyActivity = Assert.IsType<Activity>(Activity.Current);
                        Assert.Equal(activities[index].TraceId, replyActivity.TraceId);
                        Assert.Equal(activities[index].SpanId, replyActivity.ParentSpanId);
                        return new ValueTask<int>(1);
                    }, token);
                    Assert.Same(activities[index], Activity.Current);
                }
                completed.TrySetResult(replies);
            }
            catch (Exception exception)
            {
                completed.TrySetException(exception);
                throw;
            }
        }

        try
        {
            await runtime.TellAsync<ProbeActor>(route[0], (_, _) => DispatchAsync(0), token);
            // A queued call behind each sender proves that its posted turn has exited.
            // This includes self-posts and revisits, without depending on elapsed time.
            foreach (var sender in route.SkipLast(1))
                await DrainAsync(runtime, sender, token);
            Assert.False(completed.Task.IsCompletedSuccessfully);
            release.TrySetResult();
            Assert.Equal(route.SkipLast(1).Distinct().Count(),
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), token));
            await DrainAsync(runtime, route[^1], token);

            for (var index = 0; index < activities.Length; index++)
            {
                Assert.Equal(trace.TraceId, activities[index].TraceId);
                Assert.Equal(index == 0 ? trace.SpanId : activities[index - 1].SpanId,
                    activities[index].ParentSpanId);
            }
            Assert.Same(trace, Activity.Current);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Real_call_cycles_are_rejected_including_after_posts(bool post, bool selfPost)
    {
        var token = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var first = ActorId.From("cycle/a");
        var second = ActorId.From("cycle/b");
        var sender = selfPost ? first : ActorId.From("cycle/sender");
        foreach (var id in new[] { first, second, sender }.Distinct())
            await hosting.CreateAsync<ProbeActor>(id, token);
        var completed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask CallCycleAsync()
        {
            var exception = await Record.ExceptionAsync(async () =>
                await runtime.AskAsync<ProbeActor, int>(second, async (_, _) =>
                    await runtime.AskAsync<ProbeActor, int>(first,
                        static (_, _) => new ValueTask<int>(1), token), token));
            completed.TrySetResult(exception!);
        }

        if (post)
        {
            await runtime.TellAsync<ProbeActor>(sender, (_, _) =>
            {
                Assert.Equal(ActorTellResult.Accepted,
                    runtime.TryTell<ProbeActor>(first, (_, _) => CallCycleAsync(), token));
                return default;
            }, token);
        }
        else
        {
            await runtime.TellAsync<ProbeActor>(first, (_, _) => CallCycleAsync(), token);
        }

        var exception = Assert.IsType<InvalidOperationException>(
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), token));
        Assert.Contains("Circular actor call detected", exception.Message, StringComparison.Ordinal);
        Assert.Contains("(cycle/a -> cycle/b)", exception.Message, StringComparison.Ordinal);
        // Rejection must leave both mailboxes usable.
        await DrainAsync(runtime, first, token);
        await DrainAsync(runtime, second, token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_canceled_post_does_not_leak_context_to_the_next_turn(bool canceled)
    {
        var token = TestContext.Current.CancellationToken;
        using var listener = ListenToActorActivities();
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var sender = ActorId.From("failure/sender");
        var target = ActorId.From("failure/target");
        await hosting.CreateAsync<ProbeActor>(sender, token);
        await hosting.CreateAsync<ProbeActor>(target, token);
        Activity? failedActivity = null;
        using (var origin = new Activity("failed-post").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            await runtime.TellAsync<ProbeActor>(sender, (_, _) =>
            {
                Assert.Equal(ActorTellResult.Accepted, runtime.TryTell<ProbeActor>(target, (_, _) =>
                {
                    failedActivity = Assert.IsType<Activity>(Activity.Current);
                    if (canceled) throw new OperationCanceledException(new CancellationToken(true));
                    throw new InvalidOperationException("post failed");
                }, token));
                return default;
            }, token);
            await DrainAsync(runtime, target, token);
            Assert.NotNull(failedActivity);
            Assert.Equal(origin.TraceId, failedActivity.TraceId);
            Assert.Equal(ActivityStatusCode.Error, failedActivity.Status);
            Assert.Same(origin, Activity.Current);
        }

        using var next = new Activity("next-turn").SetIdFormat(ActivityIdFormat.W3C).Start();
        await runtime.TellAsync<ProbeActor>(target, async (_, _) =>
        {
            var activity = Assert.IsType<Activity>(Activity.Current);
            Assert.Equal(next.TraceId, activity.TraceId);
            Assert.Equal(next.SpanId, activity.ParentSpanId);
            Assert.Equal(1, await runtime.AskAsync<ProbeActor, int>(sender,
                static (_, _) => new ValueTask<int>(1), token));
            Assert.Same(activity, Activity.Current);
        }, token);
        Assert.Same(next, Activity.Current);
    }

    private static ActivityListener ListenToActorActivities()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LakonaActorDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static ServiceProvider CreateProvider() => new ServiceCollection()
        .AddLakonaGameServerActors().BuildServiceProvider();

    private static async Task DrainAsync(IActorRuntime runtime, ActorId id, CancellationToken token)
    {
        await runtime.TellAsync<ProbeActor>(id, static (_, _) => default, token)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    private sealed class ProbeActor : GameActor;
}
