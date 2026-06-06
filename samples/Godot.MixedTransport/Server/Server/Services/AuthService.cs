using Shared.Interfaces;

namespace Agar.MixedTransport.Server.Services;

public sealed class AuthService : IAuthService
{
    private readonly LoginTicketStore _loginTickets;
    private readonly int _kcpPort;

    public AuthService(LoginTicketStore loginTickets, int kcpPort)
    {
        _loginTickets = loginTickets;
        _kcpPort = kcpPort;
    }

    public ValueTask<LoginReply> LoginAsync(LoginRequest request)
    {
        var grant = _loginTickets.Issue(request.Account);
        return ValueTask.FromResult(new LoginReply
        {
            Code = 0,
            Message = $"Welcome {grant.Account}. TCP login succeeded; switch to KCP battle.",
            Token = grant.Token,
            Conv = grant.Conv,
            KcpHost = string.Empty,
            KcpPort = _kcpPort,
            PlayerId = grant.PlayerId
        });
    }
}
