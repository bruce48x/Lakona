using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionDirectoryTests
{
    [Fact]
    public async Task DuplicateBindReplacesOnlyMatchingEndpoint()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var control = new SessionEndpointKey(session, "control");
        var realtime = new SessionEndpointKey(session, "realtime");
        var firstControl = new Callback("first-control");
        var secondControl = new Callback("second-control");
        var realtimeCallback = new Callback("realtime");

        await directory.BindEndpointAsync(control, "control-1", firstControl, TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(realtime, "realtime-1", realtimeCallback, TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(control, "control-2", secondControl, TestContext.Current.CancellationToken);

        Assert.Same(secondControl, await directory.GetCallbackAsync<Callback>(control, TestContext.Current.CancellationToken));
        Assert.Same(realtimeCallback, await directory.GetCallbackAsync<Callback>(realtime, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaleConnectionIdCannotDetachNewerBinding()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var endpoint = new SessionEndpointKey(session, "control");
        var callback = new Callback("new");

        await directory.BindEndpointAsync(endpoint, "old", new Callback("old"), TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(endpoint, "new", callback, TestContext.Current.CancellationToken);
        await directory.MarkEndpointDisconnectedAsync(endpoint, "old", TestContext.Current.CancellationToken);

        Assert.Same(callback, await directory.GetCallbackAsync<Callback>(endpoint, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EndpointBindingsCanBeDetachedIndependently()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var control = new SessionEndpointKey(session, "control");
        var realtime = new SessionEndpointKey(session, "realtime");
        var realtimeCallback = new Callback("realtime");

        await directory.BindEndpointAsync(control, "control-1", new Callback("control"), TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(realtime, "realtime-1", realtimeCallback, TestContext.Current.CancellationToken);
        await directory.MarkEndpointDisconnectedAsync(control, "control-1", TestContext.Current.CancellationToken);

        Assert.Null(await directory.GetCallbackAsync<Callback>(control, TestContext.Current.CancellationToken));
        Assert.Same(realtimeCallback, await directory.GetCallbackAsync<Callback>(realtime, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NewGenerationMakesOldSessionStateLost()
    {
        var directory = new InMemoryGameSessionDirectory();
        var oldSession = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var newSession = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);

        var oldDecision = await directory.TryResumeAsync(oldSession, TestContext.Current.CancellationToken);
        var newDecision = await directory.TryResumeAsync(newSession, TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.StateLost, oldDecision.Status);
        Assert.Equal(SessionResumeStatus.Resumed, newDecision.Status);
    }

    [Fact]
    public async Task TerminatedSessionResumesAsTerminated()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var notice = new SessionTerminationNotice(session, SessionTerminationReason.Policy, "Removed.");

        await directory.MarkSessionTerminatedAsync(
            session,
            notice,
            keepForResume: true,
            TestContext.Current.CancellationToken);

        var decision = await directory.TryResumeAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(SessionResumeStatus.Terminated, decision.Status);
        Assert.Same(notice, decision.Termination);
    }

    [Fact]
    public async Task BindingEndpointAfterTerminationIsRejected()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var notice = new SessionTerminationNotice(session, SessionTerminationReason.Policy);

        await directory.MarkSessionTerminatedAsync(
            session,
            notice,
            keepForResume: true,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => directory
            .BindEndpointAsync(
                new SessionEndpointKey(session, "control"),
                "connection-a",
                new Callback("control"),
                TestContext.Current.CancellationToken)
            .AsTask());
    }

    [Fact]
    public void AddSessionsRegistersDirectory()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameServerSessions();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IGameSessionDirectory>());
    }

    private sealed class Callback
    {
        public Callback(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
