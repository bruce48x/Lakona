using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Shared.Gameplay;

namespace Server.App.Hotfix;

internal sealed class AgarHotfixRuntimeEvents(IServiceProvider services)
{
    public ValueTask TickMatchmakingAsync(DateTime observedAtUtc, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(
            AgarRuntimeMethodIds.TickMatchmaking,
            new AgarMatchmakingTickRequest { ObservedAtUtc = observedAtUtc },
            cancellationToken);
    }

    public ValueTask CommitRoomSettlementAsync(
        string roomId,
        string settlementId,
        DateTime finishedAtUtc,
        int tick,
        MatchSettlementResult settlement,
        CancellationToken cancellationToken = default)
    {
        return InvokeAsync(
            AgarRuntimeMethodIds.CommitRoomSettlement,
            new AgarRoomSettlementRequest
            {
                RoomId = roomId,
                SettlementId = settlementId,
                FinishedAtUtc = finishedAtUtc,
                Tick = tick,
                Settlement = settlement
            },
            cancellationToken);
    }

    private ValueTask InvokeAsync<TRequest>(
        int methodId,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var hotfix = services.GetRequiredService<IHotfixServiceInvoker>();
        return hotfix.InvokeAsync<IAgarRuntimeService, HotfixServiceCall<TRequest>>(
            methodId,
            new HotfixServiceCall<TRequest>(
                request,
                string.Empty,
                services,
                services.GetRequiredService<IActorRuntime>(),
                services.GetRequiredService<ILakonaGameServer>()),
            cancellationToken);
    }
}
