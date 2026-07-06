#nullable enable

#if UNITY_INCLUDE_TESTS
using UnityEngine;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        public DotArenaGameTestSnapshot BuildTestSnapshot()
        {
            var realtime = _lastRealtimeConnection;
            var session = _networkSession;
            _renderStates.TryGetValue(_localPlayerId, out var localPlayer);

            return new DotArenaGameTestSnapshot
            {
                FlowState = _flowState.ToString(),
                EntryMenuState = _entryMenuState.ToString(),
                SessionMode = _sessionMode.ToString(),
                Status = _status,
                LocalPlayerId = _localPlayerId,
                IsControlConnected = session?.IsConnected ?? false,
                IsRealtimeConnected = session?.IsRealtimeConnected ?? false,
                IsConnecting = session?.IsConnecting ?? false,
                LastWorldTick = _lastWorldTick,
                ViewCount = _views.Count,
                LastRealtimeTransport = realtime?.Transport.ToString() ?? string.Empty,
                LastRealtimeHost = realtime?.Host ?? string.Empty,
                LastRealtimePort = realtime?.Port ?? 0,
                LastRealtimeRoomId = realtime?.RoomId ?? string.Empty,
                LastRealtimeMatchId = realtime?.MatchId ?? string.Empty,
                LocalPlayerX = localPlayer?.TargetPosition.x ?? 0f,
                LocalPlayerY = localPlayer?.TargetPosition.y ?? 0f
            };
        }

        public void ApplyEndpointForTest(string host, int port, string path)
        {
            _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            _port = port > 0 ? port : 20000;
            _path = string.IsNullOrWhiteSpace(path) ? "/ws" : path;
        }

        public void RequestMultiplayerMatchmakingForTest()
        {
            BeginMultiplayerMatchmaking();
        }

        public void SetEditorMoveOverrideForTest(Vector2 move)
        {
#if UNITY_EDITOR
            _editorMoveOverride = move;
            _hasEditorInputOverride = true;
#endif
        }

        public void ClearEditorMoveOverrideForTest()
        {
#if UNITY_EDITOR
            _editorMoveOverride = Vector2.zero;
            _hasEditorInputOverride = false;
#endif
        }
    }

    public sealed class DotArenaGameTestSnapshot
    {
        public string FlowState { get; set; } = string.Empty;
        public string EntryMenuState { get; set; } = string.Empty;
        public string SessionMode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string LocalPlayerId { get; set; } = string.Empty;
        public bool IsControlConnected { get; set; }
        public bool IsRealtimeConnected { get; set; }
        public bool IsConnecting { get; set; }
        public int LastWorldTick { get; set; }
        public int ViewCount { get; set; }
        public string LastRealtimeTransport { get; set; } = string.Empty;
        public string LastRealtimeHost { get; set; } = string.Empty;
        public int LastRealtimePort { get; set; }
        public string LastRealtimeRoomId { get; set; } = string.Empty;
        public string LastRealtimeMatchId { get; set; } = string.Empty;
        public float LocalPlayerX { get; set; }
        public float LocalPlayerY { get; set; }
    }
}
#endif
