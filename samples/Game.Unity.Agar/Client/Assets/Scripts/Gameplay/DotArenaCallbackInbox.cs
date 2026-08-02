#nullable enable

using System.Collections.Generic;
using Shared.Interfaces;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaCallbackInbox
    {
        private readonly object _gate = new();
        private FrameSyncStart? _pendingFrameSyncStart;
        private readonly Queue<FrameSyncFrame> _pendingFrames = new();
        private MatchmakingStatusUpdate? _pendingMatchmakingStatus;
        private readonly Queue<MatchProgressUpdate> _pendingMatchProgress = new();
        private string? _pendingRealtimeFallbackMessage;
        private string? _pendingDisconnectMessage;

        public void EnqueueFrameSyncStart(FrameSyncStart start)
        {
            lock (_gate)
            {
                _pendingFrameSyncStart = start;
            }
        }

        public void EnqueueFrame(FrameSyncFrame frame)
        {
            lock (_gate)
            {
                _pendingFrames.Enqueue(frame);
            }
        }

        public void EnqueueMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            lock (_gate)
            {
                _pendingMatchmakingStatus = CloneMatchmakingStatus(matchmakingStatus);
            }
        }

        public void EnqueueMatchProgress(MatchProgressUpdate update)
        {
            lock (_gate)
            {
                _pendingMatchProgress.Enqueue(new MatchProgressUpdate
                {
                    MatchId = update.MatchId,
                    RoomId = update.RoomId,
                    ServerTick = update.ServerTick,
                    RoundRemainingSeconds = update.RoundRemainingSeconds,
                    ProgressRevision = update.ProgressRevision,
                    PublishedAtUtc = update.PublishedAtUtc,
                });
            }
        }

        public void EnqueueDisconnected(string? disconnectMessage)
        {
            lock (_gate)
            {
                _pendingDisconnectMessage = disconnectMessage;
            }
        }

        public void EnqueueRealtimeFallback(string message)
        {
            lock (_gate)
            {
                _pendingRealtimeFallbackMessage = message;
            }
        }

        public DrainedCallbacks Drain()
        {
            FrameSyncStart? frameSyncStart;
            var frames = new List<FrameSyncFrame>();
            MatchmakingStatusUpdate? matchmakingStatus;
            var matchProgress = new List<MatchProgressUpdate>();
            string? realtimeFallbackMessage;
            string? disconnectMessage;

            lock (_gate)
            {
                frameSyncStart = _pendingFrameSyncStart;
                _pendingFrameSyncStart = null;
                while (_pendingFrames.Count > 0)
                {
                    frames.Add(_pendingFrames.Dequeue());
                }

                matchmakingStatus = _pendingMatchmakingStatus;
                _pendingMatchmakingStatus = null;
                while (_pendingMatchProgress.Count > 0)
                {
                    matchProgress.Add(_pendingMatchProgress.Dequeue());
                }

                realtimeFallbackMessage = _pendingRealtimeFallbackMessage;
                _pendingRealtimeFallbackMessage = null;

                disconnectMessage = _pendingDisconnectMessage;
                _pendingDisconnectMessage = null;
            }

            frames.Sort(static (left, right) => left.Frame.CompareTo(right.Frame));
            return new DrainedCallbacks(frameSyncStart, frames, matchmakingStatus, matchProgress, realtimeFallbackMessage, disconnectMessage);
        }

        public void Clear()
        {
            lock (_gate)
            {
                _pendingFrameSyncStart = null;
                _pendingFrames.Clear();
                _pendingMatchmakingStatus = null;
                _pendingMatchProgress.Clear();
                _pendingRealtimeFallbackMessage = null;
                _pendingDisconnectMessage = null;
            }
        }

        private static MatchmakingStatusUpdate CloneMatchmakingStatus(MatchmakingStatusUpdate source)
        {
            return new MatchmakingStatusUpdate
            {
                State = source.State,
                Message = source.Message,
                RoomId = source.RoomId,
                QueuePosition = source.QueuePosition,
                QueueSize = source.QueueSize,
                RoomCapacity = source.RoomCapacity,
                MatchedPlayerCount = source.MatchedPlayerCount,
                RealtimeConnection = source.RealtimeConnection is null
                    ? null
                    : new RealtimeConnectionInfo
                    {
                        Transport = source.RealtimeConnection.Transport,
                        Host = source.RealtimeConnection.Host,
                        Port = source.RealtimeConnection.Port,
                        Path = source.RealtimeConnection.Path,
                        RoomId = source.RealtimeConnection.RoomId,
                        MatchId = source.RealtimeConnection.MatchId,
                        SessionToken = source.RealtimeConnection.SessionToken
                    }
            };
        }
    }

    internal readonly struct DrainedCallbacks
    {
        public DrainedCallbacks(FrameSyncStart? frameSyncStart, List<FrameSyncFrame> frames, MatchmakingStatusUpdate? matchmakingStatus, List<MatchProgressUpdate> matchProgress, string? realtimeFallbackMessage, string? disconnectedMessage)
        {
            FrameSyncStart = frameSyncStart;
            Frames = frames;
            MatchmakingStatus = matchmakingStatus;
            MatchProgress = matchProgress;
            RealtimeFallbackMessage = realtimeFallbackMessage;
            DisconnectedMessage = disconnectedMessage;
        }

        public FrameSyncStart? FrameSyncStart { get; }
        public List<FrameSyncFrame> Frames { get; }
        public MatchmakingStatusUpdate? MatchmakingStatus { get; }
        public List<MatchProgressUpdate> MatchProgress { get; }
        public string? RealtimeFallbackMessage { get; }
        public string? DisconnectedMessage { get; }
    }
}
