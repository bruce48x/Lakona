using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Server.App.Hotfix
{
    internal sealed class ChatHotfixRuntimeEvents
    {
        private readonly IServiceProvider _services;

        public ChatHotfixRuntimeEvents(IServiceProvider services)
        {
            _services = services;
        }

        public ValueTask SessionExpiredAsync(
            string connectionId,
            CancellationToken cancellationToken = default)
        {
            var hotfix = _services.GetRequiredService<IHotfixServiceInvoker>();
            return hotfix.InvokeAsync<IChatRuntimeService, HotfixServiceCall<ChatSessionExpiredRequest>>(
                ChatRuntimeMethodIds.SessionExpired,
                new HotfixServiceCall<ChatSessionExpiredRequest>(
                    new ChatSessionExpiredRequest { ConnectionId = connectionId },
                    connectionId,
                    _services,
                    _services.GetRequiredService<IActorRuntime>(),
                    _services.GetRequiredService<ILakonaGameServer>()),
                cancellationToken);
        }
    }
}
