using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionEstablishedAcknowledgementsTests
{
    [Fact]
    public async Task Wait_does_not_complete_until_the_connection_acknowledges()
    {
        var acknowledgements = new GameSessionEstablishedAcknowledgements();

        var wait = acknowledgements.WaitAsync("connection-a", TestContext.Current.CancellationToken).AsTask();
        Assert.False(wait.IsCompleted);

        Assert.True(acknowledgements.Acknowledge("connection-a"));
        await wait;
    }

    [Fact]
    public async Task Canceled_wait_is_removed_and_cannot_be_acknowledged_later()
    {
        var acknowledgements = new GameSessionEstablishedAcknowledgements();
        using var cancellation = new CancellationTokenSource();
        var wait = acknowledgements.WaitAsync("connection-a", cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.False(acknowledgements.Acknowledge("connection-a"));
    }
}
