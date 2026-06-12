using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;

namespace Lakona.Game.Server.Hotfix;

public interface IHotfixManager
{
    HotfixSnapshot Current { get; }

    ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default);

    ValueTask<HotfixReloadResult> ValidateAsync(
        IHotfixAssemblySource source,
        CancellationToken cancellationToken = default);

    ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default);
}
