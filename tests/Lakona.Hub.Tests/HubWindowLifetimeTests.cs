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
}
