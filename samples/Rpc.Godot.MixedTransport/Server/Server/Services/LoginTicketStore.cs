using System.Collections.Concurrent;
using System.Net;

namespace Agar.MixedTransport.Server.Services;

public sealed class LoginTicketStore
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, LoginGrant> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<uint, LoginGrant> _conversations = new();
    private readonly int _kcpPort;

    public LoginTicketStore(int kcpPort)
    {
        _kcpPort = kcpPort;
    }

    public LoginGrant Issue(string account)
    {
        PruneExpired();

        var safeAccount = string.IsNullOrWhiteSpace(account) ? "guest" : account.Trim();
        uint conv;
        do
        {
            conv = CreateConversationId();
        }
        while (_conversations.ContainsKey(conv));

        var grant = new LoginGrant(
            $"player-{Guid.NewGuid():N}"[..15],
            safeAccount,
            $"ticket-{Guid.NewGuid():N}",
            conv,
            DateTimeOffset.UtcNow.Add(TicketLifetime),
            _kcpPort);

        _tokens[grant.Token] = grant;
        _conversations[grant.Conv] = grant;
        return grant;
    }

    public ValueTask<bool> AuthorizeKcpAsync(uint conversationId, IPEndPoint remoteEndPoint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        PruneExpired();
        return ValueTask.FromResult(_conversations.TryGetValue(conversationId, out var grant) && !grant.IsExpired);
    }

    public bool TryClaimBattle(string token, uint conversationId, string? remoteAddress, out LoginGrant grant)
    {
        grant = null!;
        PruneExpired();

        if (!_tokens.TryGetValue(token, out var tokenGrant) || tokenGrant.IsExpired)
            return false;

        if (!_conversations.TryGetValue(conversationId, out var convGrant) || convGrant.IsExpired)
            return false;

        if (!ReferenceEquals(tokenGrant, convGrant))
            return false;

        grant = tokenGrant;
        return true;
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _tokens)
        {
            if (pair.Value.ExpiresAtUtc > now)
                continue;

            _tokens.TryRemove(pair.Key, out _);
            _conversations.TryRemove(pair.Value.Conv, out _);
        }
    }

    private static uint CreateConversationId()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var conv = BitConverter.ToUInt32(bytes, 0);
        return conv == 0 ? 1u : conv;
    }
}

public sealed class LoginGrant
{
    public LoginGrant(string playerId, string account, string token, uint conv, DateTimeOffset expiresAtUtc, int kcpPort)
    {
        PlayerId = playerId;
        Account = account;
        Token = token;
        Conv = conv;
        ExpiresAtUtc = expiresAtUtc;
        KcpPort = kcpPort;
    }

    public string PlayerId { get; }
    public string Account { get; }
    public string Token { get; }
    public uint Conv { get; }
    public int KcpPort { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public bool IsExpired => ExpiresAtUtc <= DateTimeOffset.UtcNow;
}
