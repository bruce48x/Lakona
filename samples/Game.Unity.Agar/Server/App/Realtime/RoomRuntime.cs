using Agar.Sample.State.Contracts.Rooms;
using Server.App.Hotfix;
using Server.App.Services;
using Shared.Gameplay;
using Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace Server.App.Realtime;

internal sealed class RoomRuntime : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _gate = new();
    private readonly SessionDirectory _sessionDirectory;
    private readonly AgarHotfixRuntimeEvents _hotfixEvents;
    private readonly ArenaSimulation _simulation;
    private readonly string _roomId;
    private readonly ILogger<RoomRuntime> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;
    private bool _matchCommitted;

    public RoomRuntime(
        RoomSnapshot room,
        SessionDirectory sessionDirectory,
        AgarHotfixRuntimeEvents hotfixEvents,
        ILogger<RoomRuntime> logger)
    {
        _roomId = room.RoomId;
        _sessionDirectory = sessionDirectory;
        _hotfixEvents = hotfixEvents;
        _logger = logger;
        _simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            Arena = ArenaConfig.CreateDefault(),
            RespawnDelaySeconds = 5f,
            TargetParticipantCount = room.MaxPlayers,
            MinPlayersToStart = room.MaxPlayers,
            EnableBots = true
        });

        foreach (var player in room.Players)
        {
            _simulation.UpsertPlayer(new ArenaPlayerRegistration
            {
                PlayerId = player.UserId,
                PreferredSpawnIndex = player.SeatIndex,
                IsBot = false
            });
        }

        _loopTask = RunAsync(_cts.Token);
    }

    public ValueTask AddOrUpdatePlayerAsync(string playerId)
    {
        lock (_gate)
        {
            if (!_simulation.TryGetPlayerSnapshot(playerId, out _))
            {
                _simulation.UpsertPlayer(new ArenaPlayerRegistration
                {
                    PlayerId = playerId,
                    PreferredSpawnIndex = -1,
                    IsBot = false
                });
            }
        }

        return default;
    }

    public ValueTask SubmitInputAsync(string playerId, InputMessage input)
    {
        lock (_gate)
        {
            input.PlayerId = playerId;
            _simulation.SubmitInput(input);
        }

        return default;
    }

    public ValueTask<bool> RemovePlayerAsync(string playerId)
    {
        lock (_gate)
        {
            _simulation.RemovePlayer(playerId, out _);
            var remaining = _sessionDirectory.GetByRoom(_roomId)
                .Count(registration => !string.Equals(registration.PlayerId, playerId, StringComparison.Ordinal));
            return new ValueTask<bool>(remaining == 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Room runtime {RoomId} dispose cancelled.", _roomId);
        }
        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                ArenaStepResult result;

                lock (_gate)
                {
                    result = TickSimulation((float)TickInterval.TotalSeconds);
                }

                if (result.MatchEnd is null && ShouldForceRoundEnd(result.WorldState))
                {
                    result = new ArenaStepResult(
                    result.WorldState,
                    result.Deaths,
                    CreateMatchEnd(result.WorldState));
                }

                PublishWorldState(result);

                if (result.MatchEnd is not null && !_matchCommitted)
                {
                    _matchCommitted = true;
                    await PersistMatchEndAsync(result).ConfigureAwait(false);
                    _cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Room runtime loop stopped for room {RoomId}.", _roomId);
        }
    }

    private static bool ShouldForceRoundEnd(WorldState worldState)
    {
        return worldState.RoundRemainingSeconds <= 0 && worldState.Players.Count > 1;
    }

    private static MatchEnd CreateMatchEnd(WorldState worldState)
    {
        var winnerPlayerId = worldState.Players
            .OrderByDescending(static player => player.Mass)
            .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
            .FirstOrDefault()?.PlayerId ?? string.Empty;

        return new MatchEnd
        {
            WinnerPlayerId = winnerPlayerId,
            Tick = worldState.Tick
        };
    }

    private ArenaStepResult TickSimulation(float deltaTime)
    {
        return _simulation.TickWithHotfix(deltaTime);
    }

    private void PublishWorldState(ArenaStepResult result)
    {
        var registrations = _sessionDirectory.GetByRoom(_roomId);
        foreach (var registration in registrations)
        {
            var callback = registration.GetRealtimeCallback();
            if (callback is not null)
            {
                SafeInvoke(callback, target => target.OnWorldState(result.WorldState));
            }
        }

        foreach (var deadEvent in result.Deaths)
        {
            foreach (var registration in registrations)
            {
                var callback = registration.GetRealtimeCallback();
                if (callback is not null)
                {
                    SafeInvoke(callback, target => target.OnPlayerDead(deadEvent));
                }
            }
        }

        if (result.MatchEnd is null)
        {
            return;
        }

        foreach (var registration in registrations)
        {
            var callback = registration.GetRealtimeCallback();
            if (callback is not null)
            {
                SafeInvoke(callback, target => target.OnMatchEnd(result.MatchEnd));
            }
        }
    }

    private async Task PersistMatchEndAsync(ArenaStepResult result)
    {
        var settlement = SettleMatch(result.WorldState);
        var tick = result.MatchEnd?.Tick ?? result.WorldState.Tick;
        await _hotfixEvents
            .CommitRoomSettlementAsync(
                _roomId,
                $"settlement-{_roomId}-{tick}",
                DateTime.UtcNow,
                tick,
                settlement)
            .ConfigureAwait(false);
    }

    private MatchSettlementResult SettleMatch(WorldState worldState)
    {
        return _simulation.SettleMatch(worldState);
    }

    private void SafeInvoke(IBattleCallback callback, Action<IBattleCallback> action)
    {
        try
        {
            action(callback);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push room event in room {RoomId}.", _roomId);
        }
    }

}
