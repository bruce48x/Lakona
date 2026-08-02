using Server.App.Routing;
using Server.App.Rooms;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.Logging;
namespace Server.Hotfix.Rooms;

[HotfixTimer]
public sealed partial class BattleRuntimeTimerCallbacks
{
    private readonly ActorAccess _actors;
    private readonly ILogger<BattleRuntimeTimerCallbacks> _logger;

    public BattleRuntimeTimerCallbacks(
        ActorAccess actors,
        ILogger<BattleRuntimeTimerCallbacks> logger)
    {
        _actors = actors;
        _logger = logger;
    }

    public async ValueTask TickAsync(TimerTick<FrameRelayTimerArgs> tick)
    {
        if (string.IsNullOrWhiteSpace(tick.Args.RoomId))
        {
            _logger.LogDebug("Battle runtime timer tick skipped because no room id was provided.");
            return;
        }

        await _actors
            .Local<RoomActor>(new RoomId(tick.Args.RoomId))
            .PostAsync(
                static behavior => behavior.RunFrameAsync,
                new RoomFrameRequest
            {
                ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime
            },
                tick.CancellationToken)
            .ConfigureAwait(false);
    }
}
