using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.App.Chat
{
    internal sealed class LoginServiceProxy : ILoginService
    {
        private readonly IHotfixServiceInvoker _hotfix;
        private readonly IActorRuntime _actors;
        private readonly ILoginCallback _callback;
        private readonly string _connectionId;

        public LoginServiceProxy(IHotfixServiceInvoker hotfix, IActorRuntime actors, ILoginCallback callback, string connectionId)
        {
            _hotfix = hotfix;
            _actors = actors;
            _callback = callback;
            _connectionId = connectionId;
        }

        public ValueTask<LoginReply> LoginAsync(LoginRequest req)
        {
            return _hotfix.InvokeAsync<ILoginService, LoginServiceCall, LoginReply>(
                nameof(LoginAsync),
                new LoginServiceCall(_actors, _connectionId, _callback, req));
        }
    }
}
