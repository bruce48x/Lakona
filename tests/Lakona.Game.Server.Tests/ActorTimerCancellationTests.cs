using Lakona.Game.Server.Hotfix.Timers;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed partial class ActorTimerTests
{
    [Fact]
    public async Task Destroy_rejects_another_owner_and_is_idempotent_for_its_owner()
    {
        await using var fixture = new Fixture();
        var owner = await fixture.CreateActorAsync("owner");
        var other = await fixture.CreateActorAsync("other");
        var backend = fixture.Backend;
        var timer = await fixture.Catalog.AskAsync<TimerActor, TimerId>(owner.Context.Id, (self, _) =>
        {
            using var lease = fixture.AcquireCurrent();
            using var scope = LakonaTimerRuntime.Enter(backend, lease);
            return new ValueTask<TimerId>(self.CreateOnceTimer(static (TimerBehavior b) => b.TickAsync,
                TimeSpan.FromDays(1), 1));
        }, TestCancellation);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CancelAsync(fixture, other, timer, backend));
        Assert.Contains("another Actor activation", error.Message);
        Assert.True(fixture.Scheduler.Contains(timer));
        await CancelAsync(fixture, owner, timer);
        await CancelAsync(fixture, owner, timer);
        await CancelAsync(fixture, owner, default);
        Assert.False(fixture.Scheduler.Contains(timer));
    }

    [Fact]
    public async Task Same_actor_id_does_not_grant_another_activation_cancellation_rights()
    {
        await using var first = new Fixture();
        await using var second = new Fixture();
        var owner = await first.CreateActorAsync("same-id");
        var impostor = await second.CreateActorAsync("same-id");
        Assert.Equal(owner.Context.Id, impostor.Context.Id);
        var timer = await first.CreateTimerAsync(owner, TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CancelAsync(second, impostor, timer, first.Backend));
        Assert.True(first.Scheduler.Contains(timer));
        await CancelAsync(first, owner, timer);
        Assert.False(first.Scheduler.Contains(timer));
    }

    [Fact]
    public async Task Destroy_requires_owner_turn_and_hotfix_scope()
    {
        await using var fixture = new Fixture();
        var owner = await fixture.CreateActorAsync("scope");
        var timer = await fixture.CreateTimerAsync(owner, TimeSpan.FromDays(1));
        using (var lease = fixture.AcquireCurrent())
        using (LakonaTimerRuntime.Enter(fixture.Backend, lease))
            Assert.Throws<InvalidOperationException>(() => owner.DestroyTimer(timer));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Catalog.AskAsync<TimerActor, int>(owner.Context.Id, async (self, _) =>
            {
                self.DestroyTimer(timer);
                return 0;
            }, TestCancellation));
        await CancelAsync(fixture, owner, timer);
    }

    [Fact]
    public async Task Owner_can_repeat_cancellation_while_its_callback_is_still_running()
    {
        await using var fixture = new Fixture();
        var owner = await fixture.CreateActorAsync("self-cancel-twice");
        var completed = Signal();
        fixture.Probe.OnTick = async (self, tick) =>
        {
            self.DestroyTimer(tick.TimerId);
            await Task.Yield();
            self.DestroyTimer(tick.TimerId);
            completed.TrySetResult();
        };
        await fixture.CreateTimerAsync(owner, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await fixture.StartAsync();
        await Wait(completed.Task);
        await fixture.Scheduler.StopAsync(TestCancellation);
        Assert.Single(fixture.Probe.Calls);
    }

    private static async Task CancelAsync(Fixture fixture, TimerActor actor, TimerId timer,
        ILakonaTimerBackend? backend = null) =>
        await fixture.Catalog.AskAsync<TimerActor, int>(actor.Context.Id, async (self, _) =>
        {
            using var lease = fixture.AcquireCurrent();
            using var scope = LakonaTimerRuntime.Enter(backend ?? fixture.Backend, lease);
            self.DestroyTimer(timer);
            return 0;
        }, TestCancellation);
}
