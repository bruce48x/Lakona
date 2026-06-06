namespace Lakona.Game.Abstractions
{
    public enum SessionTerminationReason
    {
        ReplacedByNewLogin,
        ServerShutdown,
        Maintenance,
        Unauthorized,
        Policy,
        StateLost,
        Application
    }
}
