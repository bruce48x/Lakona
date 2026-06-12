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
    public async Task MultipleCallbackContractsShareOneEndpointWithoutOverwritingEachOther()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var endpoint = new SessionEndpointKey(session, "control");
        var login = new LoginCallback("login");
        var chat = new ChatCallback("chat");

        await directory.BindEndpointAsync(endpoint, "connection-a", login, TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(endpoint, "connection-a", chat, TestContext.Current.CancellationToken);

        Assert.Same(login, await directory.GetCallbackAsync<LoginCallback>(endpoint, TestContext.Current.CancellationToken));
        Assert.Same(chat, await directory.GetCallbackAsync<ChatCallback>(endpoint, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RebindingSameCallbackContractReplacesOnlyThatContract()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var endpoint = new SessionEndpointKey(session, "control");
        var firstLogin = new LoginCallback("first-login");
        var secondLogin = new LoginCallback("second-login");
        var chat = new ChatCallback("chat");

        await directory.BindEndpointAsync(endpoint, "connection-a", firstLogin, TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(endpoint, "connection-a", chat, TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(endpoint, "connection-b", secondLogin, TestContext.Current.CancellationToken);

        Assert.Same(secondLogin, await directory.GetCallbackAsync<LoginCallback>(endpoint, TestContext.Current.CancellationToken));
        Assert.Same(chat, await directory.GetCallbackAsync<ChatCallback>(endpoint, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MarkConnectionDisconnectedReturnsEachEndpointAggregateOnce()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var control = new SessionEndpointKey(session, "control");
        var realtime = new SessionEndpointKey(session, "realtime");

        await directory.BindEndpointAsync(control, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(control, "connection-a", new ChatCallback("chat"), TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(realtime, "connection-b", new RealtimeCallback("realtime"), TestContext.Current.CancellationToken);

        var disconnected = await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(disconnected);
        Assert.Equal(control, snapshot.Endpoint);
        Assert.Equal("connection-a", snapshot.ConnectionId);
        Assert.Equal(2, snapshot.CallbackContractTypes.Count);
        Assert.Contains(typeof(LoginCallback), snapshot.CallbackContractTypes);
        Assert.Contains(typeof(ChatCallback), snapshot.CallbackContractTypes);
        Assert.Null(await directory.GetCallbackAsync<LoginCallback>(control, TestContext.Current.CancellationToken));
        Assert.Null(await directory.GetCallbackAsync<ChatCallback>(control, TestContext.Current.CancellationToken));
        Assert.NotNull(await directory.GetCallbackAsync<RealtimeCallback>(realtime, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExpireDisconnectedEndpointsReturnsEachEndpointAggregateOnce()
    {
        var directory = new InMemoryGameSessionDirectory();
        var session = await directory.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        var endpoint = new SessionEndpointKey(session, "control");

        await directory.BindEndpointAsync(endpoint, "connection-a", new LoginCallback("login"), TestContext.Current.CancellationToken);
        await directory.BindEndpointAsync(endpoint, "connection-a", new ChatCallback("chat"), TestContext.Current.CancellationToken);
        await directory.MarkConnectionDisconnectedAsync("connection-a", TestContext.Current.CancellationToken);

        var expired = await directory.ExpireDisconnectedEndpointsAsync(DateTimeOffset.UtcNow.AddSeconds(1), TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(expired);
        Assert.Equal(endpoint, snapshot.Endpoint);
        Assert.Equal("connection-a", snapshot.ConnectionId);
        Assert.Equal(2, snapshot.CallbackContractTypes.Count);
        Assert.Null(await directory.GetCallbackAsync<LoginCallback>(endpoint, TestContext.Current.CancellationToken));
        Assert.Null(await directory.GetCallbackAsync<ChatCallback>(endpoint, TestContext.Current.CancellationToken));
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

    private sealed class LoginCallback
    {
        public LoginCallback(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class ChatCallback
    {
        public ChatCallback(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class RealtimeCallback
    {
        public RealtimeCallback(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
