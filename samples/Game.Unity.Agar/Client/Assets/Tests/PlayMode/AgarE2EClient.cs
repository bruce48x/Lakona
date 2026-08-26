#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shared.Interfaces;
using Shared.Gameplay;

namespace SampleClient.Gameplay.Tests
{
    internal sealed class AgarE2EClient : IPlayerCallback, IBattleCallback, IAsyncDisposable
    {
        private readonly object _sync = new();
        private readonly DotArenaNetworkSession _session;
        private RealtimeConnectionInfo? _realtimeEndpoint;
        private WorldState? _lastWorldState;
        private MatchEnd? _matchEnd;
        private int _worldStateCount;
        private int _matchResultSubmitted;
        private FrameSyncSimulation? _frameSync;

        public AgarE2EClient(string account, string password)
        {
            Account = account;
            Password = password;
            _session = new DotArenaNetworkSession(_ => { });
        }

        public string Account { get; }
        public string Password { get; }
        public string PlayerId { get; private set; } = string.Empty;
        public string Token { get; private set; } = string.Empty;
        public LoginReply? LoginReply { get; private set; }
        public bool IsRealtimeConnected => _session.IsRealtimeConnected;

        public RealtimeConnectionInfo? RealtimeEndpoint
        {
            get
            {
                lock (_sync)
                {
                    return Clone(_realtimeEndpoint);
                }
            }
        }

        public WorldState? LastWorldState
        {
            get
            {
                lock (_sync)
                {
                    return _lastWorldState;
                }
            }
        }

        public MatchEnd? MatchEnd
        {
            get
            {
                lock (_sync)
                {
                    return _matchEnd;
                }
            }
        }

        public int WorldStateCount => Volatile.Read(ref _worldStateCount);

        public async Task<LoginReply> ConnectAndLoginAsync(string host, int port, string path, CancellationToken cancellationToken)
        {
            var reply = await _session.ConnectAndLoginAsync(
                host,
                port,
                path,
                Account,
                Password,
                guestLogin: false,
                this,
                cancellationToken).ConfigureAwait(false);

            LoginReply = reply;
            PlayerId = reply.PlayerId;
            Token = reply.Token;
            return reply;
        }

        public Task StartMatchmakingAsync()
        {
            return _session.StartMatchmakingAsync();
        }

        public async Task AttachRealtimeAsync(CancellationToken cancellationToken)
        {
            var endpoint = RealtimeEndpoint ?? throw new InvalidOperationException($"{Account} has no realtime endpoint.");
            var reply = await _session
                .EnsureRealtimeConnectedAsync(endpoint, this, cancellationToken)
                .ConfigureAwait(false);
            if (reply == null)
            {
                throw new InvalidOperationException($"Realtime attach failed for {PlayerId}.");
            }

            if (reply.FrameSyncStart != null)
            {
                OnFrameSyncStarted(reply.FrameSyncStart);
            }

            foreach (var frame in reply.ReplayFrames.OrderBy(static frame => frame.Frame))
            {
                OnFrame(frame);
            }
        }

        public Task SubmitInputAsync(InputMessage input)
        {
            return _session.SubmitInputAsync(input);
        }

        public async Task SubmitMatchResultAsync()
        {
            if (Interlocked.Exchange(ref _matchResultSubmitted, 1) != 0)
            {
                return;
            }

            WorldState worldState;
            string roomId;
            string matchId;
            lock (_sync)
            {
                if (_matchEnd == null || _lastWorldState == null || _frameSync == null)
                {
                    Interlocked.Exchange(ref _matchResultSubmitted, 0);
                    throw new InvalidOperationException($"{PlayerId} cannot submit a match result before MatchEnd.");
                }

                worldState = _lastWorldState;
                roomId = _frameSync.RoomId;
                matchId = _frameSync.MatchId;
            }

            var settlement = MatchSettlementRules.Settle(worldState);
            var report = new FrameSyncMatchResult
            {
                RoomId = roomId,
                MatchId = matchId,
                Frame = worldState.Tick,
                WinnerPlayerId = settlement.WinnerPlayerId
            };
            foreach (var entry in settlement.Entries)
            {
                report.Players.Add(new FrameSyncPlayerResult
                {
                    PlayerId = entry.PlayerId,
                    Rank = entry.Rank,
                    Mass = entry.Mass,
                    IsWinner = entry.IsWinner
                });
            }

            await _session.SubmitMatchResultAsync(report).ConfigureAwait(false);
        }

        public Task<LeaderboardReply> GetLeaderboardAsync(int topN)
        {
            return _session.GetLeaderboardAsync(topN);
        }

        public InputMessage BuildInput()
        {
            var world = LastWorldState;
            var player = world?.Players.FirstOrDefault(item => string.Equals(item.PlayerId, PlayerId, StringComparison.Ordinal));
            if (world == null || player == null)
            {
                return new InputMessage { PlayerId = PlayerId };
            }

            var targetX = 0f;
            var targetY = 0f;
            var nearestDistance = float.MaxValue;
            foreach (var pickup in world.Pickups)
            {
                var dx = pickup.X - player.X;
                var dy = pickup.Y - player.Y;
                var distance = (dx * dx) + (dy * dy);
                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = distance;
                targetX = pickup.X;
                targetY = pickup.Y;
            }

            var moveX = targetX - player.X;
            var moveY = targetY - player.Y;
            var length = MathF.Sqrt((moveX * moveX) + (moveY * moveY));
            if (length > 0.001f)
            {
                moveX /= length;
                moveY /= length;
            }

            return new InputMessage
            {
                PlayerId = PlayerId,
                MoveX = moveX,
                MoveY = moveY,
                LastReceivedServerTick = world.Tick
            };
        }

        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            if (matchmakingStatus.State != MatchmakingState.Matched || matchmakingStatus.RealtimeConnection == null)
            {
                return;
            }

            lock (_sync)
            {
                _realtimeEndpoint = Clone(matchmakingStatus.RealtimeConnection);
            }
        }

        public void OnMatchProgress(MatchProgressUpdate update)
        {
        }

        public void OnFrameSyncStarted(FrameSyncStart start)
        {
            lock (_sync)
            {
                if (_frameSync != null && string.Equals(_frameSync.MatchId, start.MatchId, StringComparison.Ordinal))
                {
                    return;
                }

                _frameSync = new FrameSyncSimulation(start);
                _lastWorldState = _frameSync.WorldState;
            }

            Interlocked.Increment(ref _worldStateCount);
        }

        public void OnFrame(FrameSyncFrame frame)
        {
            var appliedSteps = 0;
            lock (_sync)
            {
                if (_frameSync == null)
                {
                    return;
                }

                var advance = _frameSync.SubmitFrame(frame);
                foreach (var step in advance.Steps)
                {
                    _lastWorldState = step.WorldState;
                    appliedSteps += 1;
                    if (step.MatchEnd != null)
                    {
                        _matchEnd = step.MatchEnd;
                    }
                }
            }

            if (appliedSteps > 0)
            {
                Interlocked.Add(ref _worldStateCount, appliedSteps);
            }
        }

        public void OnFrames(FrameSyncPush push)
        {
            foreach (var frame in push.Frames)
            {
                OnFrame(frame);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }

        private static RealtimeConnectionInfo? Clone(RealtimeConnectionInfo? source)
        {
            return source == null
                ? null
                : new RealtimeConnectionInfo
                {
                    Transport = source.Transport,
                    Host = source.Host,
                    Port = source.Port,
                    Path = source.Path,
                    RoomId = source.RoomId,
                    MatchId = source.MatchId,
                    SessionToken = source.SessionToken
                };
        }
    }
}
