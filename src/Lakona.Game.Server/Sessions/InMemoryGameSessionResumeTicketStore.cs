using System.Security.Cryptography;
using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal sealed class InMemoryGameSessionResumeTicketStore : IGameSessionResumeTicketStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, TicketEntry> sessionsByTicket = new(StringComparer.Ordinal);
    private readonly Dictionary<GameSessionKey, string> ticketsBySession = [];

    public ValueTask<string> IssueAsync(
        GameSessionKey session,
        string endpointScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointScope);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (ticketsBySession.TryGetValue(session, out var existing))
            {
                if (!StringComparer.Ordinal.Equals(sessionsByTicket[existing].EndpointScope, endpointScope))
                {
                    throw new InvalidOperationException("Game session resume ticket endpoint scope cannot change.");
                }
                return new ValueTask<string>(existing);
            }

            string ticket;
            do
            {
                ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            }
            while (sessionsByTicket.ContainsKey(ticket));

            ticketsBySession.Add(session, ticket);
            sessionsByTicket.Add(ticket, new TicketEntry(session, endpointScope));
            return new ValueTask<string>(ticket);
        }
    }

    public ValueTask<GameSessionKey?> ResolveAsync(
        string ticket,
        string endpointScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticket);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointScope);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return new ValueTask<GameSessionKey?>(
                sessionsByTicket.TryGetValue(ticket, out var entry) &&
                StringComparer.Ordinal.Equals(entry.EndpointScope, endpointScope)
                    ? entry.Session
                    : null);
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

    private sealed record TicketEntry(GameSessionKey Session, string EndpointScope);
}
