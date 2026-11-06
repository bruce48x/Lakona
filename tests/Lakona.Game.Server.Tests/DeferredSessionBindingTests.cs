using System.Collections.Concurrent;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class DeferredSessionBindingTests
{
    [Theory]
    [InlineData("winner")]
    [InlineData("competitor")]
    public async Task Concurrent_first_bind_cannot_commit_or_revoke_the_winners_ticket(string competingConnection)
    {
        var ct = TestContext.Current.CancellationToken;
        var notifier = new FirstEstablishmentGate();
        var tickets = new TicketProbe();
        await using var provider = CreateProvider(notifier, tickets);
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var registry = provider.GetRequiredService<IGameSessionRegistry>();
        var policies = provider.GetRequiredService<GameConnectionDeliveryPolicyRegistry>();
        foreach (var connection in new[] { "winner", "competitor", "replacement" })
            policies.Set(connection, false, "plain-endpoint", "json");
        var connections = provider.GetRequiredService<GameHandshakeConnectionStateRegistry>();
        using var handshakeSlots = new SemaphoreSlim(0, 1);
        await using var winner = connections.RegisterPending("winner", TimeSpan.FromMinutes(1), handshakeSlots, NullLogger.Instance);
        Assert.True(winner.MarkComplete());

        var session = await server.StartSessionAsync("owner", ct);
        var binding = server.BindSessionAsync(session, "winner", ct).AsTask();
        GameSessionEstablished established;
        try
        {
            established = await notifier.First.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Null(await registry.GetCurrentSessionAsync("winner", ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                server.BindSessionAsync(session, competingConnection, ct).AsTask());
            Assert.False(winner.SessionCancellation.IsCancellationRequested);
            Assert.Equal(session, await tickets.ResolveAsync(established.ResumeTicket, "plain-endpoint", ct));
        }
        finally
        {
            notifier.Acknowledge.TrySetResult();
        }
        await binding;
        Assert.Equal(session, await registry.GetCurrentSessionAsync("winner", ct));
        Assert.Null(await registry.GetCurrentSessionAsync("competitor", ct));

        await server.BindSessionAsync(session, "winner", ct);
        await server.MarkSessionDisconnectedAsync(session, "winner", ct);
        var recovery = await provider.GetRequiredService<IGameSessionHandshakeRecoveryService>()
            .RecoverAsync(established.ResumeTicket, "replacement", "plain-endpoint", ct);
        Assert.Equal(GameSessionRecoveryStatus.Resumed, recovery.Status);
        Assert.Equal(session, await registry.GetCurrentSessionAsync("replacement", ct));
        Assert.Single(notifier.Notifications);
        Assert.Equal(1, tickets.Issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_that_resolved_a_provisional_ticket_cannot_bind_after_rollback(bool retryFirstBind)
    {
        var ct = TestContext.Current.CancellationToken;
        var notifier = new FirstEstablishmentGate();
        var tickets = new TicketProbe(pauseFirstLookup: true);
        await using var provider = CreateProvider(notifier, tickets);
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var registry = provider.GetRequiredService<IGameSessionRegistry>();
        var recovery = provider.GetRequiredService<IGameSessionHandshakeRecoveryService>();
        var policies = provider.GetRequiredService<GameConnectionDeliveryPolicyRegistry>();
        foreach (var connection in new[] { "first", "stale", "retry" })
            policies.Set(connection, false, "plain-endpoint", "json");
        var session = await server.StartSessionAsync("owner", ct);

        var firstBind = server.BindSessionAsync(session, "first", ct).AsTask();
        var notice = await notifier.First.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var handshake = recovery.RecoverAsync(notice.ResumeTicket, "stale", "plain-endpoint", ct).AsTask();
        try
        {
            Assert.Equal(session, await tickets.LookupResult.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
            notifier.Acknowledge.SetException(new TimeoutException("Establishment ACK missing."));
            await Assert.ThrowsAsync<TimeoutException>(() => firstBind);
            if (retryFirstBind)
                await server.BindSessionAsync(session, "retry", ct);
        }
        finally
        {
            notifier.Acknowledge.TrySetResult();
            tickets.ContinueLookup.TrySetResult();
        }

        Assert.Equal(GameSessionRecoveryStatus.StateLost, (await handshake).Status);
        Assert.Null(await registry.GetCurrentSessionAsync("stale", ct));
        Assert.Equal(retryFirstBind ? "retry" : null, await registry.GetConnectionIdAsync(session, ct));
        Assert.Equal(retryFirstBind ? 2 : 1, notifier.Notifications.Count);
        Assert.Equal(notifier.Notifications.Count, tickets.Issues);
        if (retryFirstBind)
        {
            var currentTicket = notifier.Notifications.Last().ResumeTicket;
            Assert.NotEqual(notice.ResumeTicket, currentTicket);
            Assert.Equal(session, await tickets.ResolveAsync(currentTicket, "plain-endpoint", ct));
        }
    }

    private static ServiceProvider CreateProvider(IGameSessionEstablishedNotifier notifier, IGameSessionResumeTicketStore tickets)
    {
        var services = new ServiceCollection();
        services.AddLakonaGameServer();
        services.UseReadySingleNodeMembership();
        services.AddSingleton(notifier);
        services.AddSingleton(tickets);
        return services.BuildServiceProvider();
    }

    private sealed class TicketProbe(bool pauseFirstLookup = false) : IGameSessionResumeTicketStore
    {
        private readonly InMemoryGameSessionResumeTicketStore store = new();
        public int Issues;
        public TaskCompletionSource<GameSessionKey?> LookupResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<string> IssueAsync(GameSessionKey session, string endpointScope, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Issues);
            return store.IssueAsync(session, endpointScope, cancellationToken);
        }

        public async ValueTask<GameSessionKey?> ResolveAsync(string ticket, string endpointScope, CancellationToken cancellationToken = default)
        {
            var result = await store.ResolveAsync(ticket, endpointScope, cancellationToken);
            if (pauseFirstLookup && LookupResult.TrySetResult(result))
                await ContinueLookup.Task.WaitAsync(cancellationToken);
            return result;
        }

        public ValueTask RevokeAsync(GameSessionKey session, CancellationToken cancellationToken = default)
            => store.RevokeAsync(session, cancellationToken);
    }
}

internal sealed class FirstEstablishmentGate : IGameSessionEstablishedNotifier
{
    public TaskCompletionSource<GameSessionEstablished> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Acknowledge { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentQueue<GameSessionEstablished> Notifications { get; } = new();

    public ValueTask NotifyAsync(string connectionId, GameSessionEstablished established, CancellationToken cancellationToken = default)
    {
        Notifications.Enqueue(established);
        return First.TrySetResult(established) ? new ValueTask(Acknowledge.Task.WaitAsync(cancellationToken)) : default;
    }
}
