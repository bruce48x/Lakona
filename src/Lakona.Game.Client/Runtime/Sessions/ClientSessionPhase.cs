namespace Lakona.Game.Client.Sessions
{
    public enum ClientSessionPhase
    {
        SignedOut,
        Connecting,
        Active,
        Reconnecting,
        RefreshRequired,
        StateLost,
        Terminated
    }
}
