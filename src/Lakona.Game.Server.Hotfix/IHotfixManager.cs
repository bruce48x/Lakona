using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

public interface IHotfixManager
{
    HotfixSnapshot Current { get; }

    ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default);
}
