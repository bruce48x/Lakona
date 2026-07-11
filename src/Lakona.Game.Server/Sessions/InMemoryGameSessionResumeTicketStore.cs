using System.Security.Cryptography;
using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal sealed class InMemoryGameSessionResumeTicketStore : IGameSessionResumeTicketStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, GameSessionKey> sessionsByTicket = new(StringComparer.Ordinal);
    private readonly Dictionary<GameSessionKey, string> ticketsBySession = [];

    public ValueTask<string> IssueAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (ticketsBySession.TryGetValue(session, out var existing))
            {
                return new ValueTask<string>(existing);
            }

            string ticket;
            do
            {
                ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            }
            while (sessionsByTicket.ContainsKey(ticket));

            ticketsBySession.Add(session, ticket);
            sessionsByTicket.Add(ticket, session);
            return new ValueTask<string>(ticket);
        }
    }

    public ValueTask<GameSessionKey?> ResolveAsync(
        string ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticket);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return new ValueTask<GameSessionKey?>(
                sessionsByTicket.TryGetValue(ticket, out var session) ? session : null);
        }
    }

    public ValueTask RevokeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (ticketsBySession.Remove(session, out var ticket))
            {
                sessionsByTicket.Remove(ticket);
            }
        }

        return default;
    }
}
