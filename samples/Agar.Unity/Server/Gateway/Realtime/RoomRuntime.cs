using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State;
using Gateway.Services;
using Shared.Gameplay;
using Shared.Interfaces;
using Microsoft.Extensions.Logging;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Gateway.Realtime;

internal sealed class RoomRuntime : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _gate = new();
    private readonly SessionDirectory _sessionDirectory;
    private readonly IRoomStateStore _rooms;
    private readonly IPlayerSessionStateStore _sessions;
    private readonly IUserStateStore _users;
    private readonly ILeaderboardStateStore _leaderboard;
    private readonly ArenaSimulation _simulation;
    private readonly string _roomId;
    private readonly ILogger<RoomRuntime> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;
    private bool _matchCommitted;
    private bool _hotfixFallbackLogged;

    public RoomRuntime(
        RoomSnapshot room,
        SessionDirectory sessionDirectory,
        IRoomStateStore rooms,
        IPlayerSessionStateStore sessions,
        IUserStateStore users,
        ILeaderboardStateStore leaderboard,
        ILogger<RoomRuntime> logger)
    {
        _roomId = room.RoomId;
        _sessionDirectory = sessionDirectory;
        _rooms = rooms;
        _sessions = sessions;
        _users = users;
        _leaderboard = leaderboard;
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

        return ValueTask.CompletedTask;
    }

    public ValueTask SubmitInputAsync(string playerId, InputMessage input)
    {
        lock (_gate)
        {
            input.PlayerId = playerId;
            _simulation.SubmitInput(input);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RemovePlayerAsync(string playerId)
    {
        lock (_gate)
        {
            _simulation.RemovePlayer(playerId, out _);
            var remaining = _sessionDirectory.GetByRoom(_roomId)
                .Count(registration => !string.Equals(registration.PlayerId, playerId, StringComparison.Ordinal));
            return ValueTask.FromResult(remaining == 0);
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
        try
        {
            return _simulation.TickWithHotfix(deltaTime);
        }
        catch (HotfixMethodNotLoadedException ex)
        {
            LogHotfixFallback(ex, "tick");
            return _simulation.Tick(deltaTime);
        }
    }

    private void PublishWorldState(ArenaStepResult result)
    {
        var registrations = _sessionDirectory.GetByRoom(_roomId);
        foreach (var registration in registrations)
        {
            var callback = registration.GetRealtimePreferredCallback();
            if (callback is not null)
            {
                SafeInvoke(callback, target => target.OnWorldState(result.WorldState));
            }
        }

        foreach (var deadEvent in result.Deaths)
        {
            foreach (var registration in registrations)
            {
                var callback = registration.GetRealtimePreferredCallback();
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
            var callback = registration.GetRealtimePreferredCallback();
            if (callback is not null)
            {
                SafeInvoke(callback, target => target.OnMatchEnd(result.MatchEnd));
            }
        }
    }

    private async Task PersistMatchEndAsync(ArenaStepResult result)
    {
        var settlement = SettleMatch(result.WorldState);

        var winnerPlayerId = settlement.WinnerPlayerId;
        await _rooms
            .CompleteAsync(new RoomMatchCompletion
            {
                RoomId = _roomId,
                SettlementId = $"settlement-{_roomId}-{result.MatchEnd?.Tick ?? result.WorldState.Tick}",
                FinishedAtUtc = DateTime.UtcNow,
                WinnerUserId = winnerPlayerId,
                Reason = settlement.Reason,
                Results = settlement.Entries.Select(entry => new RoomSettlementEntry
                {
                    UserId = entry.PlayerId,
                    Rank = entry.Rank,
                    Mass = entry.Mass,
                    IsWinner = entry.IsWinner
                }).ToList()
            })
            .ConfigureAwait(false);

        var registrations = _sessionDirectory.GetByRoom(_roomId);
        foreach (var registration in registrations)
        {
            _sessionDirectory.ClearRoom(registration.PlayerId, _roomId);
            await _sessions
                .ClearRoomAsync(new PlayerRoomClearRequest
                {
                    UserId = registration.PlayerId,
                    RoomId = _roomId,
                    ClearedAtUtc = DateTime.UtcNow,
                    Reason = "Match completed."
                })
                .ConfigureAwait(false);
        }

        var winnerEntry = settlement.Entries.FirstOrDefault(static entry => entry.IsWinner);
        if (winnerEntry is not null && !winnerEntry.IsBot)
        {
            await _users.AddWinAsync(winnerEntry.PlayerId).ConfigureAwait(false);
        }

        foreach (var entry in settlement.Entries.Where(static entry => !entry.IsBot && entry.VictoryPoints > 0))
        {
            await _users.AddVictoryPointsAsync(entry.PlayerId, entry.VictoryPoints).ConfigureAwait(false);
            var profile = await _users.GetProfileAsync(entry.PlayerId).ConfigureAwait(false);
            await _leaderboard
                .RecordVictoryPointsAsync(entry.PlayerId, profile.VictoryPoints, profile.WinCount)
                .ConfigureAwait(false);
            _logger.LogInformation("Awarded {VictoryPoints} victory points to {PlayerId} for rank {Rank} in room {RoomId}.",
                entry.VictoryPoints,
                entry.PlayerId,
                entry.Rank,
                _roomId);
        }
    }

    private MatchSettlementResult SettleMatch(WorldState worldState)
    {
        try
        {
            return _simulation.SettleMatch(worldState);
        }
        catch (HotfixMethodNotLoadedException ex)
        {
            LogHotfixFallback(ex, "settlement");
            return CreateStableSettlement(worldState);
        }
    }

    private MatchSettlementResult CreateStableSettlement(WorldState worldState)
    {
        var rankedPlayers = worldState.Players
            .OrderByDescending(static player => player.Mass)
            .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
            .ToArray();

        var winnerPlayerId = rankedPlayers.FirstOrDefault()?.PlayerId ?? "";
        var settlement = new MatchSettlementResult
        {
            WinnerPlayerId = winnerPlayerId,
            Reason = "Round timer elapsed."
        };

        for (var index = 0; index < rankedPlayers.Length; index++)
        {
            var player = rankedPlayers[index];
            var rank = index + 1;
            var isBot = VictoryPointAwards.IsBotPlayer(player.PlayerId);
            settlement.Entries.Add(new MatchSettlementEntry
            {
                PlayerId = player.PlayerId,
                Rank = rank,
                Mass = NormalizeRankingMass(player.Mass),
                IsWinner = string.Equals(player.PlayerId, winnerPlayerId, StringComparison.Ordinal),
                IsBot = isBot,
                VictoryPoints = isBot ? 0 : VictoryPointAwards.GetPointsForRank(rank)
            });
        }

        return settlement;
    }

    private void LogHotfixFallback(Exception exception, string operation)
    {
        if (_hotfixFallbackLogged)
        {
            return;
        }

        _hotfixFallbackLogged = true;
        _logger.LogWarning(
            exception,
            "Room {RoomId} is using stable {HotfixOperation} rules because hotfix dispatch is not loaded.",
            _roomId,
            operation);
    }

    private void SafeInvoke(IPlayerCallback callback, Action<IPlayerCallback> action)
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

    private static int NormalizeRankingMass(float mass)
    {
        return float.IsNaN(mass) || float.IsInfinity(mass)
            ? 0
            : Math.Max(0, (int)MathF.Round(mass, MidpointRounding.AwayFromZero));
    }
}
