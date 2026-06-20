using Server.App.Hotfix;
using Lakona.Game.Server.Sessions;

namespace Server.App.Hosting
{
    internal sealed class ChatSessionLifecycleBridge : IGameSessionLifecycleHandler
    {
        private readonly ChatHotfixRuntimeEvents _hotfixEvents;

        public ChatSessionLifecycleBridge(ChatHotfixRuntimeEvents hotfixEvents)
        {
            _hotfixEvents = hotfixEvents;
        }

        public ValueTask OnConnectionOpenedAsync(
            GameConnectionContext context,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionBoundAsync(
            GameSessionBindingContext context,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(
            GameSessionBindingContext context,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionExpiredAsync(
            GameSessionBindingContext context,
            CancellationToken cancellationToken = default)
        {
            return _hotfixEvents.SessionExpiredAsync(context.ConnectionId, cancellationToken);
        }

        public ValueTask OnSessionTerminatedAsync(
            GameSessionTerminationContext context,
            CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
