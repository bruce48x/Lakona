#nullable enable

using Shared.Interfaces;
using Lakona.Game.Client.Sessions;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaMultiplayerState
    {
        public DotArenaMultiplayerState()
        {
            SessionController = new ClientSessionController();
        }

        public SessionMode SessionMode { get; set; } = SessionMode.None;
        public string LocalPlayerId { get; set; } = string.Empty;
        public bool HasAuthenticatedProfile { get; set; }
        public string AuthenticatedPlayerId { get; set; } = string.Empty;
        public int LocalWinCount { get; set; }
        public PendingUiRequest PendingUiRequest { get; set; }
        public float MatchmakingStartedAt { get; set; } = -1f;
        public RealtimeConnectionInfo? LastRealtimeConnection { get; set; }
        public ClientSessionController SessionController { get; }

        public bool HasPendingUiRequest => PendingUiRequest != PendingUiRequest.None;
        public bool HasAuthenticatedMultiplayerProfile => SessionMode == SessionMode.Multiplayer && HasAuthenticatedProfile;

        public DotArenaAuthenticatedProfile CaptureAuthenticatedProfile()
        {
            return new DotArenaAuthenticatedProfile(HasAuthenticatedProfile, AuthenticatedPlayerId, LocalWinCount);
        }

        public void ClearAuthenticatedProfile()
        {
            HasAuthenticatedProfile = false;
            AuthenticatedPlayerId = string.Empty;
            LocalWinCount = 0;
        }

        public void ApplyAuthenticatedProfile(string playerId, int winCount)
        {
            HasAuthenticatedProfile = true;
            AuthenticatedPlayerId = playerId;
            LocalWinCount = winCount < 0 ? 0 : winCount;
        }

        public void RestoreAuthenticatedProfile(DotArenaAuthenticatedProfile profile)
        {
            HasAuthenticatedProfile = profile.HasAuthenticatedProfile;
            AuthenticatedPlayerId = profile.PlayerId;
            LocalWinCount = profile.WinCount;
        }

        public void ApplyMultiplayerLogin(string playerId, string sessionToken, string sessionId, long sessionGeneration, int winCount)
        {
            LocalPlayerId = playerId;
            SessionMode = SessionMode.Multiplayer;
            ApplyAuthenticatedProfile(playerId, winCount);
            StartFrameworkSession(playerId, sessionToken, sessionId, sessionGeneration);
        }

        public void ClearSession()
        {
            SessionMode = SessionMode.None;
            LocalPlayerId = string.Empty;
        }

        public void ClearAll()
        {
            ClearSession();
            ClearAuthenticatedProfile();
            ClearRequestState(resetSessionState: true);
        }

        public void ClearRequestState(bool resetSessionState)
        {
            PendingUiRequest = PendingUiRequest.None;
            LastRealtimeConnection = null;
            MatchmakingStartedAt = -1f;

            if (resetSessionState)
            {
                SessionController.EndSession();
            }
        }

        public void MarkSessionStateLost()
        {
            SessionController.MarkStateLost();
        }

        private void StartFrameworkSession(string playerId, string sessionToken, string sessionId, long sessionGeneration)
        {
            var frameworkSessionId = string.IsNullOrWhiteSpace(sessionId)
                ? string.IsNullOrWhiteSpace(sessionToken) ? playerId : sessionToken
                : sessionId;
            var generation = sessionGeneration <= 0 ? 1 : sessionGeneration;
            SessionController.StartSession($"{frameworkSessionId}:{generation}");
        }
    }

    internal readonly struct DotArenaAuthenticatedProfile
    {
        public DotArenaAuthenticatedProfile(bool hasAuthenticatedProfile, string playerId, int winCount)
        {
            HasAuthenticatedProfile = hasAuthenticatedProfile;
            PlayerId = playerId;
            WinCount = winCount;
        }

        public bool HasAuthenticatedProfile { get; }
        public string PlayerId { get; }
        public int WinCount { get; }
    }
}
