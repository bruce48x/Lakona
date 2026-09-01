using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubWindowLifetimeTests
{
    [Fact]
    public void Close_CancelsEveryOperationTokenAndIsIdempotent()
    {
        using var lifetime = new HubWindowLifetime();
        var token = lifetime.Token;

        lifetime.Close();
        lifetime.Close();

        Assert.True(lifetime.IsClosing);
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task SecondInstance_NotifiesPrimaryToActivateItsWindow()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $"Lakona.Hub.Tests.{suffix}";
        var pipeName = $"Lakona.Hub.Tests.{suffix}";
        using var primary = HubSingleInstance.Acquire(mutexName, pipeName);
        using var secondary = HubSingleInstance.Acquire(mutexName, pipeName);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        primary.StartListening(() => activated.TrySetResult());

        Assert.True(secondary.NotifyPrimary());
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }
}
