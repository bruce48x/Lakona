using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class GameSessionResumeTicketStoreTests
{
    [Fact]
    public async Task Ticket_is_opaque_and_resolves_exactly_one_session_generation()
    {
        var store = new InMemoryGameSessionResumeTicketStore();
        var session = new GameSessionKey("player-a", "session-a", 7);

        var ticket = await store.IssueAsync(session, "websocket|memorypack|reliable", TestContext.Current.CancellationToken);
        var resolved = await store.ResolveAsync(ticket, "websocket|memorypack|reliable", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("player-a", ticket, StringComparison.Ordinal);
        Assert.DoesNotContain("session-a", ticket, StringComparison.Ordinal);
        Assert.Equal(session, resolved);
    }

    [Fact]
    public async Task Reissuing_a_ticket_for_the_same_generation_is_stable()
    {
        var store = new InMemoryGameSessionResumeTicketStore();
        var session = new GameSessionKey("player-a", "session-a", 1);

        var first = await store.IssueAsync(session, "websocket|memorypack|reliable", TestContext.Current.CancellationToken);
        var second = await store.IssueAsync(session, "websocket|memorypack|reliable", TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Revoking_a_session_removes_its_ticket()
    {
        var store = new InMemoryGameSessionResumeTicketStore();
        var session = new GameSessionKey("player-a", "session-a", 1);
        var ticket = await store.IssueAsync(session, "websocket|memorypack|reliable", TestContext.Current.CancellationToken);

        await store.RevokeAsync(session, TestContext.Current.CancellationToken);

        Assert.Null(await store.ResolveAsync(ticket, "websocket|memorypack|reliable", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ticket_cannot_resume_on_a_different_endpoint_scope()
    {
        var store = new InMemoryGameSessionResumeTicketStore();
        var session = new GameSessionKey("player-a", "session-a", 1);
        var ticket = await store.IssueAsync(session, "websocket|memorypack|reliable", TestContext.Current.CancellationToken);

        var resolved = await store.ResolveAsync(
            ticket,
            "kcp|memorypack|best-effort",
            TestContext.Current.CancellationToken);

        Assert.Null(resolved);
    }
}
