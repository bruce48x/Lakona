namespace Lakona.Game.Client.Sessions
{
    public enum ClientSessionPhase
    {
        SignedOut,
        Connecting,
        Ready,
        Active,
        Reconnecting,
        RefreshRequired,
        StateLost,
        Terminated,
        ConnectionFailed
    }
}
