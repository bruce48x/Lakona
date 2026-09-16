using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixActivationTrackerTests
{
    [Fact]
    public void Failure_does_not_poison_later_resolution()
    {
        var tracker = new HotfixActivationTracker();
        var registration = ServiceDescriptor.Singleton<object, object>();
        using (tracker.Enter(registration))
            Assert.Contains("dependency cycle", Assert.Throws<InvalidOperationException>(() => tracker.Enter(registration)).Message);
        using var later = tracker.Enter(registration);
    }

    [Fact]
    public void Different_registrations_of_same_type_are_not_a_cycle()
    {
        var tracker = new HotfixActivationTracker();
        using var first = tracker.Enter(ServiceDescriptor.Singleton<object, object>());
        using var second = tracker.Enter(ServiceDescriptor.Singleton<object, object>());
    }

    [Fact]
    public async Task Independent_concurrent_activations_are_not_a_cycle()
    {
        var tracker = new HotfixActivationTracker();
        var registration = ServiceDescriptor.Singleton<object, object>();
        using var barrier = new Barrier(2);
        Task Activate() => Task.Run(() =>
        {
            using var activation = tracker.Enter(registration);
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
            }, TestContext.Current.CancellationToken);
        await Task.WhenAll(Activate(), Activate()).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Captured_context_does_not_retain_completed_activation_as_a_cycle()
    {
        var tracker = new HotfixActivationTracker();
        var registration = ServiceDescriptor.Singleton<object, object>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task later;
        using (tracker.Enter(registration))
        {
            later = Task.Run(async () =>
            {
                await release.Task;
                using var activation = tracker.Enter(registration);
            }, TestContext.Current.CancellationToken);
        }
        release.SetResult();
        await later.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
