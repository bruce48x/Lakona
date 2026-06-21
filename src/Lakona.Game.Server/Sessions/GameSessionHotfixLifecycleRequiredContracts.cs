using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionHotfixLifecycleRequiredContracts : IHotfixRequiredServiceContracts
{
    public IReadOnlyList<Type> ServiceContracts { get; } =
    [
        typeof(IGameSessionLifecycle)
    ];
}
