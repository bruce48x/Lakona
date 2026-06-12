using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.App.Chat
{
    internal sealed class ChatServiceProxy : IChatService
    {
        private readonly IHotfixServiceInvoker _hotfix;
        private readonly IActorRuntime _actors;
        private readonly IChatCallback _callback;
        private readonly string _connectionId;

        public ChatServiceProxy(IHotfixServiceInvoker hotfix, IActorRuntime actors, IChatCallback callback, string connectionId)
        {
            _hotfix = hotfix;
            _actors = actors;
            _callback = callback;
            _connectionId = connectionId;
        }

        public ValueTask BindAsync(ChatBindRequest req)
        {
            return _hotfix.InvokeAsync<IChatService, ChatServiceCall>(
                nameof(BindAsync),
                new ChatServiceCall(_actors, _connectionId, _callback, req, null));
        }

        public ValueTask SendAsync(ChatSendRequest req)
        {
            return _hotfix.InvokeAsync<IChatService, ChatServiceCall>(
                nameof(SendAsync),
                new ChatServiceCall(_actors, _connectionId, _callback, null, req));
        }
    }
}
