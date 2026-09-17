using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Testing.Fixtures.Hotfix;

[HotfixLifecycle]
public sealed class SessionProbe : IGameSessionLifecycle
{
    public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call) => default;
    public ValueTask SessionResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call) => default;
    public ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call) => default;
}
