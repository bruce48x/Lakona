using Lakona.Game.Server.LocalAdmin;
using Xunit;

namespace Lakona.Game.Server.Tests.LocalAdmin;

public sealed class LakonaLocalAdminHostedServiceTests
{
    [Theory]
    [InlineData("127.0.0.1", 20090, "http://127.0.0.1:20090/")]
    [InlineData("localhost", 20090, "http://localhost:20090/")]
    [InlineData("::1", 20090, "http://[::1]:20090/")]
    public void FormatPrefix_brackets_ipv6_hosts(string host, int port, string expected)
    {
        Assert.Equal(expected, LakonaLocalAdminHostedService.FormatPrefixForTesting(host, port));
    }

    [Fact]
    public async Task Request_tracker_waits_for_in_flight_handlers_to_finish()
    {
        var tracker = new LakonaLocalAdminRequestTracker();
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = false;

        _ = tracker.Track(async () =>
        {
            await unblock.Task;
            observed = true;
        });

        var drainTask = tracker.DrainAsync(TestContext.Current.CancellationToken);
        Assert.False(drainTask.IsCompleted);

        unblock.SetResult();
        await drainTask;

        Assert.True(observed);
    }
}
