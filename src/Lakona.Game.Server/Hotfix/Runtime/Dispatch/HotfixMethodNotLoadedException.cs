namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixMethodNotLoadedException : Exception
{
    public HotfixMethodNotLoadedException()
    {
    }

    public HotfixMethodNotLoadedException(string message)
        : base(message)
    {
    }

    public HotfixMethodNotLoadedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
