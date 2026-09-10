using System.IO.Pipes;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubWindowLifetimeTests
{
    [Fact]
    public void NotifyPrimary_does_not_report_success_without_activation_acknowledgement()
    {
        var pipeName = "Lakona.Hub.Tests." + Guid.NewGuid().ToString("N");
        using var unresponsive = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        Assert.False(HubSingleInstance.NotifyPrimary(pipeName));
    }

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
    public void SecondInstance_NotifiesPrimaryToActivateItsWindow()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $"Lakona.Hub.Tests.{suffix}";
        var pipeName = $"Lakona.Hub.Tests.{suffix}";
        using var primary = HubSingleInstance.Acquire(mutexName, pipeName);
        using var secondary = HubSingleInstance.Acquire(mutexName, pipeName);
        var activationCount = 0;

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        primary.StartListening(() => Interlocked.Increment(ref activationCount));

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            Assert.True(secondary.NotifyPrimary());
            Assert.Equal(attempt, Volatile.Read(ref activationCount));
        }
    }
}
